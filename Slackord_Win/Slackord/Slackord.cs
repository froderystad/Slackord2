﻿// Slackord2 - Written by Thomas Loupe
// https://github.com/thomasloupe/Slackord2
// https://thomasloupe.com

using System.IO;
using Discord;
using Discord.WebSocket;
using Newtonsoft.Json.Linq;
using octo = Octokit;
using Microsoft.Extensions.DependencyInjection;
using MaterialSkin;
using MaterialSkin.Controls;
using System.Diagnostics;
using Application = System.Windows.Forms.Application;
using Label = System.Windows.Forms.Label;
using Discord.Net;
using Octokit;
using Discord.Rest;

namespace Slackord
{
    public partial class Slackord : MaterialForm
    {
        private const string CurrentVersion = "v2.4";
        public DiscordSocketClient _discordClient;
        private OpenFileDialog _ofd;
        private string _discordToken;
        private octo.GitHubClient _octoClient;
        public bool _isFileParsed;
        public IServiceProvider _services;
        public JArray parsed;
        private readonly List<string> Responses = new();
        private readonly List<string> ListOfFilesToParse = new();
        private readonly List<bool> isThreadMessages = new();
        private readonly List<bool> isThreadStart = new();

        public Slackord()
        {
            InitializeComponent();
            SetWindowSizeAndLocation();
            var materialSkinManager = MaterialSkinManager.Instance;
            materialSkinManager.AddFormToManage(this);
            materialSkinManager.Theme = MaterialSkinManager.Themes.DARK;
            materialSkinManager.ColorScheme = new ColorScheme(Primary.Orange600, Primary.DeepOrange700, Primary.Amber500, Accent.Amber700, TextShade.BLACK);
            _isFileParsed = false;
            CheckForExistingBotToken();
        }

        private void SetWindowSizeAndLocation()
        {
            Location = Properties.Settings.Default.FormLocation;
            Height = Properties.Settings.Default.FormHeight;
            Width = Properties.Settings.Default.FormWidth;
            FormClosing += SaveSettingsEventHandler;
            StartPosition = FormStartPosition.Manual;
        }

