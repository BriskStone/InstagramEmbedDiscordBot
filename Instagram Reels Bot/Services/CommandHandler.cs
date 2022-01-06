﻿using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using OpenGraphNet;
using System.Linq;
using Instagram_Reels_Bot.Modules;
using Discord.Interactions;
using Instagram_Reels_Bot.Helpers;

namespace Instagram_Reels_Bot.Services
{
    public class CommandHandler
    {
        // setup fields to be set later in the constructor
        private readonly IConfiguration _config;
        private readonly CommandService _commands;
        private readonly InteractionService _interact;
        public readonly DiscordShardedClient _client;
        private readonly IServiceProvider _services;
        //private readonly Subscriptions _subscriptions;
        /// <summary>
        /// Notifies the owner of an error
        /// false by default. Toggled by user DM command.
        /// Reverts to false when bot is restarted.
        /// </summary>
        public static bool notifyOwnerOnError = false;

        public CommandHandler(IServiceProvider services)
        {
            // juice up the fields with these services
            // since we passed the services in, we can use GetRequiredService to pass them into the fields set earlier
            _config = services.GetRequiredService<IConfiguration>();
            _commands = services.GetRequiredService<CommandService>();
            _interact = services.GetRequiredService<InteractionService>();
            _client = services.GetRequiredService<DiscordShardedClient>();
            _services = services;
            //_subscriptions = subs;

            // take action when we execute a command
            _commands.CommandExecuted += CommandExecutedAsync;

            // take action when we receive a message (so we can process it, and see if it is a valid command)
            _client.MessageReceived += MessageReceivedAsync;

            // Process the InteractionCreated payloads to execute Interactions commands
            _client.InteractionCreated += HandleInteraction;

            //Slash Commands:
            _interact.SlashCommandExecuted += SlashCommandExecuted;
            _interact.ContextCommandExecuted += ContextCommandExecuted;
            _interact.ComponentCommandExecuted += ComponentCommandExecuted;

        }

        public async Task InitializeAsync()
        {
            // register modules that are public and inherit ModuleBase<T>.
            await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
            await _interact.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
        }

