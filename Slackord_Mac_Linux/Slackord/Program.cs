using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using Microsoft.Extensions.DependencyInjection;
using Discord.Net;
using System.CommandLine;
using System.Globalization;
using System.Text.RegularExpressions;
using Octokit;

namespace Slackord
{
    internal class Slackord : InteractionModuleBase<SocketInteractionContext>
    {
        private const string CurrentVersion = "v2.3";
        private DiscordSocketClient _discordClient;
        private string _discordToken;
        private bool _isFileParsed;
        private IServiceProvider _services;
        private JArray parsed;
        private readonly List<SlackMessage> Responses = new();
        private readonly List<FileInfo> ListOfFilesToParse = new();
        private readonly CultureInfo locale;
        private DirectoryInfo filesFolder;
        private FileInfo usersFile;
        private readonly bool dryRun;
        private UserService userService;

        static async Task<int> Main(string[] args)
        {
            var localeOption = new Option<string>(
                name: "--locale",
                getDefaultValue: () => "en-US",
                description: "The locale used to format date and time"
            );

            var filesOption = new Option<DirectoryInfo>(
                name: "--files-folder",
                getDefaultValue: () => new DirectoryInfo("Files"),
                description: "The folder to look for Slack export files ('~' for home doesn't work)"
            );

            var usersFileOption = new Option<FileInfo>(
                name: "--users-file",
                description: "Users file from Slack export (tries to resolve users from message export by default)"
            );

            var dryRunOption = new Option<bool>(
                name: "--dry-run",
                getDefaultValue: () => false,
                description: "Don't upload files to Discord"
            );

            var rootCommand = new RootCommand("Slackord2");
            rootCommand.AddOption(localeOption);
            rootCommand.AddOption(filesOption);
            rootCommand.AddOption(usersFileOption);
            rootCommand.AddOption(dryRunOption);

            rootCommand.SetHandler((localeArg, filesFolderArg, usersFileArg, dryRunArg) => 
                {
                    new Slackord(localeArg, filesFolderArg, usersFileArg, dryRunArg).Start();
                },
                localeOption, filesOption, usersFileOption, dryRunOption);

            return await rootCommand.InvokeAsync(args);
        }

        public Slackord(string locale, DirectoryInfo filesFolder, FileInfo usersFile, bool dryRun)
        {
            _isFileParsed = false;

            this.locale = new CultureInfo(locale);
            this.filesFolder = filesFolder;
            this.usersFile = usersFile;
            this.dryRun = dryRun;

            if (filesFolder != null && !filesFolder.Attributes.HasFlag(FileAttributes.Directory))
            {
                Console.WriteLine(filesFolder + " is a file, but must be a directory or not exist. Exiting!");
                System.Environment.Exit(1);
            }

            if (usersFile != null && !usersFile.Exists)
            {
                Console.WriteLine(usersFile + " does not exist. Exiting!");
                System.Environment.Exit(1);
            }
            userService = UserService.fromFile(usersFile);
        }

        public async void Start()
        {
            AboutSlackord();
            CheckForExistingBotToken();
            CheckForFilesFolder();
        }

        private Task CheckForFilesFolder()
        {
            if (!filesFolder.Exists)
            {
                filesFolder.Create();
            }
            return Task.CompletedTask;
        }

        public static async Task AboutSlackord()
        {
            Console.WriteLine("Slackord " + CurrentVersion + ".\n" +
                "Created by Thomas Loupe." + "\n" +
                "Github: https://github.com/thomasloupe" + "\n" +
                "Twitter: https://twitter.com/acid_rain" + "\n" +
                "Website: https://thomasloupe.com" + "\n");

            Console.WriteLine("Slackord will always be free!\n"
                + "If you'd like to buy me a beer anyway, I won't tell you not to!\n"
                + "You can donate at https://www.paypal.me/thomasloupe\n" + "\n"); ;
            await CheckForUpdates();
        }

