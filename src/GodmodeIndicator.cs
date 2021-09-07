// Requires: ImageLibrary

// #define UNITY_ASSERTIONS

using System;
using System.Collections.Generic;
using System.ComponentModel;

using Oxide.Game.Rust.Cui;
using Newtonsoft.Json;

using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

using static Oxide.Game.Rust.Cui.CuiHelper;

namespace Oxide.Plugins
{
    [Info("Godmode Indicator", "2CHEVSKII", "2.1.2")]
    [Description("Displays an indicator on screen if a player is in godmode")]
    class GodmodeIndicator : CovalencePlugin
    {
        #region Configuration and fields

        const string UI_MAIN_PANEL_NAME = "godmodeindicator.ui::main_panel";

        static            GodmodeIndicator Instance;
        [PluginReference] Plugin           ImageLibrary;
        [PluginReference] Plugin           Godmode;
        string                             uiCached;

        Dictionary<string, GodmodeUi> idToComponent;

        PluginSettings settings;

        class PluginSettings
        {
            [JsonProperty("UI X position")]
            public float UiX { get; set; }
            [JsonProperty("UI Y position")]
            public float UiY { get; set; }
            [JsonProperty("UI URL")]
            public string UiUrl { get; set; }
            [JsonProperty("UI Color")]
            public string UiColor { get; set; }
            [DefaultValue(1.0f)]
            [JsonProperty("UI Scale", DefaultValueHandling = DefaultValueHandling.Populate)]
            public float UiScale { get; set; }
        }

        PluginSettings GetDefaultSettings() => new PluginSettings {
            UiX = 0.05f,
            UiY = 0.85f,
            UiColor = "1 1 1 1",
            UiUrl = "https://i.imgur.com/SF6lN2N.png",
            UiScale = 1.0f
        };

        protected override void LoadDefaultConfig()
        {
            Config.Clear();
            settings = GetDefaultSettings();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                settings = Config.ReadObject<PluginSettings>();
                if (settings == null)
                    throw new JsonException("Unable to load configuration file...");
                SaveConfig();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(settings, true);

        #endregion

        #region Oxide hooks

        void Init()
        {
            Instance = this;
            idToComponent = new Dictionary<string, GodmodeUi>();
        }

        void OnServerInitialized()
        {
            BuildUi();
            foreach (var player in players.Connected)
            {
                OnUserConnected(player);
            }
        }

        void OnUserConnected(IPlayer user)
        {
            var player = (BasePlayer)user.Object;
            player.gameObject.AddComponent<GodmodeUi>();
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            Debug.Assert(idToComponent.ContainsKey(player.UserIDString));

            if (idToComponent.ContainsKey(player.UserIDString)) // Ok we check it here now to get rid of errors, but I STILL DONT HAVE ANY FUCKING CLUE why they are possible in the first place
            {
                idToComponent[player.UserIDString].OnSleepEnded();
            }
        }

        void OnUserDisconnected(IPlayer user)
        {
            var component = idToComponent[user.Id];
            UnityEngine.Object.Destroy(component);
        }

        void Unload()
        {
            foreach (var player in players.Connected)
            {
                OnUserDisconnected(player);
            }

            uiCached = null;
            Instance = null;
        }

        void OnGodmodeToggled(string playerId, bool enabled)
        {
            Debug.Assert(idToComponent.ContainsKey(playerId));

            idToComponent[playerId].GodmodePluginStatus = enabled;
        }

        #endregion

        void BuildUi()
        {
            string icon = UI_MAIN_PANEL_NAME + "::icon";
            ulong id = (ulong)icon.GetHashCode();
            ImageLibrary.Call(
                "AddImage",
                settings.UiUrl,
                icon,
                id,
                new Action(
                    () => {
                        var container = new CuiElementContainer();

                        container.Add(
                            new CuiElement {
                                Name = UI_MAIN_PANEL_NAME,
                                Parent = "Hud",
                                Components =
                                {
                                    new CuiRawImageComponent
                                    {
                                        Color = settings.UiColor,
                                        Sprite = "assets/content/textures/generic/fulltransparent.tga",
                                        Png = (string)ImageLibrary?.Call("GetImage", icon, id),
                                        FadeIn = 0.4f
                                    },
                                    new CuiRectTransformComponent
                                    {
                                        AnchorMin = $"{settings.UiX - .04f * settings.UiScale} {settings.UiY - .056f * settings.UiScale}",
                                        AnchorMax =
                                            $"{settings.UiX + .04f * settings.UiScale} {settings.UiY + .056f * settings.UiScale}"
                                    }
                                },
                                FadeOut = 0.4f
                            }
                        );

                        uiCached = ToJson(container);

                        foreach (var component in idToComponent.Values)
                        {
                            component.OnUiBuilt();
                        }
                    }
                )
            );
        }

        #region GodmodeUi

        class GodmodeUi : MonoBehaviour
        {
            BasePlayer player;
            bool       uiVisible;
            bool       isInPluginGod;

            public bool GodmodePluginStatus
            {
                set
                {
                    isInPluginGod = value;
                }
            }

            bool IsGod => isInPluginGod || player.IsGod();

            public void OnSleepEnded()
            {
                if (!IsInvoking(nameof(Tick)))
                    InvokeRepeating(nameof(Tick), 1f, 1f);
            }

            public void OnUiBuilt()
            {
                if (!IsInvoking(nameof(Tick)) && !player.IsSleeping())
                    InvokeRepeating(nameof(Tick), 1f, 1f);
            }

            #region MonoBehaviour lifecycle

            void Awake()
            {
                player = GetComponent<BasePlayer>();

                Instance.idToComponent[player.UserIDString] = this;
            }

            void Start()
            {
                isInPluginGod = Instance.Godmode && Instance.Godmode.Call<bool>("IsGod", player.UserIDString);
                if (!player.IsSleeping() && Instance.uiCached != null)
                    InvokeRepeating(nameof(Tick), 1f, 1f);
            }

            void OnDestroy()
            {
                if (uiVisible && player.IsConnected)
                {
                    SetVisible(false);
                }

                Instance?.idToComponent?.Remove(player.UserIDString);
            }

            #endregion

            void SetVisible(bool bVisible)
            {
                Debug.Assert(bVisible != uiVisible, "Double setting visible to " + bVisible);
                Debug.Assert(player && player.IsConnected, "Player is null or disconnected");

                if (bVisible)
                {
                    AddUi(player, Instance.uiCached);
                }
                else
                {
                    DestroyUi(player, UI_MAIN_PANEL_NAME);
                }

                uiVisible = bVisible;
            }

            void Tick()
            {
                if (!IsGod && uiVisible)
                {
                    SetVisible(false);
                }
                else if (IsGod && !uiVisible)
                {
                    SetVisible(true);
                }
            }
        }

        #endregion
    }
}
