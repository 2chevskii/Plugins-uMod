// #define SIMULATE_OXIDE_PATCH

using Newtonsoft.Json;

using System.Collections.Generic;
using System.Collections;
using Oxide.Core.Libraries.Covalence;
using System.Text.RegularExpressions;
using System;
using System.Linq;
using Oxide.Core;
using Oxide.Game.Rust;
using UnityEngine;
using System.Text;
using System.Reflection;

using Pool = Facepunch.Pool;

namespace Oxide.Plugins
{
    [Info("SmoothRestarter", "2CHEVSKII", "2.0.0")]
    public class SmoothRestarter : CovalencePlugin
    {
        #region Fields


        #region Permissions

        const string PERMISSION_RESTART = "smoothrestarter.restart";
        const string PERMISSION_CANCEL = "smoothrestarter.cancel";

        #endregion

        #region Message keys

        const string M_CHAT_PREFIX = "Chat prefix";
        const string M_KICK_REASON = "Kick reason";

        const string M_NO_PERMISSION = "[Alert] No permission";
        const string M_USAGE_ERROR = "[Alert] Command usage error";
        const string M_CANCEL_SUCCESS = "[Alert] Restart cancelled";
        const string M_RESTART_SUCCESS = "[Alert] Restart init success";
        const string M_NOT_RESTARTING = "[Alert] Server not restarting";
        const string M_CANNOT_RESTART = "[Alert] Cannot restart server";
        const string M_CANNOT_CANCEL = "[Alert] Cannot cancel server restart";

        const string M_OXIDE_PATCH_RESTART = "[Announce] Restart (Oxide patch)";
        const string M_TIMED_RESTART = "[Announce] Restart (planned)";
        const string M_COMMAND_RESTART = "[Announce] Restart (command initiated)";
        const string M_RESTART_CANCELLED = "[Announce] Restart cancelled";
        const string M_RESTART_COUNTDOWN = "[Announce] Restart countdown";

        #endregion

        static SmoothRestarter instance;

        readonly Dictionary<string, string> defaultMessagesEn = new Dictionary<string, string>
        {
            [M_CHAT_PREFIX] = "[SmoothRestarter]",
            [M_KICK_REASON] = "Server restart",

            [M_NO_PERMISSION] = "You are not allowed to use this command.",
            [M_USAGE_ERROR] = "Usage: /srestart <delay time> | /srestart cancel | /srestart status",
            [M_CANCEL_SUCCESS] = "Restart cancelled successfully.",
            [M_RESTART_SUCCESS] = "Restart initiated.",
            [M_NOT_RESTARTING] = "Server is not restarting at the moment.",
            [M_CANNOT_RESTART] = "Could not initiate server restart.",
            [M_CANNOT_CANCEL] = "Could not cancel server restart.",

            [M_OXIDE_PATCH_RESTART] = "New Oxide patch is out! Server will be restarted in {0}.",
            [M_TIMED_RESTART] = "Planned server restart in {0}.",
            [M_COMMAND_RESTART] = "Server restart was initiated by {1}. Restarting in {0}.",
            [M_RESTART_CANCELLED] = "Restart was cancelled.",
            [M_RESTART_COUNTDOWN] = "Server restarting in {0}.",
        };

        readonly int[] countDownSeconds = new[]
        {
            300,
            250,
            200,
            150,
            100,
            80,
            50,
            40,
            30,
            25,
            20,
            15,
            10,
            5,
            4,
            3,
            2,
            1
        };

        Settings settings;
        FieldInfo nativeRestartRoutine;
        SmoothRestart component;


        #endregion

        #region Oxide Hooks


