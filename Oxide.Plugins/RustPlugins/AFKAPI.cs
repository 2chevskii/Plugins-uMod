//#define DEBUGCOMPILE //Uncomment this line to get debug information (Or useless console spam, depends on POV)
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;
using System;

/* AFKAPI -> Public Rust Plugin [uMod and RustWorkshop]
 * Author -> 2CHEVSKII :
 * : https://umod.org/user/2CHEVSKII 
 * : https://github.com/2chevskii/ 
 * : https://rustworkshop.space/members/2chevskii.8/ 
 * : https://www.youtube.com/channel/UCgq5jjofrmIXCagJXqrMG9w 
 * Changelog:
 * [0.1.0] - Initial release
 * [0.1.1] - Fixes
 * */

namespace Oxide.Plugins
{
    [Info("AFK API", "2CHEVSKII", "0.1.1")]
    [Description("API to check, if player is AFK")]
    public class AFKAPI : RustPlugin
    {

        #region -Fields-
        
        #region [Permissions]


        private string PermissionUse { get; set; }
        private string PermissionKick { get; set; }

        private void RegisterPermissions()
        {
            PermissionUse = Name.ToLower() + ".use";
            PermissionKick = Name.ToLower() + ".kick";
            permission.RegisterPermission(PermissionUse, this);
            permission.RegisterPermission(PermissionKick, this);
#if(DEBUGCOMPILE)
            Puts($"Permissions are registered successfully! ({PermissionUse}, {PermissionKick})");
#endif
        }


#endregion

        #region [Storage]


        private List<AFKPlayer> TrackedPlayers { get; set; }
        private List<BasePlayer> AFKPlayers { get; set; }
        private Timer AFKTimer { get; set; }
        private static AFKAPI Instance { get; set; }
        private PluginSettings Settings { get; set; }


        #endregion
        
        #endregion

        #region -Configuration-


