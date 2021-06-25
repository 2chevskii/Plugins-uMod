using Newtonsoft.Json;

using System.Collections.Generic;
using System.Collections;
using Oxide.Core.Libraries.Covalence;
using System.Text.RegularExpressions;
using System;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("SmoothRestarter", "2CHEVSKII", "2.0.0")]
    public class SmoothRestarter : CovalencePlugin
    {
        const string PERMISSION_RESTART = "smoothrestart.canrestart";

        static DateTime NextRestartTime { get; set; }

        static Queue<DateTime> RestartTimes { get; set; }

        #region Oxide Hooks

        void OnServerInitialized()
        {
            //ServerMgr.Instance.InvokeRepeating(CheckTimers, 60, 60);

            List<DateTime> list = new List<DateTime>();

            for (int i = 0; i < Settings.Current.EveryDayRestart.Length; i++)
            {
                var rt = Settings.Current.EveryDayRestart[i];
                int hour, minute;
                var t = ParseDayTime(rt, out hour, out minute);

                if (!t) continue;

                var dt = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.Today.Day, hour, minute, 0);

                if (DateTime.Now.CompareTo(dt) >= 0)
                {
                    dt = dt.AddDays(1);
                }

                list.Add(dt);
            }

            var ordered = list.OrderBy(dt => dt.Date).ThenBy(dt => dt.Hour).ThenBy(dt => dt.Minute);

            RestartTimes = new Queue<DateTime>(ordered.Reverse());
        }

        void Unload()
        {
            ServerMgr.Instance.CancelInvoke(CheckTimers);
        }

        #endregion

        bool HasPermission(IPlayer player)
        {
            return player.HasPermission(PERMISSION_RESTART);
        }

        bool IsServerRestartingNatively()
        {
            return ServerMgr.Instance.Restarting;
        }

        void CancelNativeRestart()
        {
            var rc = typeof(ServerMgr).GetField("restartCoroutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var val = rc.GetValue(ServerMgr.Instance);
            if(val != null)
            {
                ServerMgr.Instance.StopCoroutine(val as IEnumerator);
                rc.SetValue(ServerMgr.Instance, null);
            }
        }

        bool ParseDayTime(string strTime, out int hour, out int minute)
        {
            hour = minute = 0;
            var pattern = @"(\d{1,2})[:.-](\d{1,2})";

            var regex = new Regex(pattern);

            var match = regex.Match(strTime.Trim());

            if (!match.Success) return false;

            hour = int.Parse(match.Groups[1].Value);
            minute = int.Parse(match.Groups[2].Value);
            return true;
        }

        void CheckTimers()
        {

        }

        #region Configuration

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Settings.Current = Config.ReadObject<Settings>();
        }

        protected override void LoadDefaultConfig()
        {
            Settings.Current = Settings.GetDefault();
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(Settings.Current);
        }

        class Settings
        {
            public static Settings GetDefault()
            {
                return new Settings
                {
                    EveryDayRestart = new[]
                    {
                        "0:00"
                    },
                    NewDevblogRestart = false,
                    NewOxideBuildRestart = false,
                    RestartAlertSeconds = 300
                };
            }

            public static Settings Current { get; set; }

            public string[] EveryDayRestart { get; set; }
            public bool NewDevblogRestart { get; set; }
            public bool NewOxideBuildRestart { get; set; }
            public int RestartAlertSeconds { get; set; }
        }

        #endregion
    }
}