        void Init()
        {
            instance = this;

            permission.RegisterPermission(PERMISSION_RESTART, this);
            permission.RegisterPermission(PERMISSION_CANCEL, this);

            AddUniversalCommand("srestart", nameof(CommandHandler));

            nativeRestartRoutine = typeof(ServerMgr).GetField("restartCoroutine", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        void OnServerInitialized()
        {
            component = SingletonComponent<ServerMgr>.Instance.gameObject.AddComponent<SmoothRestart>();
        }

        void Unload()
        {
            UnityEngine.Object.Destroy(SingletonComponent<ServerMgr>.Instance.GetComponent<SmoothRestart>());
            instance = null;
        }


        #endregion

        #region Command handler


        bool CommandHandler(IPlayer player, string command, string[] args)
        {
            var subcommand = args.Length > 0 ? args[0] : null;

            if (subcommand == null)
            {
                MessagePlayer(player, M_USAGE_ERROR);
            }
            else if (subcommand.Equals("status", StringComparison.OrdinalIgnoreCase))
            {
                if (!component.IsRestarting)
                {
                    MessagePlayer(player, M_NOT_RESTARTING);
                }
                else
                {
                    var timeleft = component.SecondsLeft;

                    MessagePlayer(player, M_RESTART_COUNTDOWN, timeleft);
                }
            }
            else if (subcommand.Equals("cancel", StringComparison.OrdinalIgnoreCase))
            {
                if (!CheckPermission(player, PERMISSION_CANCEL))
                {
                    MessagePlayer(player, M_NO_PERMISSION);
                }
                else if (!component.IsRestarting)
                {
                    MessagePlayer(player, M_NOT_RESTARTING);
                }
                else if (!component.CancelRestart(player))
                {
                    MessagePlayer(player, M_CANNOT_CANCEL);
                }
                else
                {
                    MessagePlayer(player, M_CANCEL_SUCCESS);
                }
            }
            else
            {
                var strTime = string.Join(" ", args);

                try
                {
                    var parsedTime = new TimeParser(strTime).ParseDelayTime();

                    if (!CheckPermission(player, PERMISSION_RESTART))
                    {
                        MessagePlayer(player, M_NO_PERMISSION);
                    }
                    else if (!component.IsRestarting && component.DoCommandRestart(player, parsedTime))
                    {
                        MessagePlayer(player, M_RESTART_SUCCESS);
                    }
                    else MessagePlayer(player, M_CANNOT_RESTART);
                }
                catch
                {
                    MessagePlayer(player, M_USAGE_ERROR);
                }
            }

            return true;
        }


        #endregion

        #region Helpers


        bool CheckPermission(IPlayer player, string permission)
        {
            return player.IsServer || player.HasPermission(permission);
        }

        #region Log

        void LogRestart(RestartReason reason, int secondsLeft, IPlayer initiator = null)
        {
            if (!settings.LogEnabled)
            {
                return;
            }

            switch (reason)
            {
                case RestartReason.Timed:
                    Log($"Timed server restart initiated, {secondsLeft}s left.");
                    break;
                case RestartReason.OxideUpdate:
                    Log($"New Oxide version found, server restarting in {secondsLeft}s.");
                    break;
                case RestartReason.Command:
                    Log($"Server restart initiated by '{initiator.Name}'. {secondsLeft}s left.");
                    break;
            }
        }

        void LogRestartCancel(IPlayer initiator)
        {
            if (!settings.LogEnabled)
            {
                return;
            }

            Log($"Server restart cancelled by {initiator.Name}");
        }

        void LogNativeRestartCancel()
        {
            if (!settings.LogEnabled)
            {
                return;
            }

            Log("Native restart cancelled");
        }

        #endregion

        List<DateTime> GetRestartTimesFromConfig()
        {
            var times = settings.EveryDayRestart;
            var list = Pool.GetList<DateTime>();

            for (int i = 0; i < times.Length; i++)
            {
                var time = times[i];

                var timeParser = new TimeParser(time);
                TimeSpan parsedTime;

                try
                {
                    parsedTime = timeParser.ParseDayTime();
                }
                catch (Exception e)
                {
                    LogError("Failed to parse restart time from the configuration: {0}", e.Message);
                    continue;
                }

                list.Add(DateTime.Now.Date + parsedTime);
            }

            if (list.IsEmpty())
            {
                LogWarning("Timed restart list is empty");
            }

            return list;
        }

        int GetNextCountdownValue(int secondsLeft)
        {
            if (secondsLeft <= 0)
            {
                return 0;
            }

            for (int i = 0; i < countDownSeconds.Length; i++)
            {
                var el = countDownSeconds[i];
                if (el >= secondsLeft)
                {
                    continue;
                }

                return el;
            }

            return 0;
        }

        string FormatTimeSpan(TimeSpan ts)
        {
            var builder = new StringBuilder();

            var hrs = ts.Hours;
            var mins = ts.Minutes;
            var seconds = ts.Seconds;

            if (hrs > 0 || (mins <= 0 && seconds <= 0))
            {
                builder.Append(hrs).Append('h');
                if (mins > 0 || seconds > 0)
                {
                    builder.Append(' ');
                }
            }
            if (mins > 0 || (hrs <= 0 && seconds <= 0))
            {
                builder.Append(mins).Append('m');
                if (seconds > 0)
                {
                    builder.Append(' ');
                }
            }
            if (seconds > 0 || (hrs <= 0 && mins <= 0))
            {
                builder.Append(seconds).Append('s');
            }

            return builder.ToString();
        }


        #endregion

        #region Oxide update checks


        void GetLatestOxideVersion(Action<Exception, VersionNumber> callback)
        {
            webrequest.Enqueue("https://umod.org/games/rust.json", null, (code, data) =>
            {
                if (code != 200)
                {
                    callback(new Exception($"Failed to get Oxide version from uMod API ({code})"), new VersionNumber());
                }
                else
                {
                    try
                    {
                        var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(data);

                        var ver = ((string)obj["latest_release_version"]).Split('.');

                        var verN = new VersionNumber(int.Parse(ver[0]), int.Parse(ver[1]), int.Parse(ver[2]));

                        callback(null, verN);
                    }
                    catch (Exception e)
                    {
                        callback(new Exception("Failed to deserialize uMod API response", e), new VersionNumber());
                    }
                }
            }, this);
        }

        VersionNumber GetCurrentOxideVersion()
        {
            var rustExtension = Interface.Oxide.GetAllExtensions().First(ext => ext.Name == "Rust") as RustExtension;

            return rustExtension.Version;
        }

        bool IsOxideOutdated(VersionNumber current, VersionNumber newest)
        {
            return current < newest;
        }


        #endregion

        #region Native restart check/cancel


        bool IsServerRestartingNatively()
        {
            return ServerMgr.Instance.Restarting;
        }

        void CancelNativeRestart()
        {
            var val = nativeRestartRoutine.GetValue(ServerMgr.Instance);
            ServerMgr.Instance.StopCoroutine(val as IEnumerator);
            nativeRestartRoutine.SetValue(ServerMgr.Instance, null);

            LogNativeRestartCancel();
        }


        #endregion

        #region Helper types


        class TimeParser
        {
            readonly char[] separators = new[] { '.', ':' };
            readonly Regex delayTimeRegex = new Regex(@"(?:(\d+)h|hr)?\s*(?:(\d+)m|min)?\s*(?:(\d+)s|sec)?");
            readonly Regex digitRegex = new Regex(@"\d+");

            readonly string input;

            public TimeParser(string input)
            {
                this.input = input.Trim();
            }

            Exception GenerateException(string message)
            {
                return new Exception(message + string.Format(" ({0})", input));
            }

            public TimeSpan ParseDayTime()
            {
                var splitted = input.Split(separators);

                if (splitted.Length != 2)
                {
                    throw GenerateException("Day time must be in format 'hour:minute'");
                }

                int hour, minute;

                if (!int.TryParse(splitted[0], out hour))
                {
                    throw GenerateException("Could not parse hour");
                }

                if (hour < 0 || hour > 23)
                {
                    throw GenerateException("Hour must be in range between 0 and 23");
                }

                if (!int.TryParse(splitted[1], out minute))
                {
                    throw GenerateException("Could not parse minute");
                }

                if (minute < 0 || minute > 59)
                {
                    throw GenerateException("Minute must be in range between 0 and 59");
                }

                return new TimeSpan(hour, minute, 0);
            }

            public TimeSpan ParseDelayTime()
            {
                var match = delayTimeRegex.Match(input);

                if (!match.Success)
                {
                    throw GenerateException("Input string does not match the pattern");
                }

                var hoursStr = match.Groups[1];
                var minutesStr = match.Groups[2];
                var secondsStr = match.Groups[3];

                int hours, minutes, seconds;
                hours = minutes = seconds = 0;

                if (hoursStr != null && hoursStr.Length > 0)
                {
                    hours = int.Parse(digitRegex.Match(hoursStr.Value).Value);
                }

                if (minutesStr != null && minutesStr.Length > 0)
                {
                    minutes = int.Parse(digitRegex.Match(minutesStr.Value).Value);
                }

                if (secondsStr != null && secondsStr.Length > 0)
                {
                    seconds = int.Parse(digitRegex.Match(secondsStr.Value).Value);
                }

                if (hours == 0 && minutes == 0 && seconds == 0)
                {
                    throw GenerateException("Restart delay cannot be less than one second");
                }

                return new TimeSpan(hours, minutes, seconds);
            }
        }

        public enum RestartReason
        {
            Timed = 0,
            OxideUpdate = 1,
            Command = 2
        }

        public enum NotificationType
        {
            RestartInit,
            RestartTimeLeft,
            RestartCancelled
        }


        #endregion

        #region Core


        class SmoothRestart : MonoBehaviour
        {
            #region Fields and props

            public bool IsRestarting => restartRoutine != null;
            public int SecondsLeft => (int)(currentRestartTime - DateTime.Now).TotalSeconds;

            bool newOxideOut;
            Coroutine restartRoutine;
            DateTime currentRestartTime;
            RestartReason currentRestartReason;

            Queue<DateTime> restartTimes;
            Settings settings;

            VersionNumber currentOxideVersion;

            #endregion

            #region Lifecycle methods

            void Start()
            {
                settings = instance.settings;
                currentOxideVersion = instance.GetCurrentOxideVersion();

                var list = instance.GetRestartTimesFromConfig();
                var now = DateTime.Now;
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];

                    if (t.Hour < now.Hour || (t.Hour == now.Hour && t.Minute <= now.Minute))
                    {
                        t.AddDays(1);
                    }
                }

                restartTimes = new Queue<DateTime>(list.OrderBy(t => t.Date).ThenBy(t => t.TimeOfDay));

                Pool.FreeList(ref list);

                InvokeRepeating(nameof(CheckRestartNeeded), 5f, 1f);

                if (settings.NewOxideBuildRestart)
                {
                    InvokeRepeating(nameof(CheckNewOxideOut), 1f, 300f);
                }
            }

            void OnDestroy()
            {
                CancelInvoke();
                CancelRestart();
            }

            #endregion

            #region Restart methods

            void DoOxideUpdateRestart()
            {
                var restartTime = new TimeSpan(0, 0, settings.OxideBuildRestartDelay);

                if (!CanSmoothRestart(RestartReason.OxideUpdate, (int)restartTime.TotalSeconds))
                {
                    return;
                }

                NotifyRestartInit(RestartReason.OxideUpdate, restartTime);
                instance.LogRestart(RestartReason.OxideUpdate, (int)restartTime.TotalSeconds);

                DoRestart(DateTime.Now + restartTime, RestartReason.OxideUpdate);
                OnSmoothRestart(currentRestartReason, SecondsLeft);
            }

            void DoTimedRestart()
            {
                var rt = restartTimes.Peek();
                DequeueRestartTimeAndUpdateDate();

                var ts = rt - DateTime.Now;

                if (!CanSmoothRestart(RestartReason.Timed, (int)ts.TotalSeconds))
                {
                    return;
                }

                NotifyRestartInit(RestartReason.Timed, ts);
                instance.LogRestart(RestartReason.Timed, (int)ts.TotalSeconds);

                DoRestart(rt, RestartReason.Timed);
                OnSmoothRestart(currentRestartReason, SecondsLeft);
            }

            public bool DoCommandRestart(IPlayer player, TimeSpan delay)
            {
                if (IsRestarting)
                {
                    return false;
                }

                var rt = DateTime.Now + delay;

                if (!CanSmoothRestart(RestartReason.Command, (int)delay.TotalSeconds, player))
                {
                    return false;
                }

                NotifyRestartInit(RestartReason.Command, delay, player);
                instance.LogRestart(RestartReason.Command, (int)delay.TotalSeconds, player);

                DoRestart(rt, RestartReason.Command);
                OnSmoothRestart(currentRestartReason, SecondsLeft, player);
                return true;
            }

            void DoRestart(DateTime restartTime, RestartReason reason)
            {
                if (instance.IsServerRestartingNatively())
                {
                    instance.CancelNativeRestart();
                }

                CancelRestart();

                currentRestartTime = restartTime;
                currentRestartReason = reason;

                restartRoutine = StartCoroutine(nameof(GetRestartRoutine), currentRestartTime);
            }

            IEnumerator GetRestartRoutine(DateTime restartTime)
            {
                int secondsLeft, cdSeconds;
                while ((secondsLeft = (int)(restartTime - DateTime.Now).TotalSeconds) > 0)
                {
                    cdSeconds = instance.GetNextCountdownValue(secondsLeft);

                    yield return new WaitForSecondsRealtime(secondsLeft - cdSeconds);

                    NotifyRestartCountdown(restartTime - DateTime.Now);
                }

                foreach (var player in instance.players.Connected.ToList())
                {
                    player.Kick(instance.lang.GetMessage(M_KICK_REASON, instance, player.Id));
                }

                restartRoutine = null;

                ConsoleSystem.Run(ConsoleSystem.Option.Server, "quit", Array.Empty<object>());
            }

            public bool CancelRestart(IPlayer canceller)
            {
                if (!CanSmoothCancel(canceller))
                {
                    return false;
                }

                NotifyRestartCancelled(canceller);

                CancelRestart();
                OnSmoothRestartCancelled(canceller);

                return true;
            }

            void CancelRestart()
            {
                if (IsRestarting)
                {
                    StopCoroutine(restartRoutine);
                    restartRoutine = null;
                }
            }

            #endregion

            #region Helpers

            void DequeueRestartTimeAndUpdateDate()
            {
                var t = restartTimes.Dequeue();
                restartTimes.Enqueue(t.AddDays(1));
            }

            #endregion

            #region Checks

            void CheckRestartNeeded()
            {
                if (IsRestarting)
                {
                    return;
                }

                if (newOxideOut)
                {
                    DoOxideUpdateRestart();
                }
                else if (restartTimes.Count < 1)
                {
                    return;
                }
                else if (restartTimes.Peek() < DateTime.Now)
                {
                    DequeueRestartTimeAndUpdateDate();
                    CheckRestartNeeded();
                }
                else if ((restartTimes.Peek() - DateTime.Now).TotalSeconds <= instance.countDownSeconds.Max())
                {
                    DoTimedRestart();
                }
            }

            void CheckNewOxideOut()
            {
                if (newOxideOut)
                {
                    return;
                }

#if SIMULATE_OXIDE_PATCH
                newOxideOut = true;
#else
                var current = instance.GetCurrentOxideVersion();

                instance.GetLatestOxideVersion((e, latest) =>
                {
                    if (e != null)
                    {
                        instance.LogError(e.Message);
                    }
                    else newOxideOut = instance.IsOxideOutdated(current, latest);
                });
#endif
            }

            #endregion

            #region Notification methods

            void NotifyRestartInit(RestartReason reason, TimeSpan timeLeft, IPlayer initiator = null)
            {
                var secondsLeft = (int)timeLeft.TotalSeconds;
                var targetPlayers = BasePlayer.activePlayerList;

                if (!CanSmoothNotify(NotificationType.RestartInit, secondsLeft, targetPlayers, reason, initiator))
                {
                    return;
                }

                var strTime = instance.FormatTimeSpan(timeLeft);
                string msg;

                switch (reason)
                {
                    case RestartReason.Timed:
                        msg = M_TIMED_RESTART;
                        break;
                    case RestartReason.OxideUpdate:
                        msg = M_OXIDE_PATCH_RESTART;
                        break;
                    default:
                        msg = M_COMMAND_RESTART;
                        break;
                }

                if (initiator != null)
                {
                    NotifyPlayers(targetPlayers, msg, strTime, initiator.Name);
                }
                else NotifyPlayers(targetPlayers, msg, strTime);
            }

            void NotifyRestartCountdown(TimeSpan ts)
            {
                var targetPlayers = BasePlayer.activePlayerList;

                if (!CanSmoothNotify(NotificationType.RestartTimeLeft, (int)ts.TotalSeconds, targetPlayers, currentRestartReason))
                {
                    return;
                }

                NotifyPlayers(targetPlayers, M_RESTART_COUNTDOWN, instance.FormatTimeSpan(ts));
            }

            void NotifyRestartCancelled(IPlayer canceller)
            {
                var targetPlayers = BasePlayer.activePlayerList;

                if (!CanSmoothNotify(NotificationType.RestartCancelled, -1, targetPlayers, currentRestartReason, canceller))
                {
                    return;
                }

                NotifyPlayers(targetPlayers, M_RESTART_CANCELLED);
            }

            void NotifyPlayers(IList<BasePlayer> targets, string msg, params object[] args)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    instance.MessagePlayer(targets[i].IPlayer, msg, args);
                }
            }