        protected override void LoadDefaultConfig()
        {
#if(DEBUGCOMPILE)
            Puts("LoadDefaultConfig called...");
#endif
            Config.Clear();
            Settings = GetDefaultSettings();
            Puts("Creating new configuration file...");
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(Settings, true);

        protected override void LoadConfig()
        {
#if(DEBUGCOMPILE)
            Puts("LoadConfig called...");
#endif
            base.LoadConfig();
            try
            {
                Settings = Config.ReadObject<PluginSettings>();
                if(Settings == null)
                    throw new JsonException("Unable to read configuration file, it will be reset.");
            }
            catch
            {
#if(DEBUGCOMPILE)
                Puts("Exception thrown in LoadConfig...");
#endif
                LoadDefaultConfig();
            }
        }

        private PluginSettings GetDefaultSettings()
        {
#if(DEBUGCOMPILE)
            Puts("Default config object generated...");
#endif
            return new PluginSettings
            {
                GeneralSettings = new PluginSettings.GeneralPluginSettings
                {
                    SecondsToAFKStatus = 300,
                    StatusRefreshInterval = 5,
                    AllowSetupThroughAPI = true
                },
                CompareSettings = new PluginSettings.ComparePluginSettings
                {
                    CompareBuild = true,
                    CompareCommunication = true,
                    CompareItemActions = true,
                    CompareRotation = true
                },
                NotificationSettings = new PluginSettings.NotificationPluginSettings
                {
                    NotifyPlayer = true,
                    NotifyPlayerSound = true,
                    NotifyPlayerTime = 60
                }
            };
        }
        

        #endregion

        #region -Localization-
        

        //Dictionary keys
        private const string mprefix = "Plugin prefix";
        private const string mplayerafk = "Player AFK status";
        private const string merrornoargs = "No valid arguments error";
        private const string moffline = "Offline status";
        private const string misafk = "Is AFK status";
        private const string misnotafk = "Is not AFK status";
        private const string mafkplayerlist = "AFK player list";
        private const string mnoafkplayers = "No players AFK atm";
        private const string mnotify = "Notification for AFK player";
        private const string mnoperm = "No permission";

        private readonly Dictionary<string, string> defmessages = new Dictionary<string, string>
        {
            { mprefix, "<color=#6797e5>[AFK API]</color>" },
            { mplayerafk, "The player <color=#fff268>{0}</color> AFK status: {1}" },
            { merrornoargs, "<color=orange>Wrong command usage, no valid arguments specified</color>" },
            { moffline, "<color=red>OFFLINE</color>" },
            { misafk, "<color=yellow>IS AFK</color>" },
            { misnotafk, "<color=lime>NOT AFK</color>" },
            { mafkplayerlist, "<color=yellow>AFK player list:</color>{0}" },
            { mnoafkplayers, "<color=lime>Currently no players are AFK</color>" },
            { mnotify, "<color=red>Start moving, or you will be punished for AFK!</color>" },
            { mnoperm, "<color=red>You have no permission to run this command!</color>" }
        };

        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");
        
        private void Messenger(BasePlayer player, bool prefix, string message, params string[] args) => covalence.Players.FindPlayerById(player.UserIDString).Message(lang.GetMessage(message, this, player.UserIDString), prefix ? lang.GetMessage(mprefix, this, player.UserIDString) + " " : string.Empty, args);


        #endregion

        #region -API-
        

        private bool IsPlayerAFK(ulong id)
        {
#if(DEBUGCOMPILE)
            Puts($"IsPlayerAFK API method called with id:{id.ToString()}, {AFKPlayers.Any(p => p.userID == id)} returned...");
#endif
            return AFKPlayers.Any(p => p.userID == id);
        }
        
        private long GetPlayerAFKTime(ulong id)
        {
#if(DEBUGCOMPILE)
            Puts($"AFK time called for id:{id.ToString()}");
#endif
            var targetPlayer = TrackedPlayers.Find(t => t.Player.userID == id);
            if(targetPlayer != null) return targetPlayer.TimeAFK;
            else return -1L;
        }
        
        private List<BasePlayer> GetAFKPlayers()
        {
#if(DEBUGCOMPILE)
            Puts("AFK player list called...");
#endif
            return AFKPlayers;
        }
        
        private bool AFKAPI_Setup(string newSettings, bool needToSave = false)
        {
            if(!Settings.GeneralSettings.AllowSetupThroughAPI) return false;
            if(newSettings != null)
            {
                var _newSettings = JsonConvert.DeserializeObject<PluginSettings>(newSettings);
                if(_newSettings != null && _newSettings as PluginSettings != null)
                {
                    _newSettings.GeneralSettings.AllowSetupThroughAPI = true;
                    Settings = _newSettings;
                    if(needToSave) SaveConfig();
                    CheckHookSubscriptions();
                    InitializeTimer();
#if(DEBUGCOMPILE)
                    Puts("New settings were given through API...");
#endif
                    return true;
                }
                else return false;
            }
            else return false;
        }


        #endregion

        #region -Helpers-


        /// <summary>
        /// Unsibscribes plugin from unnecessary hooks as specified in configuration
        /// </summary>
        private void CheckHookSubscriptions()
        {
            if(!Settings.CompareSettings.CompareBuild)
            {
#if(DEBUGCOMPILE)
                Puts("Build hooks unsubscribed...");
#endif
                Unsubscribe("CanBuild");

            }
            else
            {
#if(DEBUGCOMPILE)
                Puts("Build hooks subscribed...");
#endif
                Subscribe("CanBuild");
            }
            if(!Settings.CompareSettings.CompareCommunication)
            {
#if(DEBUGCOMPILE)
                Puts("Communication hooks unsubscribed...");
#endif
                Unsubscribe("OnPlayerChat");
                Unsubscribe("OnPlayerVoice");
            }
            else
            {
#if(DEBUGCOMPILE)
                Puts("Communication hooks subscribed...");
#endif
                Subscribe("OnPlayerChat");
                Subscribe("OnPlayerVoice");
            }
            if(!Settings.CompareSettings.CompareItemActions)
            {
#if(DEBUGCOMPILE)
                Puts("Item hooks unsubscribed...");
#endif
                Unsubscribe("CanCraft");
                Unsubscribe("OnPlayerActiveItemChanged");
                Unsubscribe("OnItemAction");
                Unsubscribe("CanMoveItem");
            }
            else
            {
#if(DEBUGCOMPILE)
                Puts("Item hooks subscribed...");
#endif
                Subscribe("CanCraft");
                Subscribe("OnPlayerActiveItemChanged");
                Subscribe("OnItemAction");
                Subscribe("CanMoveItem");
            }
        }

        private void InitializeTimer()
        {
            if(AFKTimer != null && !AFKTimer.Destroyed) AFKTimer.Destroy();
            AFKTimer = timer.Every(Settings.GeneralSettings.StatusRefreshInterval, () =>
            {
                foreach(var trackedPlayer in TrackedPlayers)
                {
                    trackedPlayer.CheckPosition(Settings.CompareSettings.CompareRotation);
                    if(trackedPlayer.TimeAFK >= Settings.GeneralSettings.SecondsToAFKStatus && !AFKPlayers.Contains(trackedPlayer.Player))
                        AFKPlayers.Add(trackedPlayer.Player);
                    else if(trackedPlayer.TimeAFK < Settings.GeneralSettings.SecondsToAFKStatus && AFKPlayers.Contains(trackedPlayer.Player))
                        AFKPlayers.Remove(trackedPlayer.Player);
                    if(Settings.NotificationSettings.NotifyPlayer && Settings.GeneralSettings.SecondsToAFKStatus - trackedPlayer.TimeAFK < Settings.NotificationSettings.NotifyPlayerTime)
                    {
                        Messenger(trackedPlayer.Player, true, mnotify);
                        if(Settings.NotificationSettings.NotifyPlayerSound)
                        {
                            var beep = new Effect();
                            beep.Init(Effect.Type.Generic, trackedPlayer.Player.transform.position, trackedPlayer.Player.transform.forward);
                            beep.pooledString = "assets/prefabs/tools/pager/effects/beep.prefab";
                            EffectNetwork.Send(beep, trackedPlayer.Player.Connection);
                        }
                    }
                }
            });
#if(DEBUGCOMPILE)
            Puts($"New timer initialized with {Settings.GeneralSettings.StatusRefreshInterval.ToString()} interval. " +
                $"Notifications are {(Settings.NotificationSettings.NotifyPlayer ? "ON" : "OFF")}. " +
                $"Sounds are {(Settings.NotificationSettings.NotifyPlayerSound ? "ON" : "OFF")}...");
#endif
        }


        #endregion

        #region -Hooks-


        private void Init()
        {
            Instance = this;
            RegisterPermissions();
            TrackedPlayers = new List<AFKPlayer>();
            AFKPlayers = new List<BasePlayer>();
#if(DEBUGCOMPILE)
            PrintWarning("Debug mode is active...");
#endif
        }

        private void Loaded()
        {
            CheckHookSubscriptions();
            foreach(var player in BasePlayer.activePlayerList)
                TrackedPlayers.Add(new AFKPlayer(player));
            InitializeTimer();
        }

        private void Unload() => Instance = null;

        private void OnPlayerInit(BasePlayer player) => TrackedPlayers.Add(new AFKPlayer(player));

        private void OnPlayerDisconnected(BasePlayer player, string reason) => TrackedPlayers.Remove(TrackedPlayers.Find(dp => dp.Player == player));

        private void CanBuild(Planner planner, Construction prefab, Construction.Target target)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == planner.GetOwnerPlayer());
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void CanCraft(ItemCrafter itemCrafter, ItemBlueprint bp, int amount)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == itemCrafter.GetComponent<BasePlayer>());
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void OnPlayerActiveItemChanged(BasePlayer player, Item oldItem, Item newItem)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == player);
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void OnPlayerChat(ConsoleSystem.Arg arg)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == arg.Player());
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void OnPlayerVoice(BasePlayer player, byte[] data)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == player);
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void OnItemAction(Item item, string action, BasePlayer player)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == player);
            if(tplayer != null) tplayer.TimeAFK = 0;
        }

        private void CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            var tplayer = TrackedPlayers.Find(p => p.Player == playerLoot.GetComponent<BasePlayer>());
            if(tplayer != null) tplayer.TimeAFK = 0;
        }


        #endregion

        #region -Commands-


        /// <summary>
        /// Returns tracked player status
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [ChatCommand("isafk")]
        private void CmdIsAFK(BasePlayer player, string command, string[] args)
        {
            if(player != null && permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                if(args.Length == 1)
                {
                    string status = default(string);
                    if(BasePlayer.Find(args[0]) != null)
                    {
                        if(AFKPlayers.Contains(BasePlayer.Find(args[0]))) status = lang.GetMessage(misafk, this, player.UserIDString);
                        else status = lang.GetMessage(misnotafk, this, player.UserIDString);
                    }
                    else status = lang.GetMessage(moffline, this, player.UserIDString);
                    Messenger(player, true, mplayerafk, args[0], status);
                }
                else Messenger(player, true, merrornoargs);
            }
            else Messenger(player, true, mnoperm);
        }

        /// <summary>
        /// Returns list of AFK players 
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [ChatCommand("getafk")]
        private void CmdGetAFK(BasePlayer player, string command, string[] args)
        {
            if(player != null && permission.UserHasPermission(player.UserIDString, PermissionUse))
            {
                string reply = string.Empty;
                if(AFKPlayers.Count < 1) Messenger(player, true, mnoafkplayers);
                else
                {
                    foreach(var entry in AFKPlayers) reply += "\n" + entry.displayName;
                    Messenger(player, false, mafkplayerlist, reply);
                }
            }
            else Messenger(player, true, mnoperm);
        }

        /// <summary>
        /// Kicks all the AFK players at once
        /// </summary>
        /// <param name="player"></param>
        /// <param name="command"></param>
        /// <param name="args"></param>
        [ChatCommand("kickafk")]
        private void CmdKickAllAFK(BasePlayer player, string command, string[] args)
        {
            if(player != null && permission.UserHasPermission(player.UserIDString, PermissionKick))
            {
                foreach(var afkplayer in AFKPlayers)
                    if(afkplayer.IsConnected) afkplayer.Kick("AFK for too long");
            }
            else Messenger(player, true, mnoperm);
        }


        #endregion

        #region -Nested classes-


        /// <summary>
        /// Stores data and realizes position and rotation checks for tracked by API players
        /// </summary>
        private class AFKPlayer
        {
            internal BasePlayer Player { get; private set; }
            internal uint TimeAFK { get; set; }
            private Vector3 LastPosition { get; set; }
            private Quaternion LastRotation { get; set; }
            public AFKPlayer(BasePlayer player)
            {
                Player = player;
                TimeAFK = 0u;
                LastPosition = player.transform.position;
                LastRotation = player.eyes.bodyRotation;
            }
            internal void CheckPosition(bool checkRotation)
            {
                if(Player.transform.position == LastPosition)
                {
                    if(checkRotation)
                        CheckRotation();
                    else
                        TimeAFK += (uint)Instance.Settings.GeneralSettings.StatusRefreshInterval;
                }
                else
                {
                    LastPosition = Player.transform.position;
                    TimeAFK = 0;
                }
            }
            internal void CheckRotation()
            {
                if(Player.eyes.bodyRotation == LastRotation) TimeAFK += (uint)Instance.Settings.GeneralSettings.StatusRefreshInterval;
                else
                {
                    LastRotation = Player.eyes.bodyRotation;
                    TimeAFK = 0;
                }
            }
        }
        
        /// <summary>
        /// Plugin configuration class
        /// </summary>
        [Serializable]
        private class PluginSettings
        {
            [JsonProperty(PropertyName = "General Settings")]
            internal GeneralPluginSettings GeneralSettings { get; set; }
            [JsonProperty(PropertyName = "Accuracy Settings")]
            internal ComparePluginSettings CompareSettings { get; set; }
            [JsonProperty(PropertyName = "Notification Settings")]
            internal NotificationPluginSettings NotificationSettings { get; set; }
            internal class GeneralPluginSettings
            {
                [JsonProperty(PropertyName = "Seconds to consider player is AFK")]
                internal int SecondsToAFKStatus { get; set; }
                [JsonProperty(PropertyName = "AFK Check Interval")]
                internal int StatusRefreshInterval { get; set; }
                [JsonProperty(PropertyName = "Allow other plugins change settings of this API")]
                internal bool AllowSetupThroughAPI { get; set; }
            }
            internal class ComparePluginSettings
            {
                [JsonProperty(PropertyName = "Check rotation in pair with position (more accurate)")]
                internal bool CompareRotation { get; set; }
                [JsonProperty(PropertyName = "Check for build attempts")]
                internal bool CompareBuild { get; set; }
                [JsonProperty(PropertyName = "Check for communication attempts (chat/voice)")]
                internal bool CompareCommunication { get; set; }
                [JsonProperty(PropertyName = "Check for item actions (craft/change/use/move)")]
                internal bool CompareItemActions { get; set; }
            }
            internal class NotificationPluginSettings
            {
                [JsonProperty(PropertyName = "Notify player before considering him AFK")]
                internal bool NotifyPlayer { get; set; }
                [JsonProperty(PropertyName = "Notify player X seconds before considering him AFK")]
                internal int NotifyPlayerTime { get; set; }
                [JsonProperty(PropertyName = "Notify with sound")]
                internal bool NotifyPlayerSound { get; set; }
            }

        }


        #endregion

    }
}
