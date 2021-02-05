using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Assets.Scripts.Unity;
using Assets.Scripts.Unity.UI_New.InGame;
using Assets.Scripts.Unity.UI_New.Main;
using Assets.Scripts.Unity.UI_New.Popups;
using BloonsTD6_Mod_Helper.Extensions;
using Harmony;
using MelonLoader;
using MelonLoader.TinyJSON;
using Timer = System.Timers.Timer;

// Disabled because i prefer it this way and some
// ReSharper disable ConvertToLocalFunction
// ReSharper disable UnusedMember.Global

namespace SCPI
{
    // Suppressed as will be used in the future.

    // Not used directly, only indirectly by tinyJSON.
    [SuppressMessage("ReSharper", "NotAccessedField.Global")]
    // Must be named this way to be parsed correctly.
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "CollectionNeverQueried.Global")]
    internal struct PubSubMessageData
    {
        // See comments above for reasoning.
#pragma warning disable 649
        public string auth_token;
        public List<string> topics;
    }

    // Not used directly, only indirectly by tinyJSON.
    [SuppressMessage("ReSharper", "NotAccessedField.Global")]
    // Must be named this way to be parsed correctly.
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    internal struct PubSubMessage
    {
        public string type;
        public PubSubMessageData data;
    }
#pragma warning restore 649
    public class Main : MelonMod
    {
        private static readonly HttpClient Client = new HttpClient();
        private static readonly ClientWebSocket Wsc = new ClientWebSocket();
        internal static readonly Queue<MyPopup> PopupsToDisplay = new Queue<MyPopup>();
        internal static readonly Queue<Action> RewardActionsToRunInInGameThread = new Queue<Action>();
        private static string _twitchToken;
        private static string _twitchID;
        internal static bool TwitchAuthed;
        private static bool _twitchSynced;
        internal const string TwitchauthPath = @"Mods/Twitch/auth.txt";
        private const string RewardsPath = @"Mods/Twitch/rewards.txt";
        internal static string TwitchAuthFailReason = "something went wrong. You should not see this text, ever.";
        private const string ClientID = "54ynosen4vbdar2wo1db4ihthvv5vx";

        public override async void OnApplicationStart()
        {
            base.OnApplicationStart();
            MelonLogger.Log("Starting up Twitch Integration...");
            MelonLogger.Log("Checking for existing authentication...");
            Directory.CreateDirectory(Path.GetDirectoryName(TwitchauthPath) ?? throw new InvalidOperationException());
            if (!File.Exists(TwitchauthPath))
            {
                TwitchAuthFailReason = " you started this mod for the first time.";
            }
            else
            {
                var content = File.Exists(TwitchauthPath) ? File.ReadAllText(TwitchauthPath) : "";
                content = content.Trim();
                //MelonLogger.Log("Content was: \"" + content + "\"");
                if (!content.Equals(""))
                {
                    await ValidateToken(content);
                }
                else
                {
                    TwitchAuthFailReason = "the authentication file was empty. Did you do that?";
                }
            }
        }

        internal static async Task<Variant> ValidateToken(string token)
        {
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("OAuth", token);
            Client.DefaultRequestHeaders.Add("Client-ID", ClientID);
            var response = await Client.GetAsync("https://id.twitch.tv/oauth2/validate");
            var responseContent = await response.Content.ReadAsStringAsync();
            //MelonLogger.Log("Validation response: " + responseContent);
            var responseJson = JSON.Load(responseContent);
            if (response.IsSuccessStatusCode)
            {
                MelonLogger.Log("That's a success! Setting variables.");
                _twitchID = responseJson["user_id"];
                _twitchToken = token;
                TwitchAuthed = true;
                int expiresIn = responseJson["expires_in"];
                if (expiresIn < 24 * 60 * 60) // token expires within 24 hours
                {
                    PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesno,
                        "Your saved Twitch Authentication is due to expire within the next 24 hours. Do you want to reauthenticate now or later?",
                        "Twitch Integration", "Now", "Later", StreamingSetupMainMenuHook.ShowTwitchAuthPopup));
                }
            }
            else
            {
                MelonLogger.LogWarning("Preexisting token has expired!");
                TwitchAuthFailReason = "a previous authentication has expired.";
            }

            return responseJson;
        }

        private static ArraySegment<byte> GetArraySegment(string input)
        {
            return new ArraySegment<byte>(input.ToCharArray().Select(c => (byte) c).ToArray());
        }

        private static Task SendToPubSub(string input)
        {
            // MelonLogger.Log(">> " + input);
            return Wsc.SendAsync(GetArraySegment(input),
                WebSocketMessageType.Text, true,
                CancellationToken.None);
        }

        private static bool _syncStarted;

        private static async void SyncFinished()
        {
            _twitchSynced = true;
            Wsc.ConnectAsync(new Uri("wss://pubsub-edge.twitch.tv"), CancellationToken.None).Wait();
            MelonLogger.Log("Connected to PubSub");
            await Task.Factory.StartNew(async () =>
            {
                while (true)
                {
                    var arraySegment = new ArraySegment<byte>(new byte[4096 * 20]);
                    //MelonLogger.Log("Waiting for messages...");
                    await Wsc.ReceiveAsync(arraySegment, CancellationToken.None);
                    //MelonLogger.Log("Got message");
                    var msgBytes = arraySegment.Skip(arraySegment.Offset).Take(arraySegment.Count).ToArray();
                    var rcvMsg = Encoding.UTF8.GetString(msgBytes);
                    //MelonLogger.Log("rcvMSg: " + rcvMsg);
                    foreach (var line in rcvMsg.Split('\n'))
                    {
                        if ("".Equals(line)) continue;
                        //MelonLogger.Log("<< " + line);
                        try
                        {
                            var msgJson = JSON.Load(line);
                            //MelonLogger.Log("parsed json: " + JSON.Dump(msgJson));
                            switch (msgJson["type"].ToString(CultureInfo.CurrentCulture))
                            {
                                case "MESSAGE":
                                {
                                    try
                                    {
                                        //MelonLogger.Log("In MESSAGE switch");
                                        var data = msgJson["data"];
                                        //MelonLogger.Log("Data: " + JSON.Dump(data));
                                        string message = data["message"];
                                        //MelonLogger.Log("Message: " + message);
                                        var rewardData = JSON.Load(message);
                                        //MelonLogger.Log("message parsed");
                                        //MelonLogger.Log("Parsed data: " + JSON.Dump(rewardData));
                                        // ReSharper disable once UnusedVariable
                                        string id = rewardData["data"]["redemption"]["reward"]["id"];
                                        //MelonLogger.Log("Got the reward id: " + id);
                                        var redemption =
                                            new ChannelPointRedemption(rewardData["data"]["redemption"]);
                                        redemption.Reward.Trigger();
                                    }
                                    catch (Exception e)
                                    {
                                        MelonLogger.LogError(e.ToString());
                                    }

                                    break;
                                }
                                case "RECONNECT":
                                {
                                    PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesonly,
                                        title:
                                        "Received RECONNECT from Twitch - please restart the game to continue using Rewards"));
                                    break;
                                }
                            }
                        }
                        catch (NullReferenceException)
                        {
                        }
                        catch (Exception e)
                        {
                            MelonLogger.LogWarning("Error during Websocket parsing: " + e);
                        }
                    }
                }
            });
            var t = new Timer(28000);
            t.Elapsed += async (sender, args) => { await SendToPubSub("{\"type\":\"PING\"}"); };
            t.AutoReset = true;
            t.Enabled = true;
            var toSend = new PubSubMessage
            {
                type = "LISTEN",
                data = new PubSubMessageData {auth_token = _twitchToken, topics = new List<string>()}
            };
            toSend.data.topics.Add("channel-points-channel-v1." + _twitchID);
            await SendToPubSub(JSON.Dump(toSend));
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            if (Game.instance is null)
                return;
            if (!_syncStarted && TwitchAuthed && !_twitchSynced)
            {
                //MelonLogger.Log("Sync start");
                _syncStarted = true;
                MelonLogger.Log("Syncing Channel Point Rewards with Twitch...");
                if (!File.Exists(RewardsPath))
                {
                    //MelonLogger.Log("Creating file!");
                    var stream = File.CreateText(RewardsPath);
                    stream.WriteAsync("{}").Wait();
                    stream.FlushAsync().Wait();
                    stream.Close();
                }

                try
                {
                    var content = JSON.Load(File.ReadAllText(RewardsPath));
                    var foundOnTwitch = new Dictionary<RewardType, bool>();
                    var availableRewards = Enum.GetValues(typeof(RewardType));
                    foreach (RewardType type in availableRewards)
                    {
                        foundOnTwitch[type] = false;
                    }

                    foreach (var value in (RewardType[]) Enum.GetValues(typeof(RewardType)))
                    {
                        try
                        {
                            string id = content[value.ToString()]["id"];
                            ChannelPointReward.SetRewardTypeID(value, id);
                        }
                        catch (KeyNotFoundException)
                        {
                        }
                    }

                    Client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", _twitchToken);
                    Task<HttpResponseMessage> t = Client.GetAsync(
                        "https://api.twitch.tv/helix/channel_points/custom_rewards?only_manageable_rewards=true&broadcaster_id=" +
                        _twitchID);
                    t.Wait();
                    HttpResponseMessage response = t.Result;
                    Task<string> t2 = response.Content.ReadAsStringAsync();
                    t2.Wait();
                    var responseContent = t2.Result;
                    MelonLogger.Log("Rewards response: " + responseContent);
                    var responseJson = JSON.Load(responseContent);
                    var data = responseJson["data"];
                    MelonLogger.Log("data acquired. Moving along.");
                    foreach (var reward in (ProxyArray) data)
                    {
                        ChannelPointReward rewardObj = new ChannelPointReward(reward);
                        try
                        {
                            var type = rewardObj.GetRewardType();
                            ChannelPointReward.SetRewardTypeID(type, rewardObj.id);
                            foundOnTwitch[type] = true;
                        }
                        catch (Exception)
                        {
                            MelonLogger.Log("Could not map reward " + rewardObj.id + " to a local type. Ignoring.");
                        }
                    }

                    async void CreateRewards()
                    {
                        bool hadIssues = false;
                        foreach (KeyValuePair<RewardType, bool> keyValuePair in foundOnTwitch)
                        {
                            if (!keyValuePair.Value)
                            {
                                //If reward not found yet, create it!
                                try
                                {
                                    var obj = JSON.Load("{\"title\":\"" +
                                                        ChannelPointReward.GetDescriptionFromType(keyValuePair.Key) +
                                                        "\"," + "\"cost\":1000," +
                                                        "\"is_enabled\":false," +
                                                        "\"background_color\":\"" +
                                                        ChannelPointReward.GetColorFromType(keyValuePair.Key) + "\"" +
                                                        "\"should_redemptions_skip_request_queue\":true}");
                                    var postResponse = await Client.PostAsync(
                                        "https://api.twitch.tv/helix/channel_points/custom_rewards?broadcaster_id=" +
                                        _twitchID,
                                        new StringContent(JSON.Dump(obj), Encoding.UTF8, "application/json"));
                                    var postResponseContent = await postResponse.Content.ReadAsStringAsync();
                                    var postResponseContentJson = JSON.Load(postResponseContent);
                                    content[keyValuePair.Key.ToString()] =
                                        postResponseContentJson["data"][0];
                                    //MelonLogger.Log("Reward creation result: " + postResponseContent);

                                    ChannelPointReward.SetRewardTypeID(keyValuePair.Key,
                                        postResponseContentJson["data"][0]["id"]);
                                }
                                catch (Exception e)
                                {
                                    MelonLogger.LogError(e.ToString());
                                    hadIssues = true;
                                }
                            }
                        }

                        var stream = File.CreateText(RewardsPath);
                        await stream.WriteAsync(JSON.Dump(content)); //a
                        await stream.FlushAsync();
                        stream.Close();

                        if (hadIssues)
                        {
                            PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesno,
                                "Something went wrong trying to create the rewards.\n" +
                                "You must have access to Channel Points for this to work! Also note that you may need to delete other rewards with the same name!\n" +
                                "Try again?",
                                "Twitch Integration Error", "Yes", "No", CreateRewards));
                        }
                        else
                        {
                            PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesonly,
                                title: "Rewards created!\nRemember to enable them so they can be used!",
                                okCallback: SyncFinished));
                        }
                    }

                    var foundAll = foundOnTwitch.All(keyValuePair => keyValuePair.Value);

                    if (foundAll)
                    {
                        SyncFinished();
                    }
                    else
                    {
                        PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesno,
                            "One or more Rewards were not found on Twitch.\n\nDo you want to create a new set of Rewards now?",
                            "Twitch Integration", "yes", "no", CreateRewards));
                    }
                }
                catch (Exception e)
                {
                    MelonLogger.LogError("Error loading Rewards: " + e);
                    PopupsToDisplay.Enqueue(new MyPopup(PopupType.Yesno,
                        "Error loading Rewards.\n\nTry again?",
                        "Twitch Integration Error", "Yes", "no", () => { _syncStarted = false; }));
                    return;
                }
            }

            if (PopupsToDisplay.Count >= 1 && StreamingSetupMainMenuHook.Seen)
            {
                if (2 < Game.instance.GetPopupScreen().activePopupHandles._size)
                {
                    //goto afterif; // Don't show popup if too many are already active.
                }

                //MelonLogger.Log("Showing popup...");
                var currentPopup = PopupsToDisplay.Dequeue();
                switch (currentPopup.Type)
                {
                    case PopupType.Yesno:
                        Game.instance.GetPopupScreen().ShowPopup(PopupScreen.Placement.menuCenter,
                            currentPopup.Title, currentPopup.Body, currentPopup.OkCallback, currentPopup.OkText,
                            currentPopup.CancelCallback, currentPopup.CancelText, Popup.TransitionAnim.Scale);
                        break;
                    case PopupType.Yesonly:
                        Game.instance.GetPopupScreen().ShowWelcomePopup(PopupScreen.Placement.menuCenter,
                            currentPopup.Title, currentPopup.OkCallback, currentPopup.OkText,
                            currentPopup.CancelCallback, "THIS IS NEVER USED SMH", Popup.TransitionAnim.Scale);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }

            if (InGame.instance is null)
                return;

            if (RewardActionsToRunInInGameThread.Count >= 1)
            {
                var action = RewardActionsToRunInInGameThread.Dequeue();
                action.Invoke();
            }
        }
    }

    [HarmonyPatch(typeof(MainMenu), "OnEnable")]
    public static class StreamingSetupMainMenuHook
    {
        internal static bool Seen;

        [HarmonyPostfix]
        public static void Postfix()
        {
            if (Seen) return;
            Seen = true;
            if (!Main.TwitchAuthed)
                Game.instance.GetPopupScreen()
                    .ShowPopup(PopupScreen.Placement.menuCenter, "Twitch Integration",
                        "Hi there! Your authentication for Twitch is currently not valid because " +
                        Main.TwitchAuthFailReason + "\nDo you want to log in now or later?",
                        new Action(
                            ShowTwitchAuthPopup), "now", new Action(() => { }), "Later", Popup.TransitionAnim.Scale);
        }

        private const string Httpredirect = "<!DOCTYPE html>\n" + "<html lang=\"en\">\n" + "    <head>\n" +
                                            "        <meta charset=\"UTF-8\">\n" +
                                            "        <title>OAuth local redirection...</title>\n" + "    </head>\n" +
                                            "    <body>\n" + "        Hi, you shouldn't see this for long.\n" +
                                            "        <noscript>\n" +
                                            "            <h1>Please enable javascript! I need to redirect the oauth token to the application please :(</h1>\n" +
                                            "        </noscript>\n" + "        <script lang=\"javascript\">\n" +
                                            "                let req = new XMLHttpRequest();\n" +
                                            "                req.open('POST', '/', false);\n" +
                                            "                req.setRequestHeader('Content-Type', 'text');\n" +
                                            "                req.send(document.location.hash);\n" +
                                            "                console.log(\"response headers: \" + req.getAllResponseHeaders());\n" +
                                            "                console.log(\"I guess i can close now?\");\n" +
                                            "                window.close();\n" + "        </script>\n" +
                                            "    </body>\n" + "</html>\n";

        private static HttpListener _listener;

        internal static void ShowTwitchAuthPopup()
        {
            MelonLogger.Log("opening Twitch authentication page");
            Process.Start(
                "https://id.twitch.tv/oauth2/authorize?client_id=54ynosen4vbdar2wo1db4ihthvv5vx" +
                "&redirect_uri=http%3A%2F%2Flocalhost%3A17863" +
                "&response_type=token" +
                "&scope=channel:read:redemptions channel:manage:redemptions");
            Game.instance.GetPopupScreen().ShowPopup(PopupScreen.Placement.menuCenter, "Authenticating",
                "Please check the browser that opened or click below to retry or cancel",
                new Action(ShowTwitchAuthPopup), "retry",
                new Action(() => { MelonLogger.Log("aborted Twitch auth"); }), "Cancel", Popup.TransitionAnim.Scale);
            Task.Factory.StartNew(async () =>
            {
                _listener?.Abort();
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://localhost:17863/");
                _listener.Start();
                while (_listener.IsListening)
                {
                    var context = await _listener.GetContextAsync();
                    var request = context.Request;
                    var response = context.Response;

                    MelonLogger.Log("Request to: " + request.Url);
                    if (request.HttpMethod == "POST")
                    {
                        string text;
                        using (var reader = new StreamReader(request.InputStream,
                            request.ContentEncoding))
                        {
                            text = await reader.ReadToEndAsync();
                        }

                        //MelonLogger.Log("Got post data: " + text);
                        var buffer = Encoding.UTF8.GetBytes("Thank you. Close this now.");
                        response.ContentLength64 = buffer.Length;
                        var output = response.OutputStream;
                        await output.WriteAsync(buffer, 0, buffer.Length);
                        output.Close();
                        _listener.Close();
                        var decoded = HttpUtility.UrlDecode(text);
                        var parts = decoded.Split('&');
                        foreach (var part in parts)
                        {
                            var keyvalueparts = part.Split('=');
                            var key = keyvalueparts[0].Replace("#", "");
                            var value = keyvalueparts[1];
                            if (!key.Equals("access_token")) continue;
                            Directory.CreateDirectory(Path.GetDirectoryName(Main.TwitchauthPath) ??
                                                      throw new InvalidOperationException());
                            File.WriteAllLines(Main.TwitchauthPath, new[] {value});
                            Action callback =
                                () => { Game.instance.GetPopupScreen().GetFirstActivePopup().HidePopup(); };
                            //MelonLogger.Log("About to await validate token");
                            var popup = new MyPopup(PopupType.Yesonly, title: "Successfully Authenticated!",
                                okCallback: callback, cancelCallback: callback);
                            Main.PopupsToDisplay.Enqueue(popup);
                            await Main.ValidateToken(value);
                        }
                    }
                    else
                    {
                        MelonLogger.Log("Got non-Post request, sending redirect response...");
                        var buffer = Encoding.UTF8.GetBytes(Httpredirect);
                        response.ContentLength64 = buffer.Length;
                        var output = response.OutputStream;
                        await output.WriteAsync(buffer, 0, buffer.Length);
                        output.Close();
                    }
                }
            });
        }
    }
}