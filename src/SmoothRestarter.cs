// #define UNITY_ASSERTIONS // Uncomment this if you have any issues with the plugin, assertion log can help locate problematic code
// #define SIMULATE_OXIDE_PATCH

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using ConVar;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;

using UnityEngine;
using UnityEngine.UI;

using Debug = UnityEngine.Debug;
using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("SmoothRestarter", "2CHEVSKII", "3.1.1")]
    [Description("A reliable way to shutdown your server when you need it")]
    public class SmoothRestarter : CovalencePlugin
    {
        #region Permission names

        const string PERMISSION_STATUS  = "smoothrestarter.status",
                     PERMISSION_RESTART = "smoothrestarter.restart",
                     PERMISSION_CANCEL  = "smoothrestarter.cancel";

        #endregion

        #region LangAPI keys

        const string M_CHAT_PREFIX                = "Chat prefix",
                     M_NO_PERMISSION              = "No permission",
                     M_KICK_REASON                = "Kick reason",
                     M_HELP                       = "Help message",
                     M_HELP_HELP                  = "Help message: Help",
                     M_HELP_STATUS                = "Help message: Status",
                     M_HELP_RESTART               = "Help message: Restart",
                     M_HELP_CANCEL                = "Help message: Cancel",
                     M_RESTARTING_ALREADY         = "Restarting already",
                     M_NOT_RESTARTING             = "Not restarting",
                     M_CANCEL_SUCCESS             = "Cancelled successfully",
                     M_RESTART_REASON_TIMED       = "Restart reason: Timed",
                     M_RESTART_REASON_OXIDE       = "Restart reason: Oxide update",
                     M_RESTART_REASON_COMMAND     = "Restart reason: Command",
                     M_RESTART_REASON_API         = "Restart reason: API call",
                     M_ANNOUNCE_RESTART_INIT      = "Announcement: Restart initiated",
                     M_ANNOUNCE_COUNTDOWN_TICK    = "Announcement: Countdown tick",
                     M_ANNOUNCE_RESTART_CANCELLED = "Announcement: Restart cancelled",
                     M_RESTART_SUCCESS            = "Restart initiated",
                     M_STATUS_RESTARTING          = "Status: Restarting",
                     M_STATUS_RESTARTING_NATIVE   = "Status: Restarting (global.restart)",
                     M_STATUS_PLANNED             = "Status: Restart planned",
                     M_STATUS_NO_PLANNED          = "Status: No planned restarts",
                     M_UI_TITLE                   = "UI title",
                     M_UI_COUNTDOWN               = "UI countdown format";

        #endregion

#if SIMULATE_OXIDE_PATCH
        static readonly VersionNumber CurrentOxideRustVersion = new VersionNumber(0, 0, 0);
#else
        static readonly VersionNumber CurrentOxideRustVersion = Interface.Oxide.GetAllExtensions().First(e => e.Name == "Rust").Version;
#endif
        static SmoothRestarter Instance;

        readonly FieldInfo nativeRestartRoutine = typeof(ServerMgr).GetField(
            "restartCoroutine",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        #region LangAPI EN dictionary

        readonly Dictionary<string, string> defaultMessagesEn = new Dictionary<string, string>
        {
            [M_CHAT_PREFIX]   = "<color=#d9770f>Smooth Restarter</color>:",
            [M_NO_PERMISSION] = "<color=#f04c32>You</color> have no permission to use this command",
            [M_KICK_REASON]   = "Server is restarting",
            [M_HELP]          = "/sr <color=#1a97ba>[command]</color> <color=#1aba8f>[arguments]</color>\n" +
                                "Commands: " +
                                "<color=#1a97ba>help</color>, <color=#1a97ba>status</color>, <color=#1a97ba>restart</color>, <color=#1a97ba>cancel</color>\n" +
                                "To get information about command usage, type '/sr <color=#1a97ba>help</color> <color=#1aba8f>[command]</color>'",
            [M_HELP_HELP]     = "/sr <color=#1a97ba>help</color> <color=#1aba8f>[command]</color> - Outputs general help message or command usage help if command is specified",
            [M_HELP_STATUS]   = "/sr <color=#1a97ba>status</color> - Outputs current restart status",
            [M_HELP_RESTART]  = "/sr <color=#1a97ba>restart</color> <color=#1aba8f>[time]</color> - Initiates new restart process\n" +
                                "Time must be in one of the following formats:\n" +
                                "<color=#77ba20>123</color> - delay before restart in seconds\n" +
                                "<color=#77ba20>123</color><h|m|s> - delay before restart in <hours|minutes|seconds>\n" +
                                "<color=#77ba20>1</color>h <color=#77ba20>2</color>m <color=#77ba20>3</color>s - delay before restart in hr+min+sec, all optional\n" +
                                "<color=#77ba20>1</color>:<color=#77ba20>23</color> - schedule restart on 1:23 (24hr format)",
            [M_HELP_CANCEL]   = "/sr <color=#1a97ba>cancel</color> - Cancels current restart process",
            [M_RESTARTING_ALREADY] = "Cannot do restart - already restarting. Use '/sr <color=#1a97ba>status</color>' to get info about current restart process, or try '/sr <color=#1a97ba>cancel</color>' to cancel current restart before starting new one",
            [M_NOT_RESTARTING] = "Cannot cancel restart - plugin does not perform a restart currently.",
            [M_CANCEL_SUCCESS] = "Restart was successfully cancelled",
            [M_RESTART_REASON_TIMED] = "Planned",
            [M_RESTART_REASON_OXIDE] = "New Oxide update is out",
            [M_RESTART_REASON_COMMAND] = "Command from <color=#dbc30b>{0}</color>",
            [M_RESTART_REASON_API] = "API call from <color=#dbc30b>{0}</color>",
            [M_ANNOUNCE_RESTART_INIT] = "Server will be restarted in <color=#a4db0b>{0:sfmt::<hr?+ hours ><min?+ minutes ><sec?+ seconds>}</color> ({1})",
            [M_ANNOUNCE_COUNTDOWN_TICK] = "<color=#a4db0b>{0:sfmt::<hr?+h ><min?+min ><sec?+s>}</color> left before server restart",
            [M_ANNOUNCE_RESTART_CANCELLED] = "Server restart was cancelled",
            [M_RESTART_SUCCESS] = "Restart initiated successfully",
            [M_STATUS_RESTARTING] = "Server is restarting, <color=#a4db0b>{1:sfmt::<min#0.0!+ min ><sec?+ seconds>}</color> left",
            [M_STATUS_RESTARTING_NATIVE] = "Server is restarting natively",
            [M_STATUS_PLANNED] = "Server restart planned at <color=#a4db0b>{0:hh\\:mm}</color> (<color=#a4db0b>{1:sfmt::<hr?+ hours ><min?+ minutes ><sec?+ seconds>}</color> left)",
            [M_STATUS_NO_PLANNED] = "Server is not restarting, no planned restarts found",
            [M_UI_TITLE] = "SmoothRestarter",
            [M_UI_COUNTDOWN] = "{0:sfmt::<min#2!>:<sec#2>} left"
        };

        #endregion

        readonly Regex timeParseRegex = new Regex(
            @"^\s*(?:(\d+)|(\d+\s*[HhMmSs])|(?!$)((?:(?<h>\d+)[Hh])?\s*(?:(?<m>\d+)[Mm])?\s*(?:(?<s>\d+)[Ss])?)|(\d{1,2}:\d{1,2}))\s*$",
            RegexOptions.Compiled
        );

        SmoothRestart  component;
        PluginSettings settings;
        bool           isNewOxideOut;

        bool IsRestarting => IsRestartingNative || IsRestartingComponent;
        bool IsRestartingNative => ServerMgr.Instance.Restarting;
        bool IsRestartingComponent => component.IsRestarting;

        #region Oxide hooks
        // ReSharper disable UnusedMember.Local

        void Init()
        {
            Instance = this;

            if (!settings.EnableUi)
            {
                Unsubscribe(nameof(OnUserConnected));
                Unsubscribe(nameof(OnUserDisconnected));
            }

            permission.RegisterPermission(PERMISSION_STATUS, this);
            permission.RegisterPermission(PERMISSION_RESTART, this);
            permission.RegisterPermission(PERMISSION_CANCEL, this);

            AddCovalenceCommand(settings.Commands, "CommandHandler");

            //Log("{0}", BitConverter.GetBytes(decimal.GetBits(0.123m)[3])[2]);
        }

        void OnServerInitialized()
        {
            component = ServerMgr.Instance.gameObject.AddComponent<SmoothRestart>();
            if (settings.EnableUi)
                foreach (var player in players.Connected)
                {
                    OnUserConnected(player);
                }
        }

        void OnUserConnected(IPlayer user)
        {
            var player = (BasePlayer)user.Object;

            player.gameObject.AddComponent<SmoothRestarterUi>();
        }

        void OnUserDisconnected(IPlayer user)
        {
            var player = (BasePlayer)user.Object;

            UnityEngine.Object.Destroy(player.GetComponent<SmoothRestarterUi>());
        }

        void Unload()
        {
            if (IsRestartingComponent)
            {
                component.CancelRestart(this);
            }

            if (settings.EnableUi)
            {
                SmoothRestarterUi.Cleanup();
            }

            UnityEngine.Object.Destroy(component);
            Instance = null;
        }

        // ReSharper restore UnusedMember.Local
        #endregion

        #region Command handler

        // ReSharper disable once UnusedMember.Local
        void CommandHandler(IPlayer player, string _, string[] args)
        {
            if (args.Length == 0)
            {
                Message(player, M_HELP);
            }
            else
            {
                var command = args[0];

                switch (command)
                {
                    case "status":
                        if (CheckPermission(player, PERMISSION_STATUS))
                        {
                            DateTime dateTime;
                            //Message(player, GetStatus(out dateTime), dateTime.TimeOfDay, (dateTime - DateTime.Now).TotalSeconds);
                            MessageWithCustomFormatter(player, SmoothTimeFormatter.Instance, GetStatus(out dateTime), dateTime.TimeOfDay, (dateTime - DateTime.Now).TotalSeconds);
                        }
                        break;
                    case "restart":
                        if (CheckPermission(player, PERMISSION_RESTART))
                        {
                            if (IsRestarting)
                            {
                                Message(player, M_RESTARTING_ALREADY);
                            }
                            else
                            {
                                TimeSpan time;
                                bool isTod;
                                if (args.Length == 1 || !TryParseTime(string.Join(" ", args.Skip(1)), out time, out isTod))
                                {
                                    Message(player, M_HELP_RESTART);
                                    return;
                                }

                                DateTime restartTime;

                                if (!isTod)
                                {
                                    restartTime = DateTime.Now + time;
                                }
                                else if (time < DateTime.Now.TimeOfDay)
                                {
                                    restartTime = DateTime.Today.AddDays(1) + time;
                                }
                                else
                                {
                                    restartTime = DateTime.Today + time;
                                }

                                component.DoRestart(restartTime, RestartReason.Command, player);
                                Message(player, M_RESTART_SUCCESS);
                            }
                        }
                        break;

                    case "cancel":
                        if (CheckPermission(player, PERMISSION_CANCEL))
                        {
                            if (IsRestartingNative)
                            {
                                CancelNativeRestart();
                            }
                            else if (IsRestartingComponent)
                            {
                                component.CancelRestart(player);
                            }
                            else
                            {
                                Message(player, M_NOT_RESTARTING);
                                return;
                            }
                            Message(player, M_CANCEL_SUCCESS);
                        }
                        break;
                    case "help":
                        Message(player, GetHelp(args.Skip(1)));
                        break;
                    default:
                        Message(player, M_HELP);
                        break;
                }
            }
        }

        string GetHelp(IEnumerable<string> args)
        {
            var arg = args.FirstOrDefault();
            if (arg != null)
            {
                switch (arg.ToLower())
                {
                    case "help":
                        return M_HELP_HELP;
                    case "status":
                        return M_HELP_STATUS;
                    case "restart":
                        return M_HELP_RESTART;
                    case "cancel":
                        return M_HELP_CANCEL;
                }
            }

            return M_HELP;
        }

        string GetStatus(out DateTime restartTime)
        {
            restartTime = default(DateTime);
            if (IsRestartingNative)
            {
                return M_STATUS_RESTARTING_NATIVE;
            }

            if (IsRestartingComponent)
            {
                restartTime = component.CurrentRestartTime.Value;
                return M_STATUS_RESTARTING;
            }

            if (settings.RestartTimes.Count > 0)
            {
                double _;
                restartTime = component.FindNextRestartTime(out _);
                return M_STATUS_PLANNED;
            }

            return M_STATUS_NO_PLANNED;
        }

        bool TryParseTime(string argString, out TimeSpan time, out bool isTod)
        {
            Match match = timeParseRegex.Match(argString);
            isTod = false;

            if (match.Groups[1].Success)
            {
                var seconds = int.Parse(match.Groups[1].Value);

                time = TimeSpan.FromSeconds(seconds);
            }
            else if (match.Groups[2].Success)
            {
                var str = match.Groups[2].Value;
                var specifier = str[str.Length - 1];
                var number = int.Parse(str.Remove(str.Length - 1).TrimEnd());

                switch (specifier)
                {
                    case 'H':
                    case 'h':
                        number *= 3600;
                        break;

                    case 'M':
                    case 'm':
                        number *= 60;
                        break;
                }

                time = TimeSpan.FromSeconds(number);
            }
            else if (match.Groups[3].Success)
            {
                var h = match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value) : 0;
                var m = match.Groups["m"].Success ? int.Parse(match.Groups["m"].Value) : 0;
                var s = match.Groups["s"].Success ? int.Parse(match.Groups["s"].Value) : 0;

                time = new TimeSpan(h, m, s);
            }
            else if (TimeSpan.TryParse(match.Groups[4].Value, out time))
            {
                isTod = true;
            }
            else
            {
                time = default(TimeSpan);
                return false;
            }

            if (time.TotalSeconds == 0)
            {
                return false;
            }

            return true;
        }

        bool CheckPermission(IPlayer player, string perm)
        {
            if (player.HasPermission(perm))
            {
                return true;
            }

            Message(player, M_NO_PERMISSION);
            return false;
        }

        #endregion

        #region Utility

        void FetchLatestOxideRustVersion(Action<Exception, VersionNumber> callback)
        {
            PluginLog("Fetching latest Oxide.Rust version...");
            webrequest.Enqueue("https://umod.org/games/rust.json", null,
                (responseCode, json) => {
                    if (responseCode != 200)
                    {
                        callback(
                            new Exception(
                                $"Failed to fetch latest Oxide.Rust version from uMod.org API - code {responseCode}"
                            ),
                            default(VersionNumber)
                        );
                    }
                    else
                    {
                        try
                        {
                            var response = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                            if (response == null)
                            {
                                throw new Exception("Response is null");
                            }

                            var latestVersionStr = response["latest_release_version"];

                            var array = ((string)latestVersionStr).Split('.');

                            var version = new VersionNumber(int.Parse(array[0]), int.Parse(array[1]), int.Parse(array[2]));

                            callback(null, version);
                        }
                        catch (Exception e)
                        {
                            callback(
                                new Exception($"Failed to deserialize uMod.org API response: {e.Message}"),
                                default(VersionNumber)
                            );
                        }
                    }
                }, this);
        }

        void PluginLog(string format, params object[] args)
        {
            if (settings.EnableLog)
            {
                Log(format, args);
            }
        }

        void KickAll()
        {
            foreach (var player in players.Connected.ToArray())
            {
                player.Kick(GetMessage(player, M_KICK_REASON));
            }
        }

        void CancelNativeRestart()
        {
            Debug.Assert(IsRestartingNative, "Cancelling native restart while !IsRestartingNative");

            var routine = (IEnumerator)nativeRestartRoutine.GetValue(ServerMgr.Instance);
            ServerMgr.Instance.StopCoroutine(routine);

            nativeRestartRoutine.SetValue(ServerMgr.Instance, null);

            ConsoleNetwork.BroadcastToAllClients("chat.add", new object[]
            {
                2,
                0,
                "<color=#fff>SERVER</color> Restart interrupted!"
            });

            PluginLog("Native restart was cancelled");
        }

        #endregion

        #region Plugin API
        // ReSharper disable UnusedMember.Local

        [HookMethod(nameof(IsSmoothRestarting))]
        public bool IsSmoothRestarting()
        {
            return IsRestartingComponent;
        }

        [HookMethod(nameof(GetPlannedRestarts))]
        public IReadOnlyCollection<TimeSpan> GetPlannedRestarts()
        {
            return settings.RestartTimes;
        }

        [HookMethod(nameof(GetCurrentRestartTime))]
        public DateTime? GetCurrentRestartTime()
        {
            return component.CurrentRestartTime;
        }

        [HookMethod(nameof(GetCurrentRestartReason))]
        public int? GetCurrentRestartReason()
        {
            return (int?)component.CurrentRestartReason;
        }

        [HookMethod(nameof(GetCurrentRestartInitiator))]
        public object GetCurrentRestartInitiator()
        {
            return component.CurrentRestartInitiator;
        }

        [HookMethod(nameof(InitSmoothRestart))]
        public bool InitSmoothRestart(DateTime restartTime, Plugin initiator)
        {
            if (initiator == null || !initiator.IsLoaded)
            {
                return false;
            }

            if (restartTime < DateTime.Now)
            {
                return false;
            }

            if (IsRestartingComponent)
            {
                return false;
            }

            if (IsRestartingNative)
            {
                CancelNativeRestart();
            }

            component.DoRestart(restartTime, RestartReason.ApiCall, initiator);
            return true;
        }

        [HookMethod(nameof(CancelSmoothRestart))]
        public bool CancelSmoothRestart(Plugin canceller)
        {
            if (canceller == null || !canceller.IsLoaded)
            {
                return false;
            }

            if (!IsRestartingComponent)
            {
                return false;
            }

            component.CancelRestart(canceller);
            return true;
        }

        // ReSharper restore UnusedMember.Local
        #endregion

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(defaultMessagesEn, this, "en");
        }

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            player.Message(GetMessage(player, langKey), GetMessage(player, M_CHAT_PREFIX), args);
        }

        void MessageWithCustomFormatter(IPlayer player, IFormatProvider formatProvider, string langKey, params object[] args)
        {
            var format = GetMessage(player, langKey);
            var message = string.Format(formatProvider, format, args);
            player.Message(message, GetMessage(player, M_CHAT_PREFIX));
        }

        void MessageRaw(IPlayer player, string message, params object[] args)
        {
            player.Message(message, GetMessage(player, M_CHAT_PREFIX), args);
        }

        void AnnounceRestartInit(float secondsLeft, RestartReason reason, object initiator = null)
        {
            foreach (IPlayer player in players.Connected)
            {
                var reasonStr = GetRestartReasonString(player, reason, initiator);

                //Message(player, M_ANNOUNCE_RESTART_INIT, secondsLeft.ToString("0"), reasonStr);
                MessageWithCustomFormatter(
                    player,
                    SmoothTimeFormatter.Instance,
                    M_ANNOUNCE_RESTART_INIT,
                    secondsLeft,
                    reasonStr
                );
            }
        }

        void Announce(string langKey, params object[] args)
        {
            foreach (IPlayer player in players.Connected)
            {
                Message(player, langKey, args);
            }
        }

        void AnnounceWithCustomFormatter(IFormatProvider formatProvider, string langKey, params object[] args)
        {
            foreach (IPlayer player in players.Connected)
            {
                MessageWithCustomFormatter(player, formatProvider, langKey, args);
            }
        }

        void AnnounceRaw(string message, params object[] args)
        {
            foreach (var player in players.Connected)
            {
                MessageRaw(player, message, args);
            }
        }

        string GetRestartReasonString(IPlayer player, RestartReason reason, object initiator = null)
        {
            Debug.Assert((int)reason < 2 || initiator != null);

            string reasonStr;

            switch (reason)
            {
                case RestartReason.Timed:
                    reasonStr = GetMessage(player, M_RESTART_REASON_TIMED);
                    break;
                case RestartReason.OxideUpdate:
                    reasonStr = GetMessage(player, M_RESTART_REASON_OXIDE);
                    break;
                case RestartReason.Command:
                    reasonStr = string.Format(GetMessage(player, M_RESTART_REASON_COMMAND), ((IPlayer)initiator).Name);
                    break;
                default:
                    reasonStr = string.Format(GetMessage(player, M_RESTART_REASON_API), ((Plugin)initiator).Name);
                    break;
            }

            return reasonStr;
        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            settings = PluginSettings.GetDefaults();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null)
                {
                    throw new Exception("Config is null");
                }

                if (!settings.Validate())
                {
                    LogWarning("Errors found in the configuration file, corrected values will be saved");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError("Error while loading configuration: {0}", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Nested types

        enum RestartReason
        {
            Timed,
            OxideUpdate,
            Command,
            ApiCall
        }

        #region Configuration

        class PluginSettings
        {
            float[]           uiPosCached;
            HashSet<TimeSpan> restartTimesCached;

            [JsonProperty("Daily restarts")]
            public string[] DailyRestart { get; set; }
            [JsonProperty("Restart when new Oxide.Rust is out")]
            public bool OxideUpdateRestart { get; set; }
            [JsonProperty("Initiate countdown at")]
            public int RestartCountdownMax { get; set; }
            [JsonProperty("Enable UI")]
            public bool EnableUi { get; set; }
            [JsonProperty("UI position (X,Y)")]
            public string UiPosition { get; set; }
            [JsonProperty("UI scale")]
            public float UiScale { get; set; }
            [JsonProperty("Enable console logs")]
            public bool EnableLog { get; set; }
            [JsonProperty("Commands")]
            public string[] Commands { get; set; }
            [JsonProperty("Disable chat countdown notifications")]
            public bool DisableChatCountdown { get; set; }
            [JsonProperty("Custom countdown reference points")]
            public int[] CountdownRefPts { get; set; }
            [JsonProperty("Use custom countdown reference points")]
            public bool UseCustomCountdownRefPts { get; set; }

            [JsonIgnore]
            public float UiX => uiPosCached[0];
            [JsonIgnore]
            public float UiY => uiPosCached[1];
            [JsonIgnore]
            public HashSet<TimeSpan> RestartTimes => restartTimesCached;

            public static PluginSettings GetDefaults()
            {
                return new PluginSettings {
                    DailyRestart = new[] { "0:00" },
                    OxideUpdateRestart = true,
                    RestartCountdownMax = 300,
                    EnableUi = true,
                    EnableLog = true,
                    UiPosition = "0.92, 0.92",
                    UiScale = 1.0f,
                    Commands = new[]
                    {
                        "sr",
                        "srestart",
                        "smoothrestart",
                        "smoothrestarter"
                    },
                    DisableChatCountdown = false,
                    UseCustomCountdownRefPts = false,
                    CountdownRefPts = new[] { 60, 50, 40, 30, 25, 15, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 }
                };
            }

            #region Validation methods

            public bool Validate()
            {
                bool valid = true;
                if (DailyRestart == null)
                {
                    Instance.LogWarning("Daily restart times cannot be null");
                    DailyRestart = Array.Empty<string>();
                    valid = false;
                }

                if (!ParseRestartTimes(out restartTimesCached))
                {
                    DailyRestart = restartTimesCached.Select(rt => rt.ToString("hh\\:mm")).ToArray();
                    valid = false;
                }

                if (RestartCountdownMax < 0)
                {
                    Instance.LogWarning("Restart countdown cannot be less than zero");
                    RestartCountdownMax = 0;
                    valid = false;
                }

                if (UiScale < 0.3f)
                {
                    UiScale = 0.3f;
                    Instance.LogWarning("UI scale cannot be less than 0.3");
                    valid = false;
                }
                else if (UiScale > 3f)
                {
                    UiScale = 3f;
                    Instance.LogWarning("UI scale cannot be greater than 3");
                    valid = false;
                }

                uiPosCached = new float[2];

                if (!ParseUiPos(out uiPosCached[0], out uiPosCached[1]))
                {
                    uiPosCached[0] = 0.92f;
                    uiPosCached[1] = 0.92f;
                    UiPosition = "0.92, 0.92";
                    valid = false;
                }

                if (Commands == null || Commands.Length == 0)
                {
                    Commands = new[] { "sr", "srestart", "smoothrestart", "smoothrestarter" };
                    valid = false;
                }

                if (CountdownRefPts == null)
                {
                    CountdownRefPts = new[] { 60, 50, 40, 30, 25, 15, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
                    valid = false;
                }

                if (UseCustomCountdownRefPts && CountdownRefPts.Length == 0)
                {
                    CountdownRefPts = new[] { 30, 10, 5 };
                    valid = false;
                }

                CountdownRefPts = CountdownRefPts.OrderByDescending(_ => _).ToArray();

                return valid;
            }

            bool ParseUiPos(out float fX, out float fY)
            {
                fX = fY = 0f;

                var array = UiPosition.Split(',');

                if (array.Length != 2)
                {
                    Instance.LogWarning("Invalid format of UI position: {0}", UiPosition);
                    return false;
                }

                bool valid = true;

                if (!float.TryParse(array[0], NumberStyles.Number, CultureInfo.InvariantCulture, out fX) || fX < 0f || fX > 1f)
                {
                    Instance.LogWarning("Invalid value for UI position X coordinate: {0}", array[0]);
                    valid = false;
                }

                if (!float.TryParse(array[1], NumberStyles.Number, CultureInfo.InvariantCulture, out fY) || fY < 0f || fY > 1f)
                {
                    Instance.LogWarning("Invalid value for UI position Y coordinate: {0}", array[1]);
                    valid = false;
                }

                return valid;
            }

            bool ParseRestartTimes(out HashSet<TimeSpan> restartTimes)
            {
                bool valid = true;
                restartTimes = new HashSet<TimeSpan>();

                foreach (var rt in DailyRestart)
                {
                    TimeSpan result;
                    if (!TimeSpan.TryParse(rt, out result))
                    {
                        valid = false;
                        Instance.LogWarning("Invalid time specifier: {0}", rt);
                    }

                    if (!restartTimes.Add(result))
                    {
                        valid = false;
                        Instance.LogWarning("Restart time duplication: {0}", rt);
                    }
                }

                return valid;
            }

            #endregion
        }

        #endregion

        #region SmoothRestarterUi

        class SmoothRestarterUi : MonoBehaviour
        {
            const string UI_BACKGROUND   = "smoothrestarter.ui::background",
                         UI_TITLE        = "smoothrestarter.ui::title",
                         UI_PROGRESS_BAR = "smoothrestarter.ui::progress_bar",
                         UI_SECONDS      = "smoothrestarter.ui::seconds_left";

            static HashSet<SmoothRestarterUi> AllComponents;
            static Dictionary<string, string> UiSecondsCache;
            static Dictionary<string, string> UiProgressbarCache;

            string     mainPanel;
            BasePlayer player;
            bool       isVisible;
            string     locale;

            bool IsVisible
            {
                set
                {
                    Debug.Assert(value != isVisible, "Double set isVisible to " + value);

                    SetVisible(value);
                }
            }

            public static void Cleanup()
            {
                if (AllComponents == null)
                {
                    return;
                }

                foreach (var component in AllComponents.ToArray())
                {
                    DestroyImmediate(component);
                }

                UiSecondsCache = null;
                UiProgressbarCache = null;
                AllComponents = null;
            }

            #region Unity methods

            void Awake()
            {
                if (AllComponents == null)
                {
                    AllComponents = new HashSet<SmoothRestarterUi>();
                }

                //if (mainPanel == null)
                //{

                //}

                player = GetComponent<BasePlayer>();
                locale = Instance.lang.GetLanguage(player.UserIDString);

                var uiTitle = Instance.GetMessage(player.IPlayer, M_UI_TITLE);

                var container = new CuiElementContainer
                {
                    new CuiElement
                    {
                        FadeOut = 0.2f,
                        Name = UI_BACKGROUND,
                        Parent = "Hud",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0.2 0.2 0.2 0.3",
                                FadeIn = 0.2f,
                                ImageType = Image.Type.Simple,
                                Material = "assets/content/ui/uibackgroundblur.mat"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = $"{Instance.settings.UiX - 0.08f * Instance.settings.UiScale} " +
                                            $"{Instance.settings.UiY - 0.05f * Instance.settings.UiScale}",
                                AnchorMax = $"{Instance.settings.UiX + 0.08f * Instance.settings.UiScale} " +
                                            $"{Instance.settings.UiY + 0.05f * Instance.settings.UiScale}"
                            }
                        }
                    },
                    new CuiElement
                    {
                        FadeOut = 0.2f,
                        Name = UI_TITLE,
                        Parent = UI_BACKGROUND,
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Color = "0.8 0.8 0.8 0.95",
                                FadeIn = 0.2f,
                                Align = TextAnchor.MiddleCenter,
                                Font = "robotocondensed-bold.ttf",
                                FontSize = 16,
                                Text = uiTitle /*nameof(SmoothRestarter)*/
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0.6",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                };

                mainPanel = SerializeUi(container);

                if (UiSecondsCache == null)
                {
                    UiSecondsCache = new Dictionary<string, string>();
                }

                if (UiProgressbarCache == null)
                {
                    UiProgressbarCache = new Dictionary<string, string>();
                }

                AllComponents.Add(this);
            }

            void Start()
            {
                InvokeRepeating(nameof(UiTick), 1f, 1f);
            }

            void OnDestroy()
            {
                if (isVisible)
                {
                    IsVisible = false;
                }

                AllComponents?.Remove(this);
            }

            #endregion

            void SetVisible(bool visible)
            {
                Debug.Assert(player && player.IsConnected, "Player is null or disconnected");

                if (visible)
                {
                    CuiHelper.AddUi(player, mainPanel);
                }
                else
                {
                    CuiHelper.DestroyUi(player, UI_SECONDS);
                    CuiHelper.DestroyUi(player, UI_PROGRESS_BAR);
                    CuiHelper.DestroyUi(player, UI_TITLE);
                    CuiHelper.DestroyUi(player, UI_BACKGROUND);
                }

                isVisible = visible;
            }

            void UiTick()
            {
                if (Instance.IsRestartingComponent)
                {
                    if (!isVisible)
                    {
                        IsVisible = true;
                    }

                    var secondsLeft = (Instance.component.CurrentRestartTime.Value - DateTime.Now).TotalSeconds;

                    UpdateSeconds(secondsLeft);
                    UpdateProgressBar(GetFraction(secondsLeft));
                }
                else if (isVisible)
                {
                    IsVisible = false;
                }
            }

            #region Ui update methods

            void UpdateSeconds(double secondsLeft)
            {
                CuiHelper.DestroyUi(player, UI_SECONDS);
                CuiHelper.AddUi(player, LookupUiDictionary(locale, (int)secondsLeft, UiSecondsCache, CreateSeconds));
            }

            void UpdateProgressBar(float fraction)
            {
                Debug.Assert(fraction >= 0f && fraction <= 1, "Progress bar fraction is outside of 0..1 range: " + fraction);

                CuiHelper.DestroyUi(player, UI_PROGRESS_BAR);
                CuiHelper.AddUi(player, LookupUiDictionary("any", fraction, UiProgressbarCache, CreateProgressBar));
            }

            string CreateSeconds(int secondsLeft)
            {
                return SerializeUi(
                    new CuiElementContainer
                    {
                        new CuiElement
                        {
                            FadeOut = 0.2f,
                            Name = UI_SECONDS,
                            Parent = UI_BACKGROUND,
                            Components =
                            {
                                new CuiTextComponent
                                {
                                    Color = "0.8 0.8 0.8 0.95",
                                    FadeIn = 0.2f,
                                    Align = TextAnchor.MiddleCenter,
                                    Font = "robotocondensed-bold.ttf",
                                    FontSize = 16,
                                    Text = string.Format(
                                        SmoothTimeFormatter.Instance,
                                        Instance.GetMessage(player.IPlayer, M_UI_COUNTDOWN),
                                        secondsLeft
                                    )
                                },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 0.45"
                                }
                            }
                        }
                    }
                );
            }

            string CreateProgressBar(float fraction)
            {
                return SerializeUi(
                    new CuiElementContainer
                    {
                        new CuiElement
                        {
                            FadeOut = 0.2f,
                            Name = UI_PROGRESS_BAR,
                            Parent = UI_BACKGROUND,
                            Components =
                            {
                                new CuiImageComponent
                                {
                                    Color = GetFractionColor(fraction),
                                    FadeIn = 0f,
                                    ImageType = Image.Type.Simple,
                                    Material = "assets/content/ui/namefontmaterial.mat"
                                },
                                new CuiRectTransformComponent
                                {
                                    AnchorMin = $"{0.5f - fraction * 0.45f} 0.45",
                                    AnchorMax = $"{0.5f + fraction * 0.45f} 0.6"
                                }
                            }
                        }
                    }
                );
            }

            #endregion

            #region Utility

            float GetFraction(double secondsLeft)
            {
                var norm = Instance.settings.RestartCountdownMax;

                if (secondsLeft > norm)
                {
                    return 1f;
                }

                return (float)(secondsLeft / norm);
            }

            string GetFractionColor(float fraction)
            {
                Debug.Assert(fraction <= 1 && fraction >= 0, "GetFractionColor: fraction is not clamped");

                float r;
                float g;

                if (fraction > 0.5f)
                {
                    r = Mathf.Lerp(.85f, .15f, fraction / 2);
                    g = 0.85f;
                }
                else
                {
                    r = 0.85f;
                    g = Mathf.Lerp(.15f, .85f, fraction * 2);
                }

                return $"{r} {g} 0.2 1";
            }

            static string LookupUiDictionary<T>(string locale, T value, Dictionary<string, string> dict, Func<T, string> createFunction)
            {
                string key = $"{locale}->{value}";
                string returnVal;

                if (!dict.TryGetValue(key, out returnVal))
                {
                    returnVal = createFunction(value);
                    dict[key] = returnVal;
                }

                return returnVal;
            }

            string SerializeUi(CuiElementContainer container)
            {
                return JsonConvert.SerializeObject(
                    container,
                    Formatting.None,
                    new JsonSerializerSettings {
                        DefaultValueHandling = DefaultValueHandling.Ignore
                    }
                ).Replace("\\n", "\n");
            }

            #endregion
        }

        #endregion

        #region SmoothRestart

        class SmoothRestart : MonoBehaviour
        {
            IEnumerator     restartRoutine;
            Queue<DateTime> restartQueue;
            int             rtCountdownStart;
            bool            doTimedRestarts;
            bool            doRestartChecks;

            public DateTime? CurrentRestartTime { get; private set; }
            public RestartReason? CurrentRestartReason { get; private set; }
            public object CurrentRestartInitiator { get; private set; }

            public bool IsRestarting => restartRoutine != null;

            bool DoRestartChecks
            {
                set
                {
                    Debug.Assert(value != doRestartChecks, "Double set DoRestartChecks to " + value);

                    if (doRestartChecks != value)
                    {
                        doRestartChecks = value;
                        if (value)
                        {
                            InvokeRepeating(nameof(RestartCheck), 1f, 1f);
                        }
                        else
                        {
                            CancelInvoke(nameof(RestartCheck));
                        }
                    }
                }
            }

            #region Unity methods

            void Awake()
            {
                rtCountdownStart = Instance.settings.RestartCountdownMax;

                if (Instance.settings.RestartTimes.Count > 0)
                {
                    restartQueue = new Queue<DateTime>(
                        Instance.settings.RestartTimes.Select(ts => DateTime.Today + ts)
                    );
                    doTimedRestarts = true;

                    Instance.PluginLog(
                        "Loaded {0} restart time(s):\n{1}",
                        restartQueue.Count,
                        string.Join("\n", restartQueue.Select(ts => ts.TimeOfDay.ToString("hh\\:mm").PadLeft(50)))
                    );
                }

                if (Instance.settings.OxideUpdateRestart)
                {
                    Invoke(nameof(OxideVersionCheck), 5f);
                }
            }

            void Start()
            {
                if (doTimedRestarts || Instance.settings.OxideUpdateRestart)
                {
                    DoRestartChecks = true;
                }
            }

            void OnDestroy()
            {
                if (IsRestarting)
                {
                    CancelRestart(Instance);
                }
            }

            #endregion

            #region Public API

            public void DoRestart(DateTime restartTime, RestartReason restartReason, object restartInitiator)
            {
                Debug.Assert(!Instance.IsRestarting, "DoRestart while restarting already");
                Debug.Assert(restartTime > DateTime.Now, "Initiating restart in the past");

                var totalSecondsLeft = (float)(restartTime - DateTime.Now).TotalSeconds;

                DoRestartChecks = false;
                restartRoutine = InitRestartRoutine(totalSecondsLeft);
                CurrentRestartTime = restartTime;
                CurrentRestartReason = restartReason;
                CurrentRestartInitiator = restartInitiator;

                StartCoroutine(restartRoutine);

                OnRestartInit(totalSecondsLeft, restartReason, restartInitiator);
            }

            public void CancelRestart(object canceller)
            {
                Debug.Assert(canceller != null, "Restart canceller is null");

                StopCoroutine(restartRoutine);
                Cleanup();

                DoRestartChecks = true;

                OnRestartCancelled(canceller);
            }

            public DateTime FindNextRestartTime(out double diff)
            {
                DateTime restartTime;
                DateTime now = DateTime.Now;
                while ((restartTime = restartQueue.Peek()) < now)
                    CycleQueue();

                diff = (restartTime - now).TotalSeconds;

                return restartTime;
            }

            #endregion

            void OxideVersionCheck()
            {
                Debug.Assert(Instance.settings.OxideUpdateRestart, "OxideVersionCheck called when config value is false");
                Debug.Assert(!Instance.isNewOxideOut, "OxideVersionCheck called when already new oxide patch detected");

                Instance.FetchLatestOxideRustVersion(
                    (e, v) => {
                        if (e != null)
                        {
                            Instance.PluginLog(e.Message);
                            Instance.PluginLog("Scheduling check after 1 minute...");
                            Invoke(nameof(OxideVersionCheck), 60f);
                        }
                        else if (v > CurrentOxideRustVersion)
                        {
                            Instance.PluginLog("New Oxide.Rust version detected {0} -> {1}", CurrentOxideRustVersion, v);
                            Instance.isNewOxideOut = true;
                        }
                        else
                        {
                            Instance.PluginLog("Current Oxide.Rust version is up-to-date, scheduling check after 10 minutes...");
                            Invoke(nameof(OxideVersionCheck), 600f);
                        }
                    });
            }

            void RestartCheck()
            {
                Debug.Assert(!IsRestarting, "Restart check while IsRestarting");

                if (Instance.IsRestartingNative)
                {
                    return; // since we have no observer on native restart routine, we do not pause invokes of this function, but simply interrupt it here
                            // might wanna think about a workaround to raise event when native restart has started/interrupted (Harmony patch probably)
                }

                if (Instance.isNewOxideOut)
                {
                    DoRestart(DateTime.Now.AddSeconds(rtCountdownStart), RestartReason.OxideUpdate, null);
                }
                else
                {
                    DateTime restartTime;

                    if (NeedsRestartOnTime(out restartTime))
                    {
                        CycleQueue();
                        DoRestart(restartTime, RestartReason.Timed, null);
                    }
                }
            }

            bool NeedsRestartOnTime(out DateTime restartTime)
            {
                if (restartQueue == null || restartQueue.Count == 0)
                {
                    restartTime = default(DateTime);
                    return false;
                }

                double diff;
                restartTime = FindNextRestartTime(out diff);

                if (diff <= rtCountdownStart)
                {
                    return true;
                }

                return false;
            }

            void CycleQueue() => restartQueue.Enqueue(restartQueue.Dequeue().AddDays(1));

            #region Component events

            void OnRestartInit(float secondsLeft, RestartReason reason, object initiator)
            {
                Instance.PluginLog(
                    "Server restart initiated in {0} seconds, reason: {1}, initiator: {2}",
                    secondsLeft,
                    reason,
                    initiator
                );

                if (Interface.CallHook("OnSmoothRestartInit", secondsLeft, reason, initiator) == null)
                {
                    Instance.AnnounceRestartInit(secondsLeft, reason, initiator);
                }
            }

            void OnRestartTick(int secondsLeft)
            {
                Instance.PluginLog("Server restart in progress, {0} seconds left", secondsLeft);

                if (Interface.CallHook("OnSmoothRestartTick", secondsLeft) == null && !Instance.settings.DisableChatCountdown)
                {
                    // The code below is so harsh Im feeling embarassed with it, but it is a solution I came with at the moment
                    // which does not force me to rewrite the whole messaging system
                    //foreach (var player in Instance.players.Connected)
                    //{
                    //    var message = string.Format(
                    //        new IntTimeFormatter(),
                    //        Instance.GetMessage(player, M_ANNOUNCE_COUNTDOWN_TICK),
                    //        secondsLeft
                    //    );

                    //    Instance.MessageRaw(player, message);
                    //}

                    // I rewrote the messaging system :/
                    Instance.AnnounceWithCustomFormatter(
                        SmoothTimeFormatter.Instance,
                        M_ANNOUNCE_COUNTDOWN_TICK,
                        secondsLeft
                    );
                }
            }

            void OnRestartCancelled(object canceller)
            {
                Instance.PluginLog("Server restart was cancelled by {0}", canceller);

                Cleanup();

                if (Interface.CallHook("OnSmoothRestartCancelled", canceller) == null)
                {
                    Instance.Announce(M_ANNOUNCE_RESTART_CANCELLED);
                }
            }

            #endregion

            IEnumerator InitRestartRoutine(float totalSecondsLeft)
            {
                Debug.Assert(totalSecondsLeft > 0);

                while (totalSecondsLeft > 0)
                {
                    var nextCd = GetNextCountdownValue(Mathf.CeilToInt(totalSecondsLeft - 1f));

                    yield return new WaitForSecondsRealtime(totalSecondsLeft - nextCd);

                    OnRestartTick(nextCd);
                    totalSecondsLeft = nextCd;
                }

                Cleanup();
                RestartNow();
            }

            void Cleanup()
            {
                restartRoutine = null;
                CurrentRestartTime = null;
                CurrentRestartReason = null;
                CurrentRestartInitiator = null;
            }

            void RestartNow()
            {
                Instance.KickAll();
                Global.quit(null);
            }

            int GetNextCountdownValue(int secondsLeft)
            {
                Debug.Assert(secondsLeft > 0, "GetNextCountdownValue: Seconds left <= 0");

                if (secondsLeft > Instance.settings.RestartCountdownMax)
                {
                    return Instance.settings.RestartCountdownMax;
                }

                if (Instance.settings.UseCustomCountdownRefPts)
                {
                    Debug.Assert(Instance.settings.CountdownRefPts.Length != 0, "Empty countdown refpts array");

                    var next = Instance.settings.CountdownRefPts.FirstOrDefault(p => p <= secondsLeft);

                    if (next < 0)
                    {
                        Debug.LogAssertion("Custom cdrefpt is < 0");
                        next = 0;
                    }

                    return next;
                }

                var divider = secondsLeft > 400 ? 100 :
                    secondsLeft > 150 ? 50 :
                    secondsLeft > 70 ? 25 :
                    secondsLeft > 20 ? 10 :
                    secondsLeft > 10 ? 5 : 1;

                var remainder = secondsLeft % divider;

                return secondsLeft - remainder;
            }
        }

        #endregion

        #region SmoothTimeFormatter

        class SmoothTimeFormatter : IFormatProvider, ICustomFormatter
        {
            public static SmoothTimeFormatter Instance = new SmoothTimeFormatter();

            static readonly Regex TemplateRegex = new Regex(
                @"<(?<tmpl>hr|min|sec)(?:#(?<pl>\d+)(?:\.(?<pr>\d+))?)?(?<mod>[!?])?(?:\+(?<append>.*?))?>",
                RegexOptions.Compiled
            );

            public string Format(string format, object arg, IFormatProvider formatProvider)
            {
                if (format != null && format.StartsWith("sfmt::"))
                {
                    format = format.Remove(0, 6);
                    if (string.IsNullOrWhiteSpace(format))
                    {
                        format = "<sec!>";
                    }

                    if (arg is TimeSpan)
                    {
                        return Format((TimeSpan)arg, format);
                    }

                    if (arg is int || arg is double || arg is float)
                    {
                        double nArg = Convert.ToDouble(arg);
                        return Format(TimeSpan.FromSeconds(nArg), format);
                    }
                }

                try
                {
                    if (arg is string)
                    {
                        return (string)arg;
                    }

                    if (arg is IFormattable)
                    {
                        return ((IFormattable)arg).ToString(format, CultureInfo.InvariantCulture);
                    }

                    throw new Exception();
                }
                catch
                {
                    throw new ArgumentException(
                        nameof(SmoothTimeFormatter) + " does not support type " + arg.GetType().Name,
                        nameof(arg)
                    );
                }
            }

            public object GetFormat(Type formatType)
            {
                return this;
            }

            static int GetDigits(int number)
            {
                if (number < 10) return 1;
                if (number < 100) return 2;
                if (number < 1000) return 3;
                if (number < 10000) return 4;
                if (number < 100000) return 5;
                if (number < 1000000) return 6;

                throw new ArgumentOutOfRangeException(nameof(number), "GetDigits does not support values >= 10e6");
            }

            static int GetDigits(double number)
            {
                return BitConverter.GetBytes(decimal.GetBits(new decimal(Math.Round(number, 6)))[3])[2];
            }

            static string PadNumber(double value, int padding, int precision)
            {
                //string padding = "";
                int intPart = (int)value;
                int intPartDigits = GetDigits(intPart);
                int floatPartDigits = GetDigits(value);
                int floatPart = (int)(Math.Pow(10, floatPartDigits) * (value - intPart));

                //SmoothRestarter.Instance.Log("Int part of {0} is {1}, digits - {2}", value, intPart, intPartDigits);
                //SmoothRestarter.Instance.Log("Float part of {0} is {1}, digits - {2}", value, floatPart, floatPartDigits);

                StringBuilder builder = new StringBuilder();

                if (intPartDigits < padding)
                {
                    builder.Append('0', padding - intPartDigits);
                }

                builder.Append(intPart);

                if (precision != 0)
                {
                    builder.Append('.');
                    int fpi = builder.Length;
                    builder.Append(floatPart);

                    if (precision > floatPartDigits)
                    {
                        builder.Append('0', precision - floatPartDigits);
                    }
                    else if (precision < floatPartDigits)
                    {
                        var num = fpi + precision - 1;
                        builder.Remove(num + 1, builder.Length - num - 1);
                    }

                    //int floatPartIndex = builder.Length;

                    //if (precision > 0 && floatPartDigits < precision)
                    //{
                    //    builder.Append(floatPart);
                    //    builder.Append('0', precision - floatPartDigits);
                    //}
                    //else if (precision < 0 && floatPartDigits > (precision *= -1))
                    //{
                    //    builder.Append(floatPart);
                    //    int num = floatPartIndex + precision;
                    //    builder.Remove(num, builder.Length - num - 1);
                    //}
                }

                //SmoothRestarter.Instance.Log("Padded number {0} is {1}", value, builder.ToString());

                return builder.ToString();
            }

            static string Format(TimeSpan ts, string format)
            {
                var templates = Pool.GetList<Template>();
                templates.AddRange(OrderTemplates(GetTemplates(format)));

                var stringBuilder = new StringBuilder(format);

                for (var i = 0; i < templates.Count; i++)
                {
                    var tmpl = templates[i];

                    string replaceValue;

                    double value;
                    switch (tmpl.Name)
                    {
                        case "sec":
                            if (tmpl.Type == TemplateType.Total)
                                value = ts.TotalSeconds;
                            else
                                value = ts.Seconds;
                            break;

                        case "min":
                            if (tmpl.Type == TemplateType.Total)
                                value = ts.TotalMinutes;
                            else
                                value = ts.Minutes;
                            break;

                        default:
                            if (tmpl.Type == TemplateType.Total)
                                value = ts.TotalHours;
                            else
                                value = ts.Hours;
                            break;
                    }

                    if (tmpl.Type == TemplateType.Optional && value == 0)
                    {
                        replaceValue = string.Empty;
                    }
                    else
                    {
                        replaceValue = PadNumber(value, tmpl.PadLeft, tmpl.PadRight);

                        replaceValue += tmpl.Appender;
                    }

                    stringBuilder.Remove(tmpl.StartIndex, tmpl.Length);

                    stringBuilder.Insert(tmpl.StartIndex, replaceValue);

                    var lMut = replaceValue.Length - tmpl.Length;

                    for (var j = i + 1; j < templates.Count; j++)
                    {
                        var t = templates[j];
                        t.StartIndex += lMut;
                        //t.EndIndex += lMut;

                        templates[j] = t;
                    }
                }

                Pool.FreeList(ref templates);

                return stringBuilder.ToString();

                //var strSecondsTotal = ts.TotalSeconds.ToString("0");
                //var strMinutesTotal = ts.TotalMinutes.ToString("0.##");
                //var strHoursTotal = ts.TotalHours.ToString("0.###");

                //var strHours = ts.Hours.ToString("0");
                //var strMinutes = ts.Minutes.ToString("0");
                //var strSeconds = ts.Seconds.ToString("0");

                //var sb = new StringBuilder(format);

                //sb.Replace("<sec::t>", strSecondsTotal);
                //sb.Replace("<min::t>", strMinutesTotal);
                //sb.Replace("<hr::t>", strHoursTotal);

                //sb.Replace("<hr>", strHours);
                //sb.Replace("<min>", strMinutes);
                //sb.Replace("<sec>", strSeconds);

                //ReplaceOptionalTemplate(sb, "hr", ts.Hours > 0 ? strHours : string.Empty);
                //ReplaceOptionalTemplate(sb, "min", ts.Minutes > 0 ? strMinutes : string.Empty);
                //ReplaceOptionalTemplate(sb, "sec", ts.Seconds > 0 ? strSeconds : string.Empty);

                //return sb.ToString();
            }

            //static void ReplaceOptionalTemplate(StringBuilder builder, string template, string replaceValue)
            //{
            //    string templateFull = "<" + template + "?";
            //    string format = builder.ToString();

            //    int tIndex = 0;
            //    while ((tIndex = format.IndexOf(templateFull, tIndex)) != -1)
            //    {
            //        var cl = format.IndexOf('>', tIndex);
            //        if (cl == -1)
            //            cl = format.Length - 1;

            //        var length = cl - tIndex + 1;

            //        if (length > templateFull.Length + 1 && replaceValue.Length != 0)
            //        {
            //            replaceValue += format.Substring(tIndex + templateFull.Length, length - templateFull.Length - 1);
            //        }

            //        builder.Remove(tIndex, length);

            //        builder.Insert(tIndex, replaceValue);

            //        tIndex = cl + (replaceValue.Length - length) + 1;

            //        format = builder.ToString();
            //    }
            //}

            static IEnumerable<Template> GetTemplates(string format)
            {
                var matches = TemplateRegex.Matches(format);

                for (var i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];

                    var template = new Template();

                    template.Name = match.Groups["tmpl"].Value;
                    template.Type = !match.Groups["mod"].Success ? TemplateType.Normal :
                        match.Groups["mod"].Value == "!" ? TemplateType.Total : TemplateType.Optional;

                    if (match.Groups["pl"].Success)
                    {
                        template.PadLeft = int.Parse(match.Groups["pl"].Value);
                    }

                    if (match.Groups["pr"].Success)
                    {
                        template.PadRight = int.Parse(match.Groups["pr"].Value);
                    }

                    template.Appender = match.Groups["append"].Value;

                    template.StartIndex = match.Index;
                    template.Length = match.Length;

                    yield return template;
                }
            }

            static IEnumerable<Template> OrderTemplates(IEnumerable<Template> templates)
            {
                return templates.OrderBy(t => t.StartIndex);
            }

            enum TemplateType
            {
                Normal,
                Total,
                Optional
            }

            struct Template
            {
                public string       Name;
                public TemplateType Type;
                public int          PadLeft;
                public int          PadRight;
                public string       Appender;
                public int          StartIndex;
                public int          Length;
            }
        }

        #endregion

        #endregion
    }
}
