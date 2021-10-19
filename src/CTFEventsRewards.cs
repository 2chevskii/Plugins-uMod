
using System;

namespace Oxide.Plugins
{
    [Info("CTF Events Rewards", "2CHEVSKII", "0.1.0")]
    [Description("Allows rewarding players for capturing flags")]
    class CTFEventsRewards : CovalencePlugin
    {
        PluginSettings settings;

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

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings { };

            public struct Reward
            {
                public int SRPoints { get; set; }
                public int EcoPoints { get; set; }

            }
        }
    }
}
