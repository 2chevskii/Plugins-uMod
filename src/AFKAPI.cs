using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Oxide.Core.Libraries.Covalence;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("AFK API", "2CHEVSKII", "1.0.1")]
    [Description("Complex developer API for 'Away from keyboard' players.")]
    class AFKAPI : CovalencePlugin
    {

        #region -Fields-

        const string BEEP_SOUND_PREFAB = "assets/prefabs/tools/pager/effects/beep.prefab";

        #region [Permissions]

        const string PERMISSION_USE = "afkapi.use";
        const string PERMISSION_KICK = "afkapi.kick";

        #endregion

        #region [Storage]

        IEnumerable<IPlayer> AFKPlayers
        {
            get
            {
                return trackedPlayers.Where(kv => kv.Value.IsAFK).Select(kv => kv.Key);
            }
        }

        static AFKAPI instance;
        Settings settings;
        Dictionary<IPlayer, AFKPlayer> trackedPlayers;

        #endregion

        #endregion

        #region -Configuration-


        protected override void LoadDefaultConfig()
        {
            settings = Settings.Default;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(settings, true);

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<Settings>();
                if (settings == null)
                {
                    throw new JsonException("Unable to read configuration file, it will be reset.");
                }
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        class Settings
        {
            public static Settings Default
            {
                get
                {
                    return new Settings
                    {
                        GeneralSettings = new Settings.GeneralPluginSettings
                        {
                            SecondsToAFKStatus = 300,
                            StatusRefreshInterval = 5,
                            AllowSetupThroughAPI = true
                        },
                        CompareSettings = new Settings.ComparePluginSettings
                        {
                            CompareBuild = true,
                            CompareCommunication = true,
                            CompareItemActions = true,
                            CompareRotation = true
                        },
                        NotificationSettings = new Settings.NotificationPluginSettings
                        {
                            NotifyPlayer = true,
                            NotifyPlayerSound = true,
                            NotifyPlayerTime = 60
                        }
                    };
                }
            }
            [JsonProperty(PropertyName = "General Settings")]
            public GeneralPluginSettings GeneralSettings { get; set; }
            [JsonProperty(PropertyName = "Accuracy Settings")]
            public ComparePluginSettings CompareSettings { get; set; }
            [JsonProperty(PropertyName = "Notification Settings")]
            public NotificationPluginSettings NotificationSettings { get; set; }
            public class GeneralPluginSettings
            {
                [JsonProperty(PropertyName = "Seconds to consider player is AFK")]
                public int SecondsToAFKStatus { get; set; }
                [JsonProperty(PropertyName = "AFK Check Interval")]
                public int StatusRefreshInterval { get; set; }
                [JsonProperty(PropertyName = "Allow other plugins change settings of this API")]
                public bool AllowSetupThroughAPI { get; set; }
            }
            public class ComparePluginSettings
            {
                [JsonProperty(PropertyName = "Check rotation in pair with position (more accurate)")]
                public bool CompareRotation { get; set; }
                [JsonProperty(PropertyName = "Check for build attempts")]
                public bool CompareBuild { get; set; }
                [JsonProperty(PropertyName = "Check for communication attempts (chat/voice)")]
                public bool CompareCommunication { get; set; }
                [JsonProperty(PropertyName = "Check for item actions (craft/change/use/move)")]
                public bool CompareItemActions { get; set; }
            }
            public class NotificationPluginSettings
            {
                [JsonProperty(PropertyName = "Notify player before considering him AFK")]
                public bool NotifyPlayer { get; set; }
                [JsonProperty(PropertyName = "Notify player X seconds before considering him AFK")]
                public int NotifyPlayerTime { get; set; }
                [JsonProperty(PropertyName = "Notify with sound")]
                public bool NotifyPlayerSound { get; set; }
            }

        }


        #endregion

        #region -Localization-


        const string M_CHAT_PREFIX = "Plugin prefix",
        M_PLAYER_AFK_STATUS = "Player AFK status",
        M_ERROR_NO_ARGS = "No valid arguments error",
        M_OFFLINE_STATUS = "Offline status",
        M_IS_AFK_STATUS = "Is AFK status",
        M_IS_NOT_AFK_STATUS = "Is not AFK status",
        M_AFK_PLAYER_LIST = "AFK player list",
        M_NO_AFK_PLAYERS_FOUND = "No players AFK atm",
        M_NOTIFICATION = "Notification for AFK player",
        M_NO_PERMISSION = "No permission",
        M_KICK_REASON = "Kick reason";

        readonly Dictionary<string, string> defmessages = new Dictionary<string, string>
        {
            { M_CHAT_PREFIX, "<color=#6797e5>[AFK API]</color> " },
            { M_PLAYER_AFK_STATUS, "The player <color=#fff268>{0}</color> AFK status: {1}" },
            { M_ERROR_NO_ARGS, "<color=orange>Wrong command usage, no valid arguments specified</color>" },
            { M_OFFLINE_STATUS, "<color=red>OFFLINE</color>" },
            { M_IS_AFK_STATUS, "<color=yellow>IS AFK</color>" },
            { M_IS_NOT_AFK_STATUS, "<color=lime>NOT AFK</color>" },
            { M_AFK_PLAYER_LIST, "<color=yellow>AFK player list:</color>\n{0}" },
            { M_NO_AFK_PLAYERS_FOUND, "<color=lime>Currently no players are AFK</color>" },
            { M_NOTIFICATION, "<color=red>Start moving, or you will be punished for AFK!</color>" },
            { M_NO_PERMISSION, "<color=red>You have no permission to run this command!</color>" },
            { M_KICK_REASON, "AFK" }
        };

        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");

        void MessagePlayer(IPlayer player, string msg, bool prefix = true, params object[] args)
        {
            if (prefix)
            {
                player.Message(lang.GetMessage(M_CHAT_PREFIX, this, player.Id) + string.Format(lang.GetMessage(msg, this, player.Id), args));
            }
            else
            {
                player.Message(string.Format(lang.GetMessage(msg, this, player.Id), args));
            }
        }


        #endregion

        #region -API-


        bool IsPlayerAFK(ulong id)
        {
            return IsPlayerAFK(id.ToString());
        }

        bool IsPlayerAFK(string id)
        {
            var player = players.FindPlayerById(id);

            if (player == null)
            {
                throw new Exception($"Player with id {id} not found.");
            }

            return IsPlayerAFK(player);
        }

        bool IsPlayerAFK(IPlayer player)
        {
            if (player == null)
            {
                throw new ArgumentNullException("Player cannot be null.");
            }

            var list = AFKPlayers;

            if (list.Count() == 0)
            {
                return false;
            }

            return list.Contains(player);
        }

        long GetPlayerAFKTime(ulong id)
        {
            return (long)GetPlayerAFKTime(id.ToString());
        }

        float GetPlayerAFKTime(string id)
        {
            var player = players.FindPlayerById(id);

            if (player == null)
            {
                throw new Exception($"Player with id {id} not found.");
            }

            return GetPlayerAFKTime(player);
        }

        float GetPlayerAFKTime(IPlayer player)
        {
            if (player == null)
            {
                throw new ArgumentNullException("Player cannot be null");
            }

            if (!player.IsConnected)
            {
                return -1f;
            }

            var comp = trackedPlayers[player];

            if (!comp.IsAFK)
            {
                return -1f;
            }

            return comp.TimeAFK;
        }

        List<BasePlayer> GetAFKPlayers()
        {
            return AFKPlayers.Select(p => (BasePlayer)p.Object).ToList();
        }

        bool AFKAPI_Setup(string newSettings, bool needToSave = false)
        {
            if (!settings.GeneralSettings.AllowSetupThroughAPI)
                return false;
            Settings _newSettings = JsonConvert.DeserializeObject<Settings>(newSettings);
            if (_newSettings != null)
            {
                _newSettings.GeneralSettings.AllowSetupThroughAPI = true;
                settings = _newSettings;
                if (needToSave)
                    SaveConfig();
                CheckHookSubscriptions();
                return true;
            }
            else
                return false;
        }


        #endregion

        #region -Helpers-


        void CheckHookSubscriptions()
        {
            if (!settings.CompareSettings.CompareBuild)
            {
                Unsubscribe(nameof(CanBuild));
            }
            if (!settings.CompareSettings.CompareCommunication)
            {
                Unsubscribe(nameof(OnPlayerChat));
                Unsubscribe(nameof(OnPlayerVoice));
            }
            if (!settings.CompareSettings.CompareItemActions)
            {
                Unsubscribe(nameof(CanCraft));
                Unsubscribe(nameof(OnActiveItemChanged));
                Unsubscribe(nameof(OnItemAction));
                Unsubscribe(nameof(CanMoveItem));
            }
        }


        #endregion

        #region -Oxide hooks-


        void Init()
        {
            instance = this;

            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_KICK, this);

            AddUniversalCommand("isafk", nameof(CmdIsAFK));
            AddUniversalCommand("getafk", nameof(CmdGetAFK));
            AddUniversalCommand("kickafk", nameof(CmdKickAFK));

            trackedPlayers = new Dictionary<IPlayer, AFKPlayer>();
        }

        void OnServerInitialized()
        {
            CheckHookSubscriptions();

            foreach (var player in players.Connected)
            {
                OnUserConnected(player);
            }
        }

        void Unload()
        {
            foreach (var p in trackedPlayers.Keys)
            {
                OnUserDisconnected(p);
            }

            instance = null;
        }

        void OnUserConnected(IPlayer player)
        {
            var basePlayer = (BasePlayer)player.Object;
            var comp = basePlayer.gameObject.AddComponent<AFKPlayer>();
        }

        void OnUserDisconnected(IPlayer player)
        {
            UnityEngine.Object.Destroy(trackedPlayers[player]);
        }

        void CanBuild(Planner planner)
        {
            BasePlayer player = planner.GetOwnerPlayer();
            if (player != null)
                trackedPlayers[player.IPlayer].OnAction();
        }

        void CanCraft(ItemCrafter itemCrafter)
        {
            var player = itemCrafter.baseEntity;
            if (player != null)
                trackedPlayers[player.IPlayer].OnAction();
        }

        void OnActiveItemChanged(BasePlayer player)
        {
            if (player?.IPlayer == null)
            {
                return;
            }
            trackedPlayers[player.IPlayer].OnAction();
        }

        void OnPlayerChat(BasePlayer player)
        {
            if (player?.IPlayer == null)
            {
                return;
            }
            trackedPlayers[player.IPlayer].OnAction();
        }

        void OnPlayerVoice(BasePlayer player)
        {
            if (player?.IPlayer == null)
            {
                return;
            }
            trackedPlayers[player.IPlayer].OnAction();
        }

        void OnItemAction(Item item, string action, BasePlayer player)
        {
            if (player?.IPlayer == null)
            {
                return;
            }
            trackedPlayers[player.IPlayer].OnAction();
        }

        void CanMoveItem(Item item, PlayerInventory playerLoot, uint targetContainer, int targetSlot, int amount)
        {
            var player = playerLoot.baseEntity;
            if (player?.IPlayer == null)
            {
                return;
            }
            trackedPlayers[player.IPlayer].OnAction();
        }


        #endregion

        #region -Commands-


        void CmdIsAFK(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PERMISSION_USE))
            {
                MessagePlayer(player, M_NO_PERMISSION);
            }
            else if (args.Length == 0)
            {
                MessagePlayer(player, M_ERROR_NO_ARGS);
            }
            else
            {
                var tPlayer = BasePlayer.Find(args[0]);

                if (!tPlayer || !trackedPlayers.ContainsKey(tPlayer.IPlayer))
                {
                    MessagePlayer(player, M_PLAYER_AFK_STATUS, true, args[0], M_OFFLINE_STATUS);
                }
                else
                {
                    var comp = trackedPlayers[tPlayer.IPlayer];

                    if (comp.IsAFK)
                    {
                        MessagePlayer(player, M_PLAYER_AFK_STATUS, true, tPlayer.displayName, M_IS_AFK_STATUS);
                    }
                    else
                    {
                        MessagePlayer(player, M_PLAYER_AFK_STATUS, true, tPlayer.displayName, M_IS_NOT_AFK_STATUS);
                    }
                }
            }
        }

        void CmdGetAFK(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PERMISSION_USE))
            {
                MessagePlayer(player, M_NO_PERMISSION);
            }
            else
            {
                var list = AFKPlayers;

                if (list.Count() == 0)
                {
                    MessagePlayer(player, M_NO_AFK_PLAYERS_FOUND);
                }
                else
                {
                    MessagePlayer(player, M_AFK_PLAYER_LIST, false, string.Join("\n", list.Select(p => p.Name)));
                }
            }
        }

        void CmdKickAFK(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PERMISSION_KICK))
            {
                MessagePlayer(player, M_NO_PERMISSION);
            }
            else
            {
                foreach (var afkPlayer in AFKPlayers)
                {
                    afkPlayer.Kick(lang.GetMessage(M_KICK_REASON, this, afkPlayer.Id));
                }
            }
        }


        #endregion

        #region -Nested types-


        class AFKPlayer : MonoBehaviour
        {
            #region [Fields]

            public bool IsAFK
            {
                get
                {
                    return TimeSinceLastAction >= instance.settings.GeneralSettings.SecondsToAFKStatus;
                }
            }

            public float TimeAFK
            {
                get
                {
                    return Mathf.Max(0f, TimeSinceLastAction - instance.settings.GeneralSettings.SecondsToAFKStatus);
                }
            }

            float TimeSinceLastAction => Time.realtimeSinceStartup - lastActionTime;

            BasePlayer player;
            float lastActionTime;
            Vector3 lastPosition;
            Quaternion lastRotation;
            Dictionary<IPlayer, AFKPlayer> trackedPlayers;

            #endregion

            #region [Public methods]

            public void OnAction()
            {
                lastActionTime = Time.realtimeSinceStartup;
            }

            #endregion

            #region [Lifecycle hooks]

            void Start()
            {
                trackedPlayers = instance.trackedPlayers;

                player = GetComponent<BasePlayer>();
                trackedPlayers[player.IPlayer] = this;

                lastPosition = player.transform.position;
                lastRotation = player.eyes.rotation;

                InvokeRepeating(nameof(CheckPosition), instance.settings.GeneralSettings.StatusRefreshInterval, instance.settings.GeneralSettings.StatusRefreshInterval);
                InvokeRepeating(nameof(CheckNotification), instance.settings.GeneralSettings.StatusRefreshInterval, instance.settings.GeneralSettings.StatusRefreshInterval);
            }

            void OnDestroy()
            {
                CancelInvoke();
                trackedPlayers.Remove(player.IPlayer);
            }

            #endregion

            #region [Position/rotation checks]

            void CheckPosition()
            {
                if (player.transform.position == lastPosition)
                {
                    if (instance.settings.CompareSettings.CompareRotation && RotationChanged())
                        OnAction();
                }
                else
                {
                    lastPosition = player.transform.position;
                    OnAction();
                }
            }
            bool RotationChanged()
            {
                if (player.eyes.rotation != lastRotation)
                {
                    lastRotation = player.eyes.rotation;
                    return true;
                }
                return false;
            }

            #endregion

            #region [Notifications]

            void CheckNotification()
            {
                if (!instance.settings.NotificationSettings.NotifyPlayer)
                {
                    return;
                }

                var tsla = TimeSinceLastAction;

                if (instance.settings.GeneralSettings.SecondsToAFKStatus - TimeSinceLastAction > instance.settings.NotificationSettings.NotifyPlayerTime)
                {
                    return;
                }

                NotifyPlayer();

                if (instance.settings.NotificationSettings.NotifyPlayerSound)
                {
                    DoBeepEffect();
                }
            }

            void NotifyPlayer()
            {
                instance.MessagePlayer(player.IPlayer, M_NOTIFICATION);
            }

            void DoBeepEffect()
            {
                var beep = new Effect();
                beep.Init(Effect.Type.Generic, player.transform.position, Vector3.zero);
                beep.pooledString = BEEP_SOUND_PREFAB;
                EffectNetwork.Send(beep, player.Connection);
            }

            #endregion
        }


        #endregion

    }
}