        private void CheckForExistingBotToken()
        {
            DisableBothBotConnectionButtons();
            _discordToken = Properties.Settings.Default.SlackordBotToken.Trim();
            if (Properties.Settings.Default.FirstRun)
            {
                richTextBox1.Text += "Welcome to Slackord 2!" + "\n";
            }
            else if (string.IsNullOrEmpty(_discordToken) || string.IsNullOrEmpty(Properties.Settings.Default.SlackordBotToken))
            {
                richTextBox1.Text += "Slackord 2 tried to automatically load your last bot token but wasn't successful." + "\n"
                    + "The token is not long enough or the token value is empty. Please enter a new token." + "\n";
            }
            else
            {
                richTextBox1.Text += "Slackord 2 found a previously entered bot token and automatically applied it! Bot connection is now enabled." + "\n";
                EnableBotConnectionMenuItem();
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }

        [STAThread]
        private async void ParseJsonFiles()
        {
            richTextBox1.Text += "Begin parsing JSON data..." + "\n";
            richTextBox1.Text += "-----------------------------------------" + "\n";

            foreach (var file in ListOfFilesToParse)
            {
                try
                {
                    var json = File.ReadAllText(file);
                    parsed = JArray.Parse(json);
                    string debugResponse;
                    foreach (JObject pair in parsed.Cast<JObject>())
                    {
                        if (pair.ContainsKey("reply_count") && pair.ContainsKey("thread_ts"))
                        {
                            isThreadStart.Add(true);
                            isThreadMessages.Add(false);
                        }
                        else if (pair.ContainsKey("thread_ts"))
                        {
                            isThreadStart.Add(false);
                            isThreadMessages.Add(true);
                        }
                        else
                        {
                            isThreadStart.Add(false);
                            isThreadMessages.Add(false);
                        }

                        if (pair.ContainsKey("files"))
                        {
                            try
                            {
                                debugResponse = pair["files"][0]["thumb_1024"].ToString() + "\n";
                                Responses.Add(debugResponse);
                            }
                            catch (NullReferenceException)
                            {
                                try
                                {
                                    debugResponse = pair["files"][0]["url_private"].ToString() + "\n";
                                    Responses.Add(debugResponse);
                                }
                                catch (NullReferenceException)
                                {
                                    debugResponse = "Skipped a tombstoned file attachement.";
                                    Responses.Add(debugResponse);
                                }
                            }
                            richTextBox1.Text += debugResponse + "\n";
                        }
                        if (pair.ContainsKey("bot_profile"))
                        {
                            try
                            {
                                debugResponse = pair["bot_profile"]["name"].ToString() + ": " + pair["text"] + "\n";
                                Responses.Add(debugResponse);
                            }
                            catch (NullReferenceException)
                            {
                                try
                                {
                                    debugResponse = pair["bot_id"].ToString() + ": " + pair["text"] + "\n";
                                    Responses.Add(debugResponse);
                                }
                                catch (NullReferenceException)
                                {
                                    debugResponse = "A bot message was ignored. Please submit an issue on Github for this.";
                                }
                            }
                            richTextBox1.Text += debugResponse + "\n";
                        }
                        if (pair.ContainsKey("user_profile") && pair.ContainsKey("text"))
                        {
                            var rawTimeDate = pair["ts"];
                            var oldDateTime = (double)rawTimeDate;
                            var convertDateTime = ConvertFromUnixTimestampToHumanReadableTime(oldDateTime).ToString("g");
                            var newDateTime = convertDateTime.ToString();
                            var slackUserName = pair["user_profile"]["display_name"].ToString();
                            var slackRealName = pair["user_profile"]["real_name"];

                            string slackMessage;
                            if (pair["text"].Contains('|'))
                            {
                                string preSplit = pair["text"].ToString();
                                string[] split = preSplit.Split(new char[] { '|' });
                                string originalText = split[0];
                                string splitText = split[1];

                                if (originalText.Contains(splitText))
                                {
                                    slackMessage = splitText + "\n";
                                }
                                else
                                {
                                    slackMessage = originalText + "\n";
                                }
                            }
                            else
                            {
                                slackMessage = pair["text"].ToString();
                            }
                            if (string.IsNullOrEmpty(slackUserName))
                            {
                                debugResponse = newDateTime + " - " + slackRealName + ": " + slackMessage;
                                Responses.Add(debugResponse);
                            }
                            else
                            {
                                debugResponse = newDateTime + " - " + slackUserName + ": " + slackMessage;
                                if (debugResponse.Length >= 2000)
                                {
                                    richTextBox1.Text += "The following parse is over 2000 characters. Discord does not allow messages over 2000 characters. This message " +
                                        "will be split into multiple posts. The message that will be split is:\n" + debugResponse;
                                }
                                else
                                {
                                    debugResponse = newDateTime + " - " + slackUserName + ": " + slackMessage + " " + "\n";
                                    Responses.Add(debugResponse);
                                }
                            }
                            richTextBox1.Text += debugResponse + "\n";
                        }
                    }
                    richTextBox1.Text += "\n";
                    richTextBox1.Text += "-----------------------------------------" + "\n";
                    richTextBox1.Text += "Parsing of " + file + " completed successfully!" + "\n";
                    richTextBox1.Text += "-----------------------------------------" + "\n";
                    richTextBox1.Text += "\n";
                    _isFileParsed = true;
                    richTextBox1.ForeColor = System.Drawing.Color.DarkGreen;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
                if (_discordClient != null)
                {
                    await _discordClient.SetActivityAsync(new Game("awaiting command to import messages...", ActivityType.Watching));
                }

            }
        }

        public async Task PostMessagesToDiscord(SocketChannel channel, ulong guildID)
        {
            try
            {
                await _discordClient.SetActivityAsync(new Game("posting messages...", ActivityType.Watching));
                int messageCount = 0;

                // TODO: Fix Application did not respond in time error.
                // await DeferAsync();
                if (_isFileParsed)
                {
                    richTextBox1.Invoke(new Action(() =>
                    {
                        richTextBox1.Text += "\n";
                        richTextBox1.Text += "Beginning transfer of Slack messages to Discord..." + "\n" +
                        "-----------------------------------------" + "\n";
                    }));

                    SocketThreadChannel threadID = null;
                    foreach (string message in Responses)
                    {
                        bool sendAsThread = false;
                        bool sendAsThreadReply = false;
                        bool sendAsNormalMessage = false;

                        string messageToSend = message;
                        bool wasSplit = false;

                        if (isThreadStart[messageCount] == true)
                        {
                            sendAsThread = true;
                        }
                        else if (isThreadStart[messageCount] == false && isThreadMessages[messageCount] == true)
                        {
                            sendAsThreadReply = true;
                        }
                        else
                        {
                            sendAsNormalMessage = true;
                        }
                        messageCount += 1;

                        if (messageToSend.Contains('|'))
                        {
                            string preSplit = message;
                            string[] split = preSplit.Split(new char[] { '|' });
                            string originalText = split[0];
                            string splitText = split[1];

                            if (originalText.Contains(splitText))
                            {
                                messageToSend = splitText + "\n";
                            }
                            else
                            {
                                messageToSend = originalText + "\n";
                            }
                        }

                        if (message.Length >= 2000)
                        {
                            var responses = messageToSend.SplitInParts(1800);

                            richTextBox1.Invoke(new Action(() =>
                            {
                                richTextBox1.Text += "SPLITTING AND POSTING: " + messageToSend;
                            }));
                            foreach (var response in responses)
                            {
                                messageToSend = response + " " + "\n";
                                if (sendAsThread)
                                {
                                    await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync(messageToSend).ConfigureAwait(false);
                                    var messages = await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).GetMessagesAsync(1).FlattenAsync();
                                    threadID = await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).CreateThreadAsync("Slackord Thread", ThreadType.PublicThread, ThreadArchiveDuration.OneDay, messages.First());
                                }
                                else if (sendAsThreadReply)
                                {
                                    await threadID.SendMessageAsync(messageToSend).ConfigureAwait(false);
                                }
                                else if (sendAsNormalMessage)
                                {
                                    await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync(messageToSend).ConfigureAwait(false);
                                }
                            }
                            wasSplit = true;
                        }
                        else
                        {
                            richTextBox1.Invoke(new Action(() =>
                            {
                                richTextBox1.Text += "POSTING: " + message;
                            }));

                            if (!wasSplit)
                            {
                                if (sendAsThread)
                                {
                                    await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync(messageToSend).ConfigureAwait(false);
                                    var messages = await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).GetMessagesAsync(1).FlattenAsync();
                                    threadID = await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).CreateThreadAsync("Slackord Thread", ThreadType.PublicThread, ThreadArchiveDuration.OneDay, messages.First());
                                }
                                else if (sendAsThreadReply)
                                {
                                    await threadID.SendMessageAsync(messageToSend).ConfigureAwait(false);
                                }
                                else if (sendAsNormalMessage)
                                {
                                    await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync(messageToSend).ConfigureAwait(false);
                                }
                            }
                        }
                    }
                    richTextBox1.Invoke(new Action(() =>
                    {
                        richTextBox1.Text += "-----------------------------------------" + "\n" +
                        "All messages sent to Discord successfully!" + "\n";
                    }));
                    // TODO: Fix Application did not respond in time error.
                    // await FollowupAsync("All messages sent to Discord successfully!", ephemeral: true);
                    await _discordClient.SetActivityAsync(new Game("awaiting parsing of messages.", ActivityType.Watching));
                }
                else if (!_isFileParsed)
                {
                    await _discordClient.GetGuild(guildID).GetTextChannel(channel.Id).SendMessageAsync("Sorry, there's nothing to post because no JSON file was parsed prior to sending this command.").ConfigureAwait(false);
                    richTextBox1.Invoke(new Action(() =>
                    {
                        richTextBox1.Text += "Received a command to post messages to Discord, but no JSON file was parsed prior to receiving the command." + "\n";
                    }));
                }
                await _discordClient.SetActivityAsync(new Game("for the Slackord command...", ActivityType.Listening));
                Responses.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void CheckForUpdatesToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            if (_octoClient == null)
            {
                _octoClient = new octo.GitHubClient(new octo.ProductHeaderValue("Slackord2"));
                CheckForUpdates();
            }
            else if (_octoClient != null)
            {
                CheckForUpdates();
            }
            else
            {
                MessageBox.Show("Couldn't connect to get updates. Github must be down, try checking again later?",
                    "Couldn't Connect!", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Slackord " + CurrentVersion + ".\n" +
                "Created by Thomas Loupe." + "\n" +
                "Github: https://github.com/thomasloupe" + "\n" +
                "Twitter: https://twitter.com/acid_rain" + "\n" +
                "Website: https://thomasloupe.com", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        public static DateTime ConvertFromUnixTimestampToHumanReadableTime(double timestamp)
        {
            var date = new DateTime(1970, 1, 1, 0, 0, 0, 0);
            var returnDate = date.AddSeconds(timestamp);
            return returnDate;
        }

        private void ToolStripButton1_Click(object sender, EventArgs e)
        {
            if (richTextBox1.SelectionLength == 0)
            {
                richTextBox1.SelectAll();
                richTextBox1.Copy();
            }
        }

        private void ToolStripButton2_Click(object sender, EventArgs e)
        {
            richTextBox1.Text = "";
        }

        public async Task MainAsync()
        {
            richTextBox1.Text += "Starting Slackord bot..." + "\n";
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
            EnableBotDisconnectionMenuItem();
            DisableTokenChangeWhileConnected();
            await _discordClient.LoginAsync(TokenType.Bot, _discordToken.Trim());
            await _discordClient.StartAsync();
            await _discordClient.SetActivityAsync(new Game("awaiting parsing of messages.", ActivityType.Watching));
            _discordClient.Ready += ClientReady;
            _discordClient.SlashCommandExecuted += SlashCommandHandler;
            await Task.Delay(-1);
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            if (command.Data.Name.Equals("slackord"))
            {
                var guildID = _discordClient.Guilds.FirstOrDefault().Id;
                var channel = _discordClient.GetChannel((ulong)command.ChannelId);
                await PostMessagesToDiscord(channel, guildID);
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

        private Task DiscordClient_Log(LogMessage arg)
        {
            richTextBox1.Invoke(new Action(() => { richTextBox1.Text += arg.ToString() + "\n"; }));
            return Task.CompletedTask;
        }

        private void EnterBotTokenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ShowEnterTokenDialog();
        }

        private void ConnectBotToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _ = MainAsync();
        }

        private void ShowEnterTokenDialog()
        {
            _discordToken = Prompt.ShowDialog("Enter bot token.", "Enter Bot Token");
            Properties.Settings.Default.SlackordBotToken = _discordToken.Trim();
            if (_discordToken.Length > 10 && string.IsNullOrEmpty(_discordToken).Equals(false))
            {
                EnableBotConnectionMenuItem();
            }
            else
            {
                EnableBotDisconnectionMenuItem();
            }
        }

        private async void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            await _discordClient.StopAsync();
            Application.Exit();
        }

        private void SaveSettingsEventHandler(object sender, FormClosingEventArgs e)
        {
            Properties.Settings.Default.FormHeight = Height;
            Properties.Settings.Default.FormWidth = Width;
            Properties.Settings.Default.FormLocation = Location;
            if (Properties.Settings.Default.FirstRun)
            {
                Properties.Settings.Default.FirstRun = false;
            }
            Properties.Settings.Default.SlackordBotToken = _discordToken.Trim();
            Properties.Settings.Default.Save();
        }

        private void DisconnectBotToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _discordClient.StopAsync();
            EnableBotConnectionMenuItem();
            EnableTokenChangeWhileConnected();
        }

