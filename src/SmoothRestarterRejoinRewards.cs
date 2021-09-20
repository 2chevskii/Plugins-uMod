// #define DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("SmoothRestarter Rejoin Rewards", "2CHEVSKII", "1.0.0")]
    [Description("Reward players if they re-join after restart")]
    public class SmoothRestarterRejoinRewards : CovalencePlugin
    {
        #region Fields

        const string PERMISSION_PREFIX = "smoothrestarterrejoinrewards.";

        const string M_PREFIX     = "Chat prefix",
                     M_REWARDS    = "Chance to claim rewards",
                     M_UNCLAIMED  = "Has unclaimed rewards",
                     M_NO_REWARDS = "No unclaimed rewards",
                     M_NO_SPACE   = "Not enough space",
                     M_CLAIMED    = "All rewards claimed";

        [PluginReference] Plugin                            SmoothRestarter;
        PluginSettings                                      settings;
        Dictionary<string, List<PluginSettings.RewardItem>> delayedItems;
        List<string>                                        rewardClaimers;
        Timer                                               notificationTimer;
        DateTime                                            currentRestartTime;
        bool                                                isRestarting;

        #endregion

        #region Oxide hooks

        void Init()
        {
            foreach (var perm in settings.Rewards.Keys)
            {
                if (perm == string.Empty)
                {
                    continue;
                }

                permission.RegisterPermission(ConstructPermission(perm), this);
            }

            AddCovalenceCommand("rjrewards", nameof(CommandHandler));
            rewardClaimers = new List<string>();
        }

        void OnServerInitialized()
        {
            bool srLoaded = SmoothRestarter != null && SmoothRestarter.IsLoaded;

            if (!srLoaded)
            {
                LogWarning(
                    "This plugin needs SmoothRestarter in order to work. " +
                    "Install it from https://umod.org/plugins/smooth-restarter and reload the plugin"
                );
            }

            delayedItems = LoadRewards();

            if (settings.ReconnectThreshold > 0)
            {
                timer.Once(
                    settings.ReconnectThreshold,
                    delegate { delayedItems.Clear(); }
                );
            }
        }

        void OnUserDisconnected(IPlayer player)
        {
            if (isRestarting)
            {
                AddRewardClaimer(player);
            }
        }

        void Unload()
        {
            SaveData();
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (!player || player.IsDead())
            {
                return;
            }

            NotifyUnclaimedRewards(player.IPlayer);
        }

        #region SmoothRestarter API

        void OnSmoothRestartInit()
        {
            currentRestartTime = SmoothRestarter.Call<DateTime>("GetCurrentRestartTime");
            isRestarting = true;

            Debug("Restart initiated, restartTime: {0}", currentRestartTime);

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
            isRestarting = false;

            Debug("Restart cancelled");

            timer.Destroy(ref notificationTimer);

            rewardClaimers.Clear();
        }

        #endregion

        #endregion

        #region Notifications

        void NotifyRewardsChance()
        {
            foreach (var player in players.Connected)
            {
                var rewards = GetRewardsForPlayer(player.Id);

                if (rewards.Count > 0)
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

            var rewards = delayedItems[player.Id];
            var fmt = string.Join(", ", rewards);

            Message(player, M_UNCLAIMED, fmt);
        }

        #endregion

        #region Command handler

        void CommandHandler(IPlayer player)
        {
            if (!HasUnclaimedRewards(player))
            {
                Message(player, M_NO_REWARDS);
            }
            else
            {
                var rewards = delayedItems[player.Id];
                var basePlayer = (BasePlayer)player.Object;

                while (rewards.Count > 0)
                {
                    var container = GetFreeContainer(basePlayer);

                    if (container == null)
                    {
                        break;
                    }

                    var item = rewards.First().CreateItem();
                    rewards.RemoveAt(0);

                    item.MoveToContainer(container, ignoreStackLimit: true);
                    item.MarkDirty();
                    container.MarkDirty();
                    basePlayer.SendConsoleCommand(
                        "note.inv",
                        item.info.itemid,
                        item.amount,
                        item.name,
                        2
                    );
                }

                if (rewards.Count > 0)
                {
                    Message(player, M_NO_SPACE, rewards.Count);
                }
                else
                {
                    delayedItems.Remove(player.Id);
                    Message(player, M_CLAIMED);
                }
            }
        }

        #endregion

        #region Util

        bool HasSlotsInInventory(BasePlayer player, bool beltContainer = false)
        {
            if (!beltContainer)
            {
                return player.inventory.containerMain.capacity - player.inventory.containerMain.itemList.Count > 0;
            }

            return player.inventory.containerBelt.capacity - player.inventory.containerBelt.itemList.Count > 0;
        }

        ItemContainer GetFreeContainer(BasePlayer player)
        {
            if (HasSlotsInInventory(player, false))
            {
                return player.inventory.containerMain;
            }

            if (HasSlotsInInventory(player, true))
            {
                return player.inventory.containerBelt;
            }

            return null;
        }

        bool HasUnclaimedRewards(IPlayer player)
        {
            return delayedItems.ContainsKey(player.Id);
        }

        void AddRewardClaimer(IPlayer player)
        {
            if (rewardClaimers.Contains(player.Id))
            {
                return;
            }

            rewardClaimers.Add(player.Id);
        }

        [Conditional("DEBUG")]
        void Debug(string format, params object[] args)
        {
            Log(string.Format("DEBUG: " + format, args));
        }

        #endregion

        #region Data

        void SaveData()
        {
            var dataFile = Interface.Oxide.DataFileSystem.GetFile(nameof(SmoothRestarterRejoinRewards));

            dataFile.WriteObject((IEnumerable<string>)rewardClaimers ?? Array.Empty<string>());
        }

        List<string> LoadData()
        {
            var list = new List<string>();
            DynamicConfigFile dataFile;

            if (Interface.Oxide.DataFileSystem.ExistsDatafile(nameof(SmoothRestarterRejoinRewards)))
            {
                dataFile = Interface.Oxide.DataFileSystem.GetFile(nameof(SmoothRestarterRejoinRewards));

                try
                {
                    var strings = dataFile.ReadObject<List<string>>();

                    if (strings == null)
                    {
                        throw new Exception("Data is null");
                    }

                    list.AddRange(strings.Where(s => !string.IsNullOrWhiteSpace(s)));
                }
                catch
                {
                    LogError("Failed to load plugin data, rewards for previous restart will not be available.");
                }
            }
            else
            {
                dataFile = Interface.Oxide.DataFileSystem.GetFile(nameof(SmoothRestarterRejoinRewards));
            }

            dataFile.Clear();
            dataFile.WriteObject(Array.Empty<string>());

            return list;
        }

        #endregion

        #region Rewards

        Dictionary<string, List<PluginSettings.RewardItem>> LoadRewards()
        {
            var dictionary = new Dictionary<string, List<PluginSettings.RewardItem>>();

            var userids = LoadData();

            foreach (var userid in userids)
            {
                var rewards = GetRewardsForPlayer(userid);

                if (rewards.Count > 0)
                {
                    dictionary.Add(userid, rewards);
                }
            }

            return dictionary;
        }

        List<PluginSettings.RewardItem> GetRewardsForPlayer(string userid)
        {
            var list = new List<PluginSettings.RewardItem>();

            list.AddRange(GetDefaultRewards());

            var perms = GetPermissionGroups(userid);

            foreach (var perm in perms)
            {
                list.AddRange(GetRewardsForPermission(perm));
            }

            return list;
        }

        IEnumerable<PluginSettings.RewardItem> GetDefaultRewards()
        {
            return GetRewards(string.Empty);
        }

        IEnumerable<PluginSettings.RewardItem> GetRewardsForPermission(string perm)
        {
            var p = DeconstructPermission(perm);

            return GetRewards(p);
        }

        PluginSettings.RewardItem[] GetRewards(string key)
        {
            PluginSettings.RewardItem[] rewards;
            if (!settings.Rewards.TryGetValue(key, out rewards))
            {
                rewards = Array.Empty<PluginSettings.RewardItem>();
            }

            return rewards;
        }

        #endregion

        #region Permissions

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
            return perm.Substring(PERMISSION_PREFIX.Length);
        }

        #endregion

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string>
                {
                    { M_PREFIX, "<color=#ffa04f>Rejoin Rewards</color>:" },
                    { M_REWARDS, "If you rejoin after the server restart, you will receive rewards:\n{0}" },
                    { M_UNCLAIMED, "You currently have unclaimed rejoin rewards, use /rjrewards to claim them" },
                    { M_NO_REWARDS, "You do not have any unclaimed rewards" },
                    { M_NO_SPACE, "You do not have enough space in your inventory, some rewards ({0}) were not claimed. " +
                                  "Free up some space and use /rjrewards to claim the remaining items" },
                    { M_CLAIMED, "All rewards were claimed" }
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

                if (settings == null || settings.Rewards == null)
                {
                    throw new Exception("Configuration or rewards dictionary is null");
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
                    ReconnectThreshold = 600f,
                    Rewards = new Dictionary<string, RewardItem[]> {
                        [string.Empty] = new[]
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
