using Newtonsoft.Json;

using System.Collections.Generic;
using System.Collections;
using Oxide.Core.Libraries.Covalence;
using System.Text.RegularExpressions;
using System;
using System.Linq;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("SmoothRestarter", "2CHEVSKII", "2.0.0")]
    public class SmoothRestarter : CovalencePlugin
    {
        const string PERMISSION_RESTART = "smoothrestart.canrestart";

        static RestartTime NextRestartTime { get; set; }

        static Queue<RestartTime> RestartTimes { get; set; }

        #region Oxide Hooks

        void OnServerInitialized()
        {
            List<RestartTime> list = new List<RestartTime>();

            for (int i = 0; i < Settings.Current.EveryDayRestart.Length; i++)
            {
                var str = Settings.Current.EveryDayRestart[i];

                try
                {
                    var parsed = RestartTime.Parse(str);

                    list.Add(parsed);
                }
                catch (Exception e)
                {
                    LogError(e.Message);
                }
            }

            var dts = new List<DateTime>();
            var now = DateTime.Now;
            var today = DateTime.Now.Date;
            for(int i = 0; i < list.Count; i++)
            {
                var rt = list[i];

                if(rt.hour < now.Hour || (rt.hour == now.Hour && rt.minute <= now.Minute))
                {
                    dts.Add(now.AddDays(1) + new TimeSpan(rt.hour, rt.minute, 0));
                } else
                {
                    //dts.Add(now + new TimeSpan())
                }
            }


        }

        void Unload()
        {

        }

        #endregion

        bool IsServerRestartingNatively()
        {
            return ServerMgr.Instance.Restarting;
        }

        void CancelNativeRestart()
        {
            var rc = typeof(ServerMgr).GetField("restartCoroutine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var val = rc.GetValue(ServerMgr.Instance);
            if (val != null)
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

        struct RestartTime
        {
            public readonly int hour;
            public readonly int minute;

            static Exception GenerateParseException(string inputStr)
            {
                return new Exception($"Restart time string ({inputStr}) must be in form of 'hour:minute'.");
            }

            public RestartTime(int hour, int minute)
            {
                this.hour = hour;
                this.minute = minute;
            }

            public static RestartTime Parse(string str)
            {
                if (str.Length < 3)
                {
                    throw GenerateParseException(str);
                }

                int hour, minute;
                char separator = ':';

                string[] separated = str.Split(separator);

                if (separated.Length < 2)
                {
                    throw GenerateParseException(str);
                }

                if (!int.TryParse(separated[0], out hour) || !int.TryParse(separated[1], out minute))
                {
                    throw GenerateParseException(str);
                }

                if (hour < 0 || hour > 23 || minute < 0 || minute > 60)
                {
                    throw GenerateParseException(str);
                }

                return new RestartTime(hour, minute);
            }
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