        private async void CheckForUpdates()
        {
            var releases = await _octoClient.Repository.Release.GetAll("thomasloupe", "Slackord-2.0").ConfigureAwait(false);
            var latest = releases[0];
            if (CurrentVersion == latest.TagName)
            {
                MessageBox.Show("You have the latest version, " + CurrentVersion + "!", CurrentVersion, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else if (CurrentVersion != latest.TagName)
            {
                var result = MessageBox.Show("A new version of Slackord is available!\n"
                    + "Current version: " + CurrentVersion + "\n"
                    + "Latest version: " + latest.TagName + "\n"
                    + "Would you like to visit the download page?", "Update Available!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
                if (result == DialogResult.Yes)
                {
                    Process.Start("https://github.com/thomasloupe/Slackord-2.0/releases/tag/" +
                                                     latest.TagName);
                }
            }
        }

        private void EnableBotConnectionMenuItem()
        {
            ConnectBotToolStripMenuItem.Enabled = true;
            DisconnectBotToolStripMenuItem.Enabled = false;
        }

        private void EnableBotDisconnectionMenuItem()
        {
            ConnectBotToolStripMenuItem.Enabled = false;
            DisconnectBotToolStripMenuItem.Enabled = true;
        }
        private void DisableBothBotConnectionButtons()
        {
            ConnectBotToolStripMenuItem.Enabled = false;
            DisconnectBotToolStripMenuItem.Enabled = false;
        }

        private void DisableTokenChangeWhileConnected()
        {
            EnterBotTokenToolStripMenuItem.Enabled = false;
        }
        private void EnableTokenChangeWhileConnected()
        {
            EnterBotTokenToolStripMenuItem.Enabled = true;
        }

        private void DonateToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            var result = MessageBox.Show("Slackord will always be free!\n"
                + "If you'd like to buy me a beer anyway, I won't tell you no!\n"
                + "Would you like to open the donation page now?", "Slackord is free, but beer is not!", MessageBoxButtons.YesNo, MessageBoxIcon.Exclamation);
            if (result == DialogResult.Yes)
            {
                Process.Start("https://paypal.me/thomasloupe");
            }
        }

        private void RichTextBox1_TextChanged(object sender, EventArgs e)
        {
            richTextBox1.SelectionStart = richTextBox1.Text.Length;
            richTextBox1.ScrollToCaret();
        }
        private void Link_Clicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }

        private void OpenToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListOfFilesToParse.Clear();
            _ofd = new OpenFileDialog { Filter = "JSON File|*.json", Title = "Import a JSON file for parsing" };
            if (_ofd.ShowDialog() == DialogResult.OK)
                ListOfFilesToParse.Add(_ofd.FileName);
            ParseJsonFiles();
        }

