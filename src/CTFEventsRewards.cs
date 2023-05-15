using System;
using System.Collections.Generic;
using System.Linq;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
    [Info("CTF Events Rewards", "2CHEVSKII", "0.1.0")]
    [Description("Allows rewarding players for capturing flags")]
    class CTFEventsRewards : CovalencePlugin
    {
        PluginSettings settings;

        bool CTFEvents_OnFinished(BasePlayer winner) { }

        bool CTFEvents_OnFinished(List<BasePlayer> winners) { }

        bool TryFindRewardForPlayer(BasePlayer player, out PluginSettings.Reward reward)
        {
            string[] permissions = permission.GetUserPermissions(player.UserIDString);
            PluginSettings.Reward[] rewards = null;

            for (var i = 0; i < permissions.Length; i++)
            {
                string perm = permissions[i];

                if (perm.StartsWith(nameof(CTFEventsRewards), StringComparison.OrdinalIgnoreCase))
                {
                    rewards = GetAssociatedRewards(perm);
                }
            }

            if (rewards == null)
            {
                rewards = GetDefaultRewards();
            }

            switch (rewards.Length)
            {
                case 0:
                    reward = default(PluginSettings.Reward);
                    return false;

                case 1:
                    reward = rewards[0];
                    return true;

                default:

                    return true;
            }
        }

        PluginSettings.Reward[] GetAssociatedRewards(string perm)
        {
            string permSanitized = perm.Substring(17);

            if (!settings.Rewards.ContainsKey(permSanitized))
            {
                return Array.Empty<PluginSettings.Reward>();
            }

            return settings.Rewards[permSanitized];
        }

        PluginSettings.Reward[] GetDefaultRewards()
        {
            if (!settings.Rewards.ContainsKey(string.Empty))
            {
                return Array.Empty<PluginSettings.Reward>();
            }

            return settings.Rewards[string.Empty];
        }

        T GetRandom<T>(T[] array)
        {
            var max = array.Length;
            var min = 0;

            var num = Random.Range(min, max);

            var item = array[num];

            return item;
        }

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
                LogError("Failed to load configuration: {0}", e.Message);
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
                new PluginSettings
                {
                    Rewards = new Dictionary<string, Reward[]>
                    {
                        [""] = new[]
                        {
                            new Reward
                            {
                                SRPoints = 0,
                                EcoPoints = 0,
                                Commands = Array.Empty<string>(),
                                ItemRewards = new[]
                                {
                                    new ItemReward
                                    {
                                        Item = "rifle.ak",
                                        Amount = 1,
                                        SkinId = 0ul,
                                        CustomName = "[reward]${name}"
                                    }
                                }
                            }
                        }
                    }
                };

            public Dictionary<string, Reward[]> Rewards { get; set; }

            public struct Reward
            {
                public int SRPoints;
                public int EcoPoints;
                public string[] Commands; // ${username}, ${userid}
                public ItemReward[] ItemRewards;
            }

            public struct ItemReward
            {
                public object Item;
                public ulong SkinId;
                public int Amount;
                public string CustomName; // ${name}

                public bool TryGetItemDefinition(out ItemDefinition def)
                {
                    int itemId;
                    string shortname;

                    if (Item is int)
                    {
                        itemId = (int)Item;

                        def = ItemManager.FindItemDefinition(itemId);
                    }
                    else if (Item is string)
                    {
                        shortname = (string)Item;

                        if (int.TryParse(shortname, out itemId))
                        {
                            def = ItemManager.FindItemDefinition(itemId);
                        }
                        else
                        {
                            def = ItemManager.FindItemDefinition(shortname);
                        }
                    }
                    else
                        def = null;

                    return def != null;
                }
            }
        }

        #endregion
    }
}