        private static async Task CheckForUpdates()
        {
            var updateCheck = new GitHubClient(new ProductHeaderValue("Slackord2"));
            var releases = await updateCheck.Repository.Release.GetAll("thomasloupe", "Slackord2");
            var latest = releases[0];
            if (CurrentVersion == latest.TagName)
            {
                Console.WriteLine("You have the latest version, " + CurrentVersion + "!");
            }
            else if (CurrentVersion != latest.TagName)
            {
                Console.WriteLine("A new version of Slackord is available!\n"
                    + "Current version: " + CurrentVersion + "\n"
                    + "Latest version: " + latest.TagName + "\n"
                    + "You can get the latest version from the GitHub repository at https://github.com/thomasloupe/Slackord2");
            }
        }

        private void CheckForExistingBotToken()
        {
            if (File.Exists("Token.txt"))
            {
                _discordToken = File.ReadAllText("Token.txt").Trim();
                Console.WriteLine("Found existing token file.");
                if (_discordToken.Length == 0 || string.IsNullOrEmpty(_discordToken))
                {
                    Console.WriteLine("No bot token found. Please enter your bot token: ");
                    _discordToken = Console.ReadLine();
                    File.WriteAllText("Token.txt", _discordToken);
                    CheckForExistingBotToken();
                }
                else
                {
                    ParseJsonFiles();
                }
            }
            else
            {
                Console.WriteLine("No bot token found. Please enter your bot token:");
                _discordToken = Console.ReadLine();
                if (_discordToken == null)
                {
                    CheckForExistingBotToken();
                }
                else
                {
                    File.WriteAllText("Token.txt", _discordToken);
                }
            }

        }

        private string FixMessageMarkup(string slackMessage)
        {
            slackMessage = Regex.Replace(slackMessage, @"\<(.*)\|(.*)\>", "$2 ($1)"); // <url|text> -> text (url)
            return slackMessage.Replace("&gt;", ">"); // quote
        }