        private void ImportJSONFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ListOfFilesToParse.Clear();
            using var fbd = new FolderBrowserDialog();
            DialogResult result = fbd.ShowDialog();

            if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
            {
                var files = Directory.EnumerateFiles(fbd.SelectedPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(s => s.EndsWith(".JSON") || s.EndsWith(".json"));
                
                foreach (var file in files)
                {
                    ListOfFilesToParse.Add(file);
                }
                MessageBox.Show("Files found: " + files.Count(), "Message");
                ParseJsonFiles();
            }
        }
    }

    static class StringExtensions
    {

        public static IEnumerable<String> SplitInParts(this String s, Int32 partLength)
        {
            if (s == null)
                throw new ArgumentNullException(nameof(s));
            if (partLength <= 0)
                throw new ArgumentException("Invalid char length specified.", nameof(partLength));

            for (var i = 0; i < s.Length; i += partLength)
                yield return s.Substring(i, Math.Min(partLength, s.Length - i));
        }

    }

    public static class Prompt
    {
        public static string ShowDialog(string text, string caption)
        {
            var prompt = new Form() { Width = 500, Height = 150, FormBorderStyle = FormBorderStyle.FixedDialog, Text = caption, StartPosition = FormStartPosition.CenterScreen };
            var textLabel = new Label() { Left = 50, Top = 20, Text = text };
            var textBox = new TextBox() { Left = 50, Top = 50, Width = 400 };
            var confirmation = new Button() { Text = "OK", Left = 225, Width = 50, Top = 75, DialogResult = DialogResult.OK };
            confirmation.Click += (sender, e) => { prompt.Close(); };
            prompt.Controls.Add(textBox);
            prompt.Controls.Add(confirmation);
            prompt.Controls.Add(textLabel);
            prompt.AcceptButton = confirmation;
            return prompt.ShowDialog() == DialogResult.OK ? textBox.Text : "";
        }
    }
}