            #endregion

            bool CanSmoothRestart(RestartReason reason, int secondsLeft, IPlayer initiator = null)
            {
                return Interface.CallHook("CanSmoothRestart", reason, secondsLeft, initiator) == null;
            }

            bool CanSmoothNotify(
                NotificationType type,
                int secondsLeft,
                ListHashSet<BasePlayer> targets,
                RestartReason reason = RestartReason.Command,
                IPlayer initiator = null
            )
            {
                return Interface.CallHook("CanSmoothNotify", type, secondsLeft, targets, reason, initiator) == null;
            }

            bool CanSmoothCancel(IPlayer initiator)
            {
                return Interface.CallHook("CanSmoothCancel", initiator) == null;
            }

            void OnSmoothRestart(RestartReason reason, int secondsLeft, IPlayer initiator = null)
            {
                Interface.Oxide.CallHook("OnSmoothRestart", reason, secondsLeft, initiator);
            }

            void OnSmoothRestartCancelled(IPlayer initiator)
            {
                Interface.Oxide.CallHook("OnSmoothRestartCancelled", initiator);
            }
        }

        #endregion

        #region Configuration


        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                settings = Config.ReadObject<Settings>();
            }
            catch (Exception e)
            {
                LogError("Failed to load configuration:\n{0}Loading default config instead (config file will be reset)", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            settings = Settings.Default;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(settings);

        class Settings
        {
            public static Settings Default => new Settings
            {
                EveryDayRestart = new[]
                {
                    "0:00"
                },
                NewOxideBuildRestart = false,
                OxideBuildRestartDelay = 300,
                LogEnabled = true
            };

            public string[] EveryDayRestart { get; set; }
            public bool NewOxideBuildRestart { get; set; }
            public int OxideBuildRestartDelay { get; set; }
            public bool LogEnabled { get; set; }
        }


        #endregion

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(defaultMessagesEn, this, "en");
        }

        void MessagePlayer(IPlayer player, string langMessage, params object[] args)
        {
            var msg = instance.lang.GetMessage(langMessage, instance, player.Id);
            var prefix = instance.lang.GetMessage(M_CHAT_PREFIX, instance, player.Id);

            var formatted = prefix + string.Format(msg, args);

            player.Message(formatted);
        }

        #endregion
    }
}