        [STAThread]
        private async Task ParseJsonFiles()
        {
            Console.WriteLine("Reading JSON files directory...");
            try
            {
                var files = filesFolder.GetFiles();
                if (files.Length == 0)
                {
                    Console.WriteLine("You haven't placed any JSON files in the Files folder.\n" +
                        "Please place your JSON files in the Files folder then press ENTER to continue.");
                    ConsoleKeyInfo keyPressed = Console.ReadKey(true);
                    if (keyPressed.Key != ConsoleKey.Enter)
                    {
                        ParseJsonFiles();
                    }
                }
                else
                {
                    Console.WriteLine("Found " + files.Length + " files in the Files folder.");
                    foreach (var file in files)
                    {
                        ListOfFilesToParse.Add(file);
                    }
                }

                foreach (var file in ListOfFilesToParse)
                {
                    try
                    {
                        var json = File.ReadAllText(file.FullName);
                        parsed = JArray.Parse(json);
                        Console.WriteLine("Begin parsing JSON data..." + "\n");
                        Console.WriteLine("-----------------------------------------" + "\n");
                        string debugResponse;
                        foreach (JObject pair in parsed.Cast<JObject>())
                        {
                            if (pair.ContainsKey("files"))
                            {
                                string messageText = null;

                                if (pair["files"][0]["thumb_1024"] != null)
                                {
                                    messageText = FixMessageMarkup(pair["text"].ToString()) + "\n" + pair["files"][0]["thumb_1024"].ToString() + "\n";
                                } else if (pair["files"][0]["url_private"] != null) {
                                    messageText = FixMessageMarkup(pair["text"].ToString()) + "\n" + pair["files"][0]["url_private"].ToString() + "\n";
                                } else {
                                    messageText = "Skipped a tombstoned file attachement." + "\n";
                                }
                                var slackMessage = new SlackMessage(
                                    (pair["id"]?.ToString()),
                                    ConvertFromUnixTimestampToHumanReadableTime((double) pair["ts"]),
                                    userService.userById(pair["user"].ToString()),
                                    messageText
                                );
                                Responses.Add(slackMessage);
                                Console.WriteLine(slackMessage.Render(locale) + "\n");
                            }
                            else if (pair.ContainsKey("bot_profile"))
                            {
                                string botId = null;
                                if (pair["bot_profile"]["name"] != null)
                                {
                                    botId = pair["bot_profile"]["name"].ToString(); 
                                }
                                else if (pair["bot_id"] != null)
                                {
                                    botId = pair["bot_profile"]["name"].ToString();
                                }

                                if (botId != null)
                                {
                                    var slackMessage = new SlackMessage(
                                    (pair["id"].ToString()),
                                    ConvertFromUnixTimestampToHumanReadableTime((long) pair["ts"]),
                                    new User(botId),
                                    FixMessageMarkup(pair["text"].ToString())
                                    );
                                    Responses.Add(slackMessage);
                                    Console.WriteLine(slackMessage.Render(locale) + "\n");
                                }
                                else
                                {
                                    Console.WriteLine("A bot message was ignored. Please submit an issue on Github for this.\n");
                                }
                            }
                            else if (pair.ContainsKey("user_profile") && pair.ContainsKey("text"))
                            {
                                var user = new User(
                                    pair["user_profile"]["id"]?.ToString(),
                                    pair["user_profile"]["real_name"].ToString(),
                                    pair["user_profile"]["display_name"]?.ToString()
                                );

                                var slackMessage = new SlackMessage(
                                    pair["id"]?.ToString(),
                                    ConvertFromUnixTimestampToHumanReadableTime((double) pair["ts"]),
                                    user,
                                    FixMessageMarkup(pair["text"].ToString())
                                );

                                Responses.Add(slackMessage);
                                Console.WriteLine(slackMessage.Render(locale) + "\n");
                            }
                        }
                        Console.WriteLine("\n");
                        Console.WriteLine("-----------------------------------------" + "\n");
                        Console.WriteLine("Parsing of " + file + " completed successfully!" + "\n");
                        Console.WriteLine("-----------------------------------------" + "\n");
                        Console.WriteLine("\n");
                        if (_discordClient != null)
                        {
                            await _discordClient.SetActivityAsync(new Game("awaiting command to import messages...", ActivityType.Watching));
                        }
                        _isFileParsed = true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex);
                        Console.WriteLine("An error occured while parsing the JSON file " + file + ". Please try again.");
                        System.Environment.Exit(1);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Error encountered in input " + e.Message);
            }

            if (!dryRun)
            {
                Console.WriteLine("Bot will now attempt to connect to the Discord server...");
                await MainAsync();
            }
        }

        public async Task MainAsync()
        {
            try
            {
                var thread = new Thread(() => { while (true) Thread.Sleep(5000); }); thread.Start();
                _discordClient = new DiscordSocketClient();
                DiscordSocketConfig _config = new();
                {
                    _config.GatewayIntents = GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds;
                }
                _discordClient = new(_config);
                _services = new ServiceCollection()
                .AddSingleton(_discordClient)
                .BuildServiceProvider();
                _discordClient.Log += DiscordClient_Log;
                await _discordClient.LoginAsync(TokenType.Bot, _discordToken);
                await _discordClient.StartAsync();
                await _discordClient.SetActivityAsync(new Game("awaiting parsing of messages.", ActivityType.Watching));
                _discordClient.Ready += ClientReady;
                _discordClient.SlashCommandExecuted += SlashCommandHandler;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Discord bot task failed with: " + ex.Message);
            }
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            if (command.Data.Name.Equals("slackord"))
            {
                var guildID = _discordClient.Guilds.FirstOrDefault().Id;
                var channel = _discordClient.GetChannel((ulong)command.ChannelId);
                await PostMessages(channel, guildID);
            }
        }

        private async Task ClientReady()
        {
            var guildID = _discordClient.Guilds.FirstOrDefault().Id;
            var guild = _discordClient.GetGuild(guildID);
            var guildCommand = new SlashCommandBuilder();
            guildCommand.WithName("slackord");
            guildCommand.WithDescription("Posts all parsed Slack JSON messages to the text channel the command came from.");
            try
            {
                await guild.CreateApplicationCommandAsync(guildCommand.Build());
            }
            catch (HttpException Ex)
            {
                Console.WriteLine(Ex.Message);
            }
        }

        private static DateTime ConvertFromUnixTimestampToHumanReadableTime(double timestamp)
        {
            var date = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var returnDate = date.AddSeconds(timestamp);
            return returnDate;
        }

        private async Task DiscordClient_Log(LogMessage arg)
        {
            Console.WriteLine(arg.ToString() + "\n");
            await Task.CompletedTask;
        }

        [SlashCommand("slackord", "Posts all parsed Slack JSON messages to the text channel the command came from.")]
        private async Task PostMessages(SocketChannel channel, ulong guildID)
        {
            await _discordClient.SetActivityAsync(new Game("posting messages...", ActivityType.Watching));
            // TODO: Fix Application did not respond in time error.
            //await DeferAsync();
            if (_isFileParsed)
            {
                Console.WriteLine("\n");
                Console.WriteLine("Beginning transfer of Slack messages to Discord..." + "\n" +
                "-----------------------------------------" + "\n");
                foreach (SlackMessage message in Responses)
                {
                    foreach (string part in message.Parts(locale))
                    {
                        await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync(part).ConfigureAwait(false);
                    }
                }
                Console.WriteLine("-----------------------------------------" + "\n" +
                    "All messages sent to Discord successfully!" + "\n");
                // TODO: Fix Application did not respond in time error.
                //await FollowupAsync("All messages sent to Discord successfully!", ephemeral: true);
                await _discordClient.SetActivityAsync(new Game("awaiting parsing of messages.", ActivityType.Watching));
            }
            else if (!_isFileParsed)
            {
                await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync("Sorry, there's nothing to post because no JSON file was parsed prior to sending this command.").ConfigureAwait(false);
                Console.WriteLine("Received a command to post messages to Discord, but no JSON file was parsed prior to receiving the command." + "\n");
            }
            await _discordClient.SetActivityAsync(new Game("for the Slackord command...", ActivityType.Listening));
            Responses.Clear();
        }
    }
    static class StringExtensions
    {
        public static IEnumerable<string> SplitInParts(this string s, int partLength)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));
            if (partLength <= 0)
                throw new ArgumentException("Invalid char length specified.", nameof(partLength));

