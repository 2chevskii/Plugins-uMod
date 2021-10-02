// #define DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Smooth Restarter Rejoin Rewards", "2CHEVSKII", "1.1.0")]
    [Description("Reward players if they re-join after restart")]
    public class SmoothRestarterRejoinRewards : CovalencePlugin
    {
        #region Fields

        const string PERMISSION_PREFIX = "smoothrestarterrejoinrewards.";

        const string M_PREFIX             = "Chat prefix",
                     M_REWARDS            = "Chance to claim rewards",
                     M_UNCLAIMED          = "Has unclaimed rewards",
                     M_NO_REWARDS         = "No unclaimed rewards",
                     M_NO_SPACE           = "Not enough space",
                     M_CLAIMED            = "All rewards claimed",
                     M_RECEIVED_ITEMS     = "Received items",
                     M_RECEIVED_ECONOMICS = "Received Economics points",
                     M_RECEIVED_SR_POINTS = "Received ServerRewards points";

        [PluginReference] Plugin                            SmoothRestarter;
        [PluginReference] Plugin                            ServerRewards;
        [PluginReference] Plugin                            Economics;
        PluginSettings                                      settings;
        Dictionary<string, List<PluginSettings.RewardItem>> delayedItems;
        Dictionary<string, PluginSettings.PointReward>      delayedPoints;
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
            delayedPoints = LoadPoints();

            if (settings.ReconnectThreshold > 0)
            {
                timer.Once(
                    settings.ReconnectThreshold,
                    delegate {
                        delayedItems.Clear();
                        delayedPoints.Clear();
                    }
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
            if (!player || player.IsDead() || !player.userID.IsSteamId())
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

            //var rewards = delayedItems[player.Id];
            //var fmt = string.Join(", ", rewards);


            Message(player, M_UNCLAIMED);
        }

        void NotifyReceivedRewards(IPlayer player, List<PluginSettings.RewardItem> items, PluginSettings.PointReward points)
        {
            if (items.Any())
            {
                Message(player, M_RECEIVED_ITEMS, string.Join(", ", items.Select(item => item.ToString())));
            }

            if (points.serverRewardsPoints > 0)
            {
                Message(player, M_RECEIVED_SR_POINTS, points.serverRewardsPoints);
            }

            if (points.economicsPoints > 0)
            {
                Message(player, M_RECEIVED_ECONOMICS, points.economicsPoints);
            }
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
                List<PluginSettings.RewardItem> itemRewards = Pool.GetList<PluginSettings.RewardItem>();
                PluginSettings.PointReward pointReward = default(PluginSettings.PointReward);

                var basePlayer = (BasePlayer)player.Object;

                if (delayedItems.ContainsKey(player.Id))
                {
                    var rewards = delayedItems[player.Id];

                    while (rewards.Count > 0)
                    {
                        var container = GetFreeContainer(basePlayer);

                        if (container == null)
                        {
                            break;
                        }

                        var rwd = rewards[0];

                        itemRewards.Add(rwd);

                        var item = rwd.CreateItem();
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
                        NextTick(() => Message(player, M_CLAIMED)); // make all claimed message appear after notification about received rewards
                    }
                }

                if (delayedPoints.ContainsKey(player.Id))
                {
                    var reward = delayedPoints[player.Id];

                    AddEconomicsPoints(player, reward.economicsPoints);
                    AddServerRewardsPoints(basePlayer, reward.serverRewardsPoints);

                    delayedPoints.Remove(player.Id);

                    pointReward = reward;
                }

                NotifyReceivedRewards(player, itemRewards, pointReward);
                Pool.FreeList(ref itemRewards);
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
            return delayedItems.ContainsKey(player.Id) || delayedPoints.ContainsKey(player.Id);
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

        Dictionary<string, PluginSettings.PointReward> LoadPoints()
        {
            var dictionary = new Dictionary<string, PluginSettings.PointReward>();

            var userids = LoadData();

            foreach (var userid in userids)
            {
                var points = GetPointsForPlayer(userid);

                if (points.serverRewardsPoints > 0 || points.economicsPoints > 0)
                {
                    dictionary.Add(userid, points);
                }
            }

            return dictionary;
        }

        PluginSettings.PointReward GetPointsForPlayer(string userid)
        {
            var reward = new PluginSettings.PointReward();

            var defaultReward = GetDefaultPoints();

            reward.serverRewardsPoints += defaultReward.serverRewardsPoints;
            reward.economicsPoints += defaultReward.economicsPoints;

            var perms = GetPermissionGroups(userid);

            foreach (var perm in perms)
            {
                var permReward = GetPointsForPermission(perm);

                reward.serverRewardsPoints += permReward.serverRewardsPoints;
                reward.economicsPoints += permReward.economicsPoints;
            }

            return reward;
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

        PluginSettings.PointReward GetDefaultPoints()
        {
            return GetPoints(string.Empty);
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

        PluginSettings.PointReward GetPointsForPermission(string perm)
        {
            var p = DeconstructPermission(perm);

            return GetPoints(p);
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

        PluginSettings.PointReward GetPoints(string key)
        {
            PluginSettings.PointReward item;
            if (!settings.RewardPoints.TryGetValue(key, out item))
            {
                item = default(PluginSettings.PointReward);
            }

            return item;
        }

        #endregion

        #region ServerRewards integration

        void AddServerRewardsPoints(BasePlayer player, int points)
        {
            if (ServerRewards != null && ServerRewards.IsLoaded)
            {
                var success = ServerRewards.Call("AddPoints", player.userID, points);
                if (success == null)
                {
                    LogWarning("Could not add ServerRewards points for player {0} [{1}]", player.displayName, player.UserIDString);
                }
            }
        }

        #endregion

        #region Economics integration

        void AddEconomicsPoints(IPlayer player, int points)
        {
            if (Economics != null && Economics.IsLoaded)
            {
                bool success = Economics.Call<bool>("Deposit", player.Id, points);

                if (!success)
                {
                    LogWarning("Could not add Economics points for player {0} [{1}]", player.Name, player.Id);
                }
            }
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
                    {
                        M_NO_SPACE,
                        "You do not have enough space in your inventory, some rewards ({0}) were not claimed. " +
                        "Free up some space and use /rjrewards to claim the remaining items"
                    },
                    { M_CLAIMED, "All rewards were claimed" },
                    { M_RECEIVED_ITEMS, "You were rewarded with items: {0}" },
                    { M_RECEIVED_ECONOMICS, "Your Economics balance was funded with {0} points" },
                    { M_RECEIVED_SR_POINTS, "You've received {0} points on your ServerRewards balance" }
                },
                this
            );
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            var format = GetMessage(player, langKey);

            player.Message(format, GetMessage(player, M_PREFIX), args);
        }

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
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

                if (PluginSettings.NeedsUpdate(settings))
                {
                    SaveConfig();
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
                    },
                    RewardPoints = new Dictionary<string, PointReward> {
                        [string.Empty] = new PointReward {
                            serverRewardsPoints = 0,
                            economicsPoints = 0
                        }
                    }
                };

            [JsonProperty("Notification frequency")]
            public float NotificationFrequency { get; set; }
            [JsonProperty("Reconnect threshold")]
            public float ReconnectThreshold { get; set; }
            [JsonProperty("Rewards")]
            public Dictionary<string, RewardItem[]> Rewards { get; set; }
            [JsonProperty("ServerRewards and economics points")]
            public Dictionary<string, PointReward> RewardPoints { get; set; }

            public static bool NeedsUpdate(PluginSettings settings)
            {
                if (settings.RewardPoints == null)
                {
                    settings.RewardPoints = new Dictionary<string, PointReward> {
                        [string.Empty] = new PointReward {
                            serverRewardsPoints = 0,
                            economicsPoints = 0
                        }
                    };

                    return true;
                }

                return false;
            }

            public struct PointReward
            {
                public int serverRewardsPoints;
                public int economicsPoints;

                public bool IsNotEmpty()
                {
                    return serverRewardsPoints > 0 || economicsPoints > 0;
                }
            }

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
