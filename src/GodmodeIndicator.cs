// Requires: ImageLibrary

using System.Collections.Generic;
using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;
using Oxide.Core.Plugins;
using UnityEngine;
using static Oxide.Game.Rust.Cui.CuiHelper;

namespace Oxide.Plugins
{
    [Info("Godmode Indicator", "2CHEVSKII", "2.0.6")]
    [Description("Displays an indicator on screen if a player is in godmode")]
    class GodmodeIndicator : RustPlugin
    {
        #region -Configuration and fields-

        static GodmodeIndicator Instance { get; set; }

        [PluginReference] Plugin ImageLibrary;

        [PluginReference] Plugin Godmode;

        List<BasePlayer> ActiveIndicator { get; set; }

        PluginSettings Settings { get; set; }

        class PluginSettings
        {
            [JsonProperty(PropertyName = "UI X position")]
            internal float UI_X { get; set; }
            [JsonProperty(PropertyName = "UI Y position")]
            internal float UI_Y { get; set; }
            [JsonProperty(PropertyName = "UI URL")]
            internal string UI_URL { get; set; }
            [JsonProperty(PropertyName = "UI Color")]
            internal string UI_Color { get; set; }
        }

        PluginSettings GetDefaultSettings() => new PluginSettings
        {
            UI_X = 0.01f,
            UI_Y = 0.86f,
            UI_Color = "1 1 1 1",
            UI_URL = "https://i.imgur.com/SF6lN2N.png"
        };

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            Settings = GetDefaultSettings();
            Puts("Creating new configuration file...");
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                Settings = Config.ReadObject<PluginSettings>();
                if (Settings == null)
                    throw new JsonException("Unable to load configuration file...");
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(Settings, true);

        #endregion

        #region -Hooks-

        void Init()
        {
            ActiveIndicator = new List<BasePlayer>();
            Instance = this;
        }

        void OnServerInitialized()
        {
            timer.Once(1f, () => BuildUI());
            timer.Once(3f, () =>
            {
                foreach (BasePlayer player in BasePlayer.activePlayerList)
                    player.gameObject.AddComponent<GodmodeComponent>();
            });
        }

        void OnPlayerConnected(BasePlayer player) => player.gameObject.AddComponent<GodmodeComponent>().IsWaiting = true;

        void OnPlayerSleepEnded(BasePlayer player)
        {
            var component = player.gameObject.GetComponent<GodmodeComponent>();
            if (component != null && component.IsWaiting) timer.Once(2f, () => component.IsWaiting = false);
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (ActiveIndicator.Contains(player)) ActiveIndicator.Remove(player);
            player.gameObject.GetComponent<GodmodeComponent>()?.DetachComponent();
        }

        void Unload() { foreach (var player in BasePlayer.activePlayerList) player.gameObject.GetComponent<GodmodeComponent>()?.DetachComponent(); }

        #endregion

        #region -UI-

        CuiElementContainer MainUI { get; set; }

        const string mainPanel = "godmodeindicator.mainui";

        void BuildUI()
        {
            string image = mainPanel + ".image";
            ImageLibrary?.Call("AddImage", Settings.UI_URL, image);
            MainUI = new CuiElementContainer();

            timer.Once(1f, () =>
            {
                MainUI.Add(new CuiElement
                {
                    Name = mainPanel,
                    Parent = "Hud",
                    Components =
                    {
                        new CuiRawImageComponent
                        {
                            Color = Settings.UI_Color,
                            Sprite = "assets/content/textures/generic/fulltransparent.tga",
                            Png = (string)ImageLibrary?.Call("GetImage", image),
                            FadeIn = 0.4f
                        },
                        new CuiRectTransformComponent
                        {
                            AnchorMin = $"{Settings.UI_X} {Settings.UI_Y}",
                            AnchorMax = $"{Settings.UI_X + 0.07f} {Settings.UI_Y + 0.1f}"
                        }
                    },
                    FadeOut = 0.4f
                });
                UIBuilt = true;
            });
        }

        #endregion

        #region -Component-

        bool UIBuilt { get; set; }

        class GodmodeComponent : MonoBehaviour
        {
            BasePlayer Player { get; set; }
            bool IsInGodMode { get; set; }
            internal bool IsWaiting { get; set; }
            float LastUpdate { get; set; }

            void Awake() => Player = GetComponent<BasePlayer>();

            void Update()
            {
                if (Time.realtimeSinceStartup - LastUpdate > 1f)
                {
                    if (Player == null || !Player.IsConnected) DetachComponent();
                    else
                    {
                        if (Player.IsGod()) IsInGodMode = true;
                        else
                        {
                            if (Instance.Godmode != null && Instance.Godmode.IsLoaded && Instance.Godmode.Call<bool>("IsGod", Player.UserIDString)) IsInGodMode = true;
                            else IsInGodMode = false;
                        }
                    }
                    if (IsInGodMode && !Instance.ActiveIndicator.Contains(Player) && Instance.UIBuilt && !IsWaiting)
                    {
                        Instance.ActiveIndicator.Add(Player);
                        AddUi(Player, Instance.MainUI);
                    }
                    else if (!IsInGodMode && Instance.ActiveIndicator.Contains(Player))
                    {
                        Instance.ActiveIndicator.Remove(Player);
                        DestroyUi(Player, mainPanel);
                    }
                    LastUpdate = Time.realtimeSinceStartup;
                }
            }

            internal void DetachComponent() => Destroy(this);

            void OnDestroy() => DestroyUi(Player, mainPanel);
        }

        #endregion
    }
}