            for (var i = 0; i < s.Length; i += partLength)
                yield return s.Substring(i, Math.Min(partLength, s.Length - i));
        }
    }

    class SlackMessage
    {
        public SlackMessage(string id, DateTime timestamp, User user, string message){
            this.Id = id;
            this.Timestamp = timestamp;
            this.User = user;
            this.Message = message;
        }
        public string Id { init; get; }
        public DateTime Timestamp { init; get; }
        public User User { init; get; }
        public string Message { init; get; }

        public string Render(CultureInfo locale)
        {
            return Timestamp.ToString(locale) + " - " + User.Render() + ": " + Message; 
        }

        public List<string> Parts(CultureInfo locale)
        {
            var unsplitMessage = Render(locale);
            if (unsplitMessage.Length < 2000)
            {
                return new List<string> { unsplitMessage };
            }
            else
            {
                Console.WriteLine("The following parse is over 2000 characters. Discord does not allow messages over 2000 characters. This message " +
                                  "will be split into multiple posts. The message that will be split is:\n" + unsplitMessage);

                int lastIndex = Message.Length > 3800 ? 3800 : Message.Length;
                string part1 = Timestamp.ToString(locale) + " - " + User.Render() + ": (1/2) " + Message.Substring(0, 1900);
                string part2 = Timestamp.ToString(locale) + " - " + User.Render() + ": (2/2) " + Message.Substring(1900, lastIndex - 1900);
                
                if (Message.Length > 3800)
                {
                    Console.WriteLine("Message too long to split in two parts. Content will be lost.");
                }
                return new List<string> { part1, part2 };
            }
        }
    }
}