        // this class is where the magic starts, and takes actions upon receiving messages
        public async Task MessageReceivedAsync(SocketMessage rawMessage)
        {
            // ensures we don't process system/other bot messages
            if (!(rawMessage is SocketUserMessage message))
            {
                return;
            }

            if (message.Source != MessageSource.User)
            {
                return;
            }
            //DMs:
            if (message.Channel.GetType() == typeof(SocketDMChannel))
            {
                //TODO: Move this to a module file:
                if (message.Content.ToLower().StartsWith("debug"))
                {
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        //Server count:
                        await message.ReplyAsync("Server Count: " + _client.Guilds.Count);

                        //IP check:
                        try
                        {
                            OpenGraph graph = OpenGraph.ParseUrl("https://api.ipify.org/", "");
                            await message.ReplyAsync("IP: " + graph.OriginalHtml);
                        }
                        catch (Exception e)
                        {
                            await message.ReplyAsync("Could not connect to server. Error: " + e);
                        }
                    }
                }
                else if (message.Content.ToLower().StartsWith("guilds"))
                {
                    //Guild list
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        string serverList = Format.Bold("Servers:");
                        foreach (SocketGuild guild in _client.Guilds)
                        {
                            String serverLine = "\n" + guild.Name + " \tBoost: " + guild.PremiumTier + " \tUsers: " + guild.MemberCount + " \tLocale: " + guild.PreferredLocale;
                            //Discord max message length:
                            if (serverList.Length + serverLine.Length > 2000)
                            {
                                await message.ReplyAsync(serverList);
                                serverList = "";
                            }
                            serverList += serverLine;
                        }
                        await message.ReplyAsync(serverList);
                    }
                }
                else if (message.Content.ToLower().StartsWith("toggle error"))
                {
                    //toggle error DMs
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        notifyOwnerOnError = !notifyOwnerOnError;
                        if (notifyOwnerOnError)
                        {
                            await message.ReplyAsync("Error notifications enabled.");
                        }
                        else
                        {
                            await message.ReplyAsync("Error notifications disabled.");
                        }
                    }
                }
                else if (message.Content.ToLower().StartsWith("users"))
                {
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        int users = 0;
                        foreach (SocketGuild guild in _client.Guilds)
                        {
                            users += guild.MemberCount;
                        }
                        await message.ReplyAsync("Users: " + users);
                    }
                }
                else if (message.Content.ToLower().StartsWith("relogin"))
                {
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        //Clear login information and relogin:
                        InstagramProcessor.InstagramLogin(true, true);
                        await message.ReplyAsync("Success");
                    }
                }
                else if (message.Content.ToLower().StartsWith("2fa"))
                {
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        //Clear login information and relogin:
                        try
                        {
                            var code = InstagramProcessor.GetTwoFactorAuthCode();
                            await message.ReplyAsync("Username: " + InstagramProcessor.GetIGUsername() + "\n2FA Code: " + code);
                        }
                        catch (Exception e)
                        {
                            await message.ReplyAsync("Failed to get 2FA code.");
                            Console.WriteLine("2FA Code error: " + e);
                        }
                    }
                }
                else if (message.Content.ToLower().StartsWith("sync"))
                {
                    if (!string.IsNullOrEmpty(_config["OwnerID"]) && message.Author.Id == ulong.Parse(_config["OwnerID"]))
                    {
                        //_subscriptions.GetLatestsPosts();
                        await message.ReplyAsync("Working on it...");
                    }
                }
                return;
            }

            // sets the argument position away from the prefix we set
            int argPos = 0;
            int endUrlLength = 0;
            bool foundPrefix = false;

            // get each prefix from the configuration file
            foreach (string prefix in _config.GetSection("Prefix").GetChildren().ToArray().Select(c => c.Value).ToArray())
            {
                //check for valid prefix:
                if (message.Content.Contains(prefix))
                {
                    argPos = message.Content.IndexOf(prefix) + prefix.Length;
                    endUrlLength = message.Content.Substring(argPos).Replace("\n"," ").IndexOf(" ");
                    foundPrefix = true;
                    break;
                }
            }
            if (!foundPrefix)
            {
                return;
            }
            
            var context = new ShardedCommandContext(_client, message);

            //create new string from command
            string commandText;
            if (endUrlLength <= 0)
            {
                commandText = message.Content.Substring(argPos).Replace("/", " ");
            }
            else
            {
                commandText = message.Content.Substring(argPos, endUrlLength).Replace("/", " ");
            }

            

            //Split url down to params:
            String[] userInput = commandText.Split(" ");

            foreach(CommandInfo command in _commands.Commands)
            {
                if (command.Name.Equals(userInput[0]))
                {
                    await _commands.ExecuteAsync(context, commandText, _services);
                }
                else if (command.Name.Equals(userInput[1]))
                {
                    commandText=commandText.Replace(userInput[0]+" ", "");
                    Console.WriteLine(commandText);
                    await _commands.ExecuteAsync(context, commandText, _services);
                }
            }
        }
        /// <summary>
        /// Handles text command success and failures.
        /// </summary>
        /// <param name="command"></param>
        /// <param name="context"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        public async Task CommandExecutedAsync(Optional<CommandInfo> command, ICommandContext context, Discord.Commands.IResult result)
        {
            // if a command isn't found, log that info to console and exit this method
            if (!command.IsSpecified)
            {
                System.Console.WriteLine($"Command not found");
                return;
            }


            // log success to the console and exit this method
            if (result.IsSuccess)
            {
                System.Console.WriteLine($"Command Executed.");
                return;
            }
            else if(result.ErrorReason.Contains("Missing Permissions"))
            {
                await context.Channel.SendMessageAsync($"I do not have permission to carry out this action. I need permission to Send Messages, Embed Links, Attach Files, Add Reactions, Read Message History, and Manage Messages in this channel.");
                return;
            }else if(result.ErrorReason.Contains("Cannot reply without permission to read message history"))
            {
                await context.Channel.SendMessageAsync("I need permission to read message history in order to reply to a message and carry out the command.");
                return;
            }
            else if (result.ErrorReason.Contains("timed out"))
            {
                await context.Channel.SendMessageAsync($"Your command timed out. This may be because the bot is overloaded. Please try again later.");
                //report to owner anyway.
            }
            else
            {
                // failure scenario, let's let the user know
                await context.Channel.SendMessageAsync($"Sorry, Something went wrong... Discord support server: https://top.gg/servers/921830686439124993");
            }

            //notify owner if desired:
            if (notifyOwnerOnError&&!string.IsNullOrEmpty(_config["OwnerID"]))
            {
                string error = Format.Bold("Error:") + "\n" + result.Error + "\n" + Format.Code(result.ErrorReason)+"\n\n"+ Format.Bold("Command:") + "\n" + Format.BlockQuote(context.Message.Content);
                if (error.Length > 2000)
                {
                    error = error.Substring(0, 2000);
                }
                await Discord.UserExtensions.SendMessageAsync(_client.GetUser(ulong.Parse(_config["OwnerID"])), error);
            }

        }
        /// <summary>
        /// Handles success and failures from slash commands.
        /// </summary>
        /// <param name="arg1"></param>
        /// <param name="arg2"></param>
        /// <param name="arg3"></param>
        /// <returns></returns>
        private Task SlashCommandExecuted(SlashCommandInfo arg1, Discord.IInteractionContext arg2, Discord.Interactions.IResult arg3)
        {
            if (!arg3.IsSuccess)
            {
                Console.WriteLine("Error: " + arg3.Error);
                switch (arg3.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        // implement
                        break;
                    case InteractionCommandError.UnknownCommand:
                        // implement
                        break;
                    case InteractionCommandError.BadArgs:
                        // implement
                        break;
                    case InteractionCommandError.Exception:
                        //notify owner if desired:
                        if (notifyOwnerOnError && !string.IsNullOrEmpty(_config["OwnerID"]))
                        {
                            string error = Format.Bold("Error:") + "\n" + Format.Code(arg3.ErrorReason) + "\n\n" + Format.Bold("Command:") + "\n" + Format.BlockQuote(arg1.Name + " " + arg1.Parameters);
                            if (error.Length > 2000)
                            {
                                error = error.Substring(0, 2000);
                            }
                            Discord.UserExtensions.SendMessageAsync(_client.GetUser(ulong.Parse(_config["OwnerID"])), error);
                        }
                        break;
                    case InteractionCommandError.Unsuccessful:
                        //notify owner if desired:
                        if (notifyOwnerOnError && !string.IsNullOrEmpty(_config["OwnerID"]))
                        {
                            string error = Format.Bold("Error:") + "\n" + Format.Code(arg3.ErrorReason) + "\n\n" + Format.Bold("Command:") + "\n" + Format.BlockQuote(arg1.Name + " " + arg1.Parameters);
                            if (error.Length > 2000)
                            {
                                error = error.Substring(0, 2000);
                            }
                            Discord.UserExtensions.SendMessageAsync(_client.GetUser(ulong.Parse(_config["OwnerID"])), error);
                        }
                        break;
                    default:
                        //notify owner if desired:
                        if (notifyOwnerOnError && !string.IsNullOrEmpty(_config["OwnerID"]))
                        {
                            string error = Format.Bold("Error:") + "\n" + Format.Code(arg3.ErrorReason) + "\n\n" + Format.Bold("Command:") + "\n" + Format.BlockQuote(arg1.Name + " " + arg1.Parameters);
                            if (error.Length > 2000)
                            {
                                error = error.Substring(0, 2000);
                            }
                            Discord.UserExtensions.SendMessageAsync(_client.GetUser(ulong.Parse(_config["OwnerID"])), error);
                        }
                        break;
                }
            }

            return Task.CompletedTask;
        }
        private Task ComponentCommandExecuted(ComponentCommandInfo arg1, Discord.IInteractionContext arg2, Discord.Interactions.IResult arg3)
        {
            if (!arg3.IsSuccess)
            {
                switch (arg3.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        // implement
                        break;
                    case InteractionCommandError.UnknownCommand:
                        // implement
                        break;
                    case InteractionCommandError.BadArgs:
                        // implement
                        break;
                    case InteractionCommandError.Exception:
                        // implement
                        break;
                    case InteractionCommandError.Unsuccessful:
                        // implement
                        break;
                    default:
                        break;
                }
            }

            return Task.CompletedTask;
        }

        private Task ContextCommandExecuted(ContextCommandInfo arg1, Discord.IInteractionContext arg2, Discord.Interactions.IResult arg3)
        {
            if (!arg3.IsSuccess)
            {
                switch (arg3.Error)
                {
                    case InteractionCommandError.UnmetPrecondition:
                        // implement
                        break;
                    case InteractionCommandError.UnknownCommand:
                        // implement
                        break;
                    case InteractionCommandError.BadArgs:
                        // implement
                        break;
                    case InteractionCommandError.Exception:
                        // implement
                        break;
                    case InteractionCommandError.Unsuccessful:
                        // implement
                        break;
                    default:
                        break;
                }
            }

            return Task.CompletedTask;
        }
        private async Task HandleInteraction(SocketInteraction arg)
        {
            Console.WriteLine("Slash Command Executed");
            try
            {
                // Create an execution context that matches the generic type parameter of your InteractionModuleBase<T> modules
                var ctx = new ShardedInteractionContext(_client, arg);
                await _interact.ExecuteCommandAsync(ctx, _services);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);

                // If a Slash Command execution fails it is most likely that the original interaction acknowledgement will persist. It is a good idea to delete the original
                // response, or at least let the user know that something went wrong during the command execution.
                if (arg.Type == InteractionType.ApplicationCommand)
                    await arg.GetOriginalResponseAsync().ContinueWith(async (msg) => await msg.Result.DeleteAsync());
            }
        }
    }
}
