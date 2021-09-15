using System;
using System.Collections.Generic;
using System.Linq;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SmoothRestarterRejoinRewards", "2CHEVSKII", "1.0.0")]
    [Description("Reward players if they re-join after restart")]
    public class SmoothRestarterRejoinRewards : CovalencePlugin
    {
        const string PERMISSION_PREFIX = "smoothrestarterrejoinrewards.";

        const string M_PREFIX     = "Chat prefix",
                     M_REWARDS    = "Chance to claim rewards",
                     M_UNCLAIMED  = "Has unclaimed rewards",
                     M_NO_REWARDS = "No unclaimed rewards";

        [PluginReference] Plugin                        SmoothRestarter;
        PluginSettings                                  settings;
        Dictionary<string, PluginSettings.RewardItem[]> delayedItems;
        bool                                            srLoaded;
        HashSet<string>                                 rewardClaimers;
        Timer                                           notificationTimer;

        bool IsRestarting => SmoothRestarter.Call<bool>("IsSmoothRestarting");
        DateTime? CurrentRestartTime => SmoothRestarter.Call<DateTime?>("GetCurrentRestartTime");

        bool ShouldSavePlayer
        {
            get
            {
                if (settings.DisconnectThreshold <= 0)
                {
                    return true;
                }

                var restartTime = CurrentRestartTime.Value;

                var timeRemaining = (restartTime - DateTime.Now).TotalSeconds;
                return timeRemaining <= settings.DisconnectThreshold;
            }
        }

        #region Oxide hooks

        void Init()
        {
            foreach (var perm in settings.Rewards.Keys)
            {
                permission.RegisterPermission(ConstructPermission(perm), this);
            }
        }

        void OnServerInitialized()
        {
            srLoaded = SmoothRestarter != null && SmoothRestarter.IsLoaded;

            if (!srLoaded)
            {
                LogWarning("This plugin needs SmoothRestarter in order to work. " +
                           "Install it from https://umod.org/plugins/smooth-restarter and reload the plugin");
            }

            delayedItems = LoadRewards();
        }

        void OnUserDisconnected(IPlayer player)
        {
            if (!srLoaded || !IsRestarting || !ShouldSavePlayer)
            {
                return;
            }

            SaveRewardClaimer(player);
        }

        void Unload()
        {
            if (rewardClaimers != null)
                Interface.Oxide.DataFileSystem.GetFile(nameof(SmoothRestarterRejoinRewards)).WriteObject(rewardClaimers);
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            NotifyUnclaimedRewards(player.IPlayer);
        }

        #region SmoothRestarter API

        void OnSmoothRestartInit()
        {
            NotifyRewardsChance();
            if (settings.NotificationFrequency > 0)
            {
                notificationTimer = timer.Every(settings.NotificationFrequency, NotifyRewardsChance);
            }
        }

        void OnSmoothRestartTick()
        {
            if (settings.NotificationFrequency <= 0)
            {
                NotifyRewardsChance();
            }
        }

        void OnSmoothRestartCancelled()
        {
            if (notificationTimer != null && !notificationTimer.Destroyed)
            {
                timer.Destroy(ref notificationTimer);
            }
        }

        #endregion

        #endregion

        void NotifyRewardsChance()
        {
            if (!ShouldSavePlayer)
            {
                return;
            }

            foreach (var player in players.Connected)
            {
                var rewards = GetRewardsFor(player.Id);

                if (rewards.Length > 0)
                {
                    var fmt = string.Join(", ", rewards);

                    Message(player, M_REWARDS, fmt);
                }
            }
        }

        void NotifyUnclaimedRewards(IPlayer player)
        {
            if (!HasUnclaimedRewards(player))
            {
                return;
            }

            var rewards = GetRewardsFor(player.Id);
            var fmt = string.Join(", ", rewards);

            Message(player, M_UNCLAIMED, fmt);
        }

        bool HasUnclaimedRewards(IPlayer player)
        {
            return delayedItems.ContainsKey(player.Id) && delayedItems[player.Id].Length > 0;
        }

        void SaveRewardClaimer(IPlayer player)
        {
            if (rewardClaimers == null)
            {
                rewardClaimers = new HashSet<string>();
            }

            rewardClaimers.Add(player.Id);
        }

        Dictionary<string, PluginSettings.RewardItem[]> LoadRewards()
        {
            if (!Interface.Oxide.DataFileSystem.ExistsDatafile(nameof(SmoothRestarterRejoinRewards)))
            {
                return new Dictionary<string, PluginSettings.RewardItem[]>();
            }

            var dataFile = Interface.Oxide.DataFileSystem.GetFile(nameof(SmoothRestarterRejoinRewards));
            var dictionary = new Dictionary<string, PluginSettings.RewardItem[]>();

            try
            {
                var userids = dataFile.ReadObject<HashSet<string>>();

                foreach (var userid in userids)
                {
                    var rewards = GetRewardsFor(userid);
                    dictionary.Add(userid, rewards);
                }
            }
            catch (Exception e)
            {
                LogError("Data file corrupt! Could not load rewards.\n" + e.Message);
            }

            dataFile.WriteObject(Array.Empty<string>());

            return dictionary;
        }

        PluginSettings.RewardItem[] GetRewardsFor(string userid)
        {
            var list = Pool.GetList<PluginSettings.RewardItem>();

            list.AddRange(GetDefaultRewards());

            var perms = GetPermissionGroups(userid);

            foreach (var perm in perms)
            {
                list.AddRange(GetRewardsForPermission(perm));
            }

            var array = list.ToArray();
            Pool.FreeList(ref list);
            return array;
        }

        PluginSettings.RewardItem[] GetDefaultRewards()
        {
            if (settings.Rewards.ContainsKey(""))
            {
                return settings.Rewards[""];
            }

            return Array.Empty<PluginSettings.RewardItem>();
        }

        PluginSettings.RewardItem[] GetRewardsForPermission(string perm)
        {
            var p = DeconstructPermission(perm);

            if (settings.Rewards.ContainsKey(p))
            {
                return settings.Rewards[p];
            }

            return Array.Empty<PluginSettings.RewardItem>();
        }

        IEnumerable<string> GetPermissionGroups(string userid)
        {
            return permission.GetUserPermissions(userid).Where(p => p.StartsWith(PERMISSION_PREFIX));
        }

        string ConstructPermission(string perm)
        {
            return PERMISSION_PREFIX + perm;
        }

        string DeconstructPermission(string perm)
        {
            return perm.Split('.')[1];
        }

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    { M_PREFIX, "RejoinRewards:" },
                    { M_REWARDS, "If you rejoin after the server restart, you will receive rewards:\n{0}" },
                    { M_UNCLAIMED, "You currently have unclaimed rejoin rewards, use /rjrewards to claim them" },
                    { M_NO_REWARDS, "You do not have any unclaimed rewards" }
                },
                this
            );
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            var format = lang.GetMessage(langKey, this, player.Id);

            player.Message(format, lang.GetMessage(M_PREFIX, this, player.Id), args);
        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            settings = PluginSettings.Default;
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
                    throw new Exception("Configuration is null");
                }
            }
            catch (Exception e)
            {
                LogError("Configuration load fail: {0}", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Nested types

        class PluginSettings
        {
            public static PluginSettings Default =>
                new PluginSettings {
                    NotificationFrequency = 30f,
                    DisconnectThreshold = 0f,
                    ReconnectThreshold = 600f,
                    Rewards = new Dictionary<string, RewardItem[]> {
                        [""] = new[]
                        {
                            new RewardItem
                            {
                                shortname = "rifle.ak",
                                quantity = 1,
                                durability = 0.3f,
                                skin = 0ul
                            },
                            new RewardItem
                            {
                                shortname = "ammo.rifle",
                                quantity = 60,
                                durability = 1f,
                                skin = 0ul
                            }
                        }
                    }
                };

            [JsonProperty("Notification frequency")]
            public float NotificationFrequency { get; set; }
            [JsonProperty("Disconnect threshold")]
            public float DisconnectThreshold { get; set; }
            [JsonProperty("Reconnect threshold")]
            public float ReconnectThreshold { get; set; }
            [JsonProperty("Rewards")]
            public Dictionary<string, RewardItem[]> Rewards { get; set; }

            public struct RewardItem
            {
                public string shortname;
                public int    quantity;
                public float  durability;
                public ulong  skin;

                public override string ToString()
                {
                    return shortname + ": " + quantity;
                }

                public Item CreateItem()
                {
                    if (quantity < 1)
                    {
                        throw new InvalidOperationException("Cannot create item with quantity less than 1");
                    }

                    var def = ItemManager.FindItemDefinition(shortname);

                    if (def == null)
                    {
                        throw new Exception($"Could not find ItemDefinition for shortname {shortname}");
                    }

                    var item = ItemManager.Create(def, quantity, skin);

                    if (durability > 0f)
                    {
                        item.conditionNormalized = Mathf.Clamp01(durability);
                        item.MarkDirty();
                    }

                    return item;
                }
            }
        }

        #endregion
    }
}
