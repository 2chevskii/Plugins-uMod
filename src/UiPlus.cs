// Requires: ImageLibrary

//#define UNITY_ASSERTIONS
//#define DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;

using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("UiPlus", "2CHEVSKII", "2.1.0")]
    [Description("Adds various custom elements to the user interface")]
    class UiPlus : CovalencePlugin
    {
        const string ICON_CLOCK            = "https://i.imgur.com/K53fjzg.png",
                     ICON_ACTIVE_PLAYERS   = "https://i.imgur.com/r7L4jW2.png",
                     ICON_SLEEPING_PLAYERS = "https://i.imgur.com/D0JFYfe.png",
                     ICON_SERVER_REWARDS   = "https://i.imgur.com/tpSErWL.png",
                     ICON_ECONOMICS        = "https://i.imgur.com/KH9NcrC.png";

        const string PERMISSION_SEE = "uiplus.see";

        static UiPlus Instance;
        static ulong  ImageId = unchecked((ulong)nameof(UiPlus).GetHashCode() + 13);

        [PluginReference] Plugin ImageLibrary;
        [PluginReference] Plugin ServerRewards;
        [PluginReference] Plugin Economics;

        PluginSettings settings;
        bool           iconsReady;

        [Conditional("DEBUG")]
        static void DebugLog(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[UiPlus] " + format, args);
        }

        void Init()
        {
            Instance = this;

            permission.RegisterPermission(PERMISSION_SEE, this);
        }

        void OnServerInitialized()
        {
            CacheIcons();
        }

        void OnUserConnected(IPlayer player)
        {
            UiPlusComponent.OnPlayerConnected((BasePlayer)player.Object);
        }

        void OnUserDisconnected(IPlayer player)
        {
            UiPlusComponent.OnPlayerDisconnected((BasePlayer)player.Object);
        }

        void Unload()
        {
            UiPlusComponent.Dispose();
            Instance = null;
        }

        void CacheIcons()
        {
            ImageLibrary.Call(
                "ImportImageList",
                nameof(UiPlus),
                new Dictionary<string, string> {
                    { "Clock", settings.Clock.IconUrl },
                    { "ActivePlayers", settings.ActivePlayers.IconUrl },
                    { "SleepingPlayers", settings.SleepingPlayers.IconUrl },
                    { "ServerRewards", settings.ServerRewards.IconUrl },
                    { "Economics", settings.Economics.IconUrl }
                },
                ImageId,
                false,
                new Action(
                    () =>
                    {
                        iconsReady = true;
                        UiPlusComponent.Initialize(settings);
                    }
                )
            );
        }

        #region Configuration load

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
                bool hasChanged = false;
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null)
                {
                    LogWarning("Plugin settings appear to be null: resetting to default...");
                    settings = PluginSettings.Default;
                    hasChanged = true;
                }

                if (settings.Clock == null)
                {
                    LogWarning("Clock settings appear to be null: resetting to default...");
                    settings.Clock = PluginSettings.Default.Clock;
                    hasChanged = true;
                }

                if (settings.ActivePlayers == null)
                {
                    LogWarning("ActivePlayers settings appear to be null: resetting to default...");
                    settings.ActivePlayers = PluginSettings.Default.ActivePlayers;
                    hasChanged = true;
                }

                if (settings.SleepingPlayers == null)
                {
                    LogWarning("SleepingPlayers settings appear to be null: resetting to default...");
                    settings.SleepingPlayers = PluginSettings.Default.SleepingPlayers;
                    hasChanged = true;
                }

                if (settings.ServerRewards == null)
                {
                    LogWarning("ServerRewards settings appear to be null: resetting to default...");
                    settings.ServerRewards = PluginSettings.Default.ServerRewards;
                    hasChanged = true;
                }

                if (settings.Economics == null)
                {
                    LogWarning("Economics settings appear to be null: resetting to default...");
                    settings.Economics = PluginSettings.Default.Economics;
                    hasChanged = true;
                }

                if (settings.Clock.IconUrl == null)
                {
                    LogWarning("Clock icon appears to be null: resetting to default...");
                    settings.Clock.IconUrl = ICON_CLOCK;
                    hasChanged = true;
                }

                if (settings.ActivePlayers.IconUrl == null)
                {
                    LogWarning("Active players icon appears to be null: resetting to default...");
                    settings.ActivePlayers.IconUrl = ICON_ACTIVE_PLAYERS;
                    hasChanged = true;
                }

                if (settings.SleepingPlayers.IconUrl == null)
                {
                    LogWarning("Sleeping players icon appears to be null: resetting to default...");
                    settings.SleepingPlayers.IconUrl = ICON_SLEEPING_PLAYERS;
                    hasChanged = true;
                }

                if (settings.ServerRewards.IconUrl == null)
                {
                    LogWarning("ServerRewards icon appears to be null: resetting to default...");
                    settings.ServerRewards.IconUrl = ICON_SERVER_REWARDS;
                    hasChanged = true;
                }

                if (settings.Economics.IconUrl == null)
                {
                    LogWarning("Economics icon appears to be null: resetting to default...");
                    settings.Economics.IconUrl = ICON_ECONOMICS;
                    hasChanged = true;
                }

                if (hasChanged)
                {
                    LogWarning("Configuration was updated, saving...");
                    SaveConfig();
                }
            }
            catch (Exception e)
            {
                LogError("Configuration failed to load: {0}", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        class UiPlusComponent : FacepunchBehaviour
        {
            const string PANEL_MATERIAL = "assets/content/ui/uibackgroundblur.mat";

            const string NAME_CLOCK_PANEL = "uiplus.ui::clock-panel",
                         NAME_CLOCK_TEXT  = "uiplus.ui::clock-text",
                         NAME_CLOCK_ICON  = "uiplus.ui::clock-icon",
                         NAME_AP_PANEL    = "uiplus.ui::activeplayers-panel",
                         NAME_AP_TEXT     = "uiplus.ui::activeplayers-text",
                         NAME_AP_ICON     = "uiplus.ui::activeplayers-icon",
                         NAME_SP_PANEL    = "uiplus.ui::sleepingplayers-panel",
                         NAME_SP_TEXT     = "uiplus.ui::sleepingplayers-text",
                         NAME_SP_ICON     = "uiplus.ui::sleepingplayers-icon",
                         NAME_SR_PANEL    = "uiplus.ui::serverrewards-panel",
                         NAME_SR_TEXT     = "uiplus.ui::serverrewards-text",
                         NAME_SR_ICON     = "uiplus.ui::serverrewards-icon",
                         NAME_ECO_PANEL   = "uiplus.ui::economics-panel",
                         NAME_ECO_TEXT    = "uiplus.ui::economics-text",
                         NAME_ECO_ICON    = "uiplus.ui::economics-icon";

            const string COLOR_PANEL = "0.6 0.6 0.6 0.1",
                         COLOR_ICON  = "1 1 1 0.9",
                         COLOR_TEXT  = "0.9 0.9 0.9 0.75";

            const string TEXT_PLACEHOLDER = "__text__";

            static HashSet<UiPlusComponent> AllComponents;

            static string ClockPanel,
                          ActivePlayersPanel,
                          SleepingPlayersPanel,
                          ServerRewardsPanel,
                          EconomicsPanel,
                          ClockIcon,
                          ActivePlayersIcon,
                          SleepingPlayersIcon,
                          ServerRewardsIcon,
                          EconomicsIcon,
                          ClockText,
                          ActivePlayersText,
                          SleepingPlayersText,
                          ServerRewardsText,
                          EconomicsText;

            static string RecentClockText,
                          RecentApText,
                          RecentSpText;

            static int lastActivePlayers,
                       lastSleepingPlayers;

            static float lastClockUpdate;

            static StringBuilder Builder;

            int lastSrPts, lastEcoPts;

            bool isSRLoaded,
                 isEcoLoaded,
                 isVisible;

            BasePlayer     player;
            PluginSettings Settings;

            bool IsVisible
            {
                get { return isVisible; }
                set
                {
                    if (value == isVisible)
                    {
                        return;
                    }

                    SetVisible(value);
                }
            }

            #region Public API

            public static void Initialize(PluginSettings settings)
            {
                Assert.IsNull(AllComponents, "Calling Initialize while AllComponents is not null!");

                AllComponents = new HashSet<UiPlusComponent>();

                Builder = new StringBuilder();

                BuildUi();

                foreach (BasePlayer player in BasePlayer.activePlayerList)
                {
                    OnPlayerConnected(player);
                }
            }

            public static void Dispose()
            {
                AllComponents?.ToList()?.ForEach(Destroy);

                AllComponents = null;
                Builder = null;

                ClockPanel = ClockIcon = ClockText = ActivePlayersPanel = ActivePlayersIcon =
                    ActivePlayersText = SleepingPlayersPanel = SleepingPlayersIcon = SleepingPlayersText = null;

                RecentClockText = RecentApText = RecentSpText = null;

                lastClockUpdate = 0;
                lastActivePlayers = 0;
                lastSleepingPlayers = 0;
            }

            public static void OnPlayerConnected(BasePlayer player)
            {
                if (AllComponents != null && player.IPlayer.HasPermission(PERMISSION_SEE))
                {
                    player.gameObject.AddComponent<UiPlusComponent>();
                }
            }

            public static void OnPlayerDisconnected(BasePlayer player)
            {
                Destroy(player.gameObject.GetComponent<UiPlusComponent>());
            }

            #endregion

            #region Ui building helpers

            static void BuildUi()
            {
                Assert.IsNull(ClockPanel, "Using BuildUi while ClockPanel is not null!");
                Assert.IsNull(ActivePlayersPanel, "Using BuildUi while ActivePlayersPanel is not null!");
                Assert.IsNull(SleepingPlayersPanel, "Using BuildUi while SleepingPlayersPanel is not null!");

                DebugLog("Building UI...");

                CuiElementContainer reusableContainer = new CuiElementContainer();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_CLOCK_PANEL,
                        Parent = "Hud",
                        Components = {
                            new CuiImageComponent {
                                Color = COLOR_PANEL,
                                ImageType = Image.Type.Simple,
                                Material = PANEL_MATERIAL
                            },
                            GetPanelTransform(
                                Instance.settings.Clock.PosX,
                                Instance.settings.Clock.PosY,
                                Instance.settings.Clock.Scale
                            )
                        }
                    }
                );

                ClockPanel = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_CLOCK_ICON,
                        Parent = NAME_CLOCK_PANEL,
                        Components = {
                            new CuiRawImageComponent {
                                Color = COLOR_ICON,
                                Png = Instance.ImageLibrary.Call<string>("GetImage", "Clock", ImageId)
                            },
                            GetIconTransform()
                        }
                    }
                );

                ClockIcon = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_CLOCK_TEXT,
                        Parent = NAME_CLOCK_PANEL,
                        Components = {
                            new CuiTextComponent {
                                Color = COLOR_TEXT,
                                Align = TextAnchor.MiddleCenter,
                                Text = TEXT_PLACEHOLDER,
                                FontSize = Instance.settings.Clock.FontSize
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                );

                ClockText = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_AP_PANEL,
                        Parent = "Hud",
                        Components = {
                            new CuiImageComponent {
                                Color = COLOR_PANEL,
                                ImageType = Image.Type.Simple,
                                Material = PANEL_MATERIAL
                            },
                            GetPanelTransform(
                                Instance.settings.ActivePlayers.PosX,
                                Instance.settings.ActivePlayers.PosY,
                                Instance.settings.ActivePlayers.Scale
                            )
                        }
                    }
                );

                ActivePlayersPanel = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_AP_ICON,
                        Parent = NAME_AP_PANEL,
                        Components = {
                            new CuiRawImageComponent {
                                Color = COLOR_ICON,
                                Png = Instance.ImageLibrary.Call<string>("GetImage", "ActivePlayers", ImageId)
                            },
                            GetIconTransform()
                        }
                    }
                );

                ActivePlayersIcon = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_AP_TEXT,
                        Parent = NAME_AP_PANEL,
                        Components = {
                            new CuiTextComponent {
                                Color = COLOR_TEXT,
                                Align = TextAnchor.MiddleCenter,
                                Text = TEXT_PLACEHOLDER,
                                FontSize = Instance.settings.ActivePlayers.FontSize
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                );

                ActivePlayersText = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SP_PANEL,
                        Parent = "Hud",
                        Components = {
                            new CuiImageComponent {
                                Color = COLOR_PANEL,
                                ImageType = Image.Type.Simple,
                                Material = PANEL_MATERIAL
                            },
                            GetPanelTransform(
                                Instance.settings.SleepingPlayers.PosX,
                                Instance.settings.SleepingPlayers.PosY,
                                Instance.settings.SleepingPlayers.Scale
                            )
                        }
                    }
                );

                SleepingPlayersPanel = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SP_ICON,
                        Parent = NAME_SP_PANEL,
                        Components = {
                            new CuiRawImageComponent {
                                Color = COLOR_ICON,
                                Png = Instance.ImageLibrary.Call<string>("GetImage", "SleepingPlayers", ImageId)
                            },
                            GetIconTransform()
                        }
                    }
                );

                SleepingPlayersIcon = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SP_TEXT,
                        Parent = NAME_SP_PANEL,
                        Components = {
                            new CuiTextComponent {
                                Color = COLOR_TEXT,
                                Align = TextAnchor.MiddleCenter,
                                Text = TEXT_PLACEHOLDER,
                                FontSize = Instance.settings.SleepingPlayers.FontSize
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                );

                SleepingPlayersText = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SR_PANEL,
                        Parent = "Hud",
                        Components = {
                            new CuiImageComponent {
                                Color = COLOR_PANEL,
                                ImageType = Image.Type.Simple,
                                Material = PANEL_MATERIAL
                            },
                            GetPanelTransform(
                                Instance.settings.ServerRewards.PosX,
                                Instance.settings.ServerRewards.PosY,
                                Instance.settings.ServerRewards.Scale
                            )
                        }
                    }
                );

                ServerRewardsPanel = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SR_ICON,
                        Parent = NAME_SR_PANEL,
                        Components = {
                            new CuiRawImageComponent {
                                Color = COLOR_ICON,
                                Png = Instance.ImageLibrary.Call<string>("GetImage", "ServerRewards", ImageId)
                            },
                            GetIconTransform()
                        }
                    }
                );

                ServerRewardsIcon = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_SR_TEXT,
                        Parent = NAME_SR_PANEL,
                        Components = {
                            new CuiTextComponent {
                                Color = COLOR_TEXT,
                                Align = TextAnchor.MiddleCenter,
                                Text = TEXT_PLACEHOLDER,
                                FontSize = Instance.settings.ServerRewards.FontSize
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                );

                ServerRewardsText = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_ECO_PANEL,
                        Parent = "Hud",
                        Components = {
                            new CuiImageComponent {
                                Color = COLOR_PANEL,
                                ImageType = Image.Type.Simple,
                                Material = PANEL_MATERIAL
                            },
                            GetPanelTransform(
                                Instance.settings.Economics.PosX,
                                Instance.settings.Economics.PosY,
                                Instance.settings.Economics.Scale
                            )
                        }
                    }
                );

                EconomicsPanel = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_ECO_ICON,
                        Parent = NAME_ECO_PANEL,
                        Components = {
                            new CuiRawImageComponent {
                                Color = COLOR_ICON,
                                Png = Instance.ImageLibrary.Call<string>("GetImage", "Economics", ImageId)
                            },
                            GetIconTransform()
                        }
                    }
                );

                EconomicsIcon = CuiHelper.ToJson(reusableContainer);

                reusableContainer.Clear();

                reusableContainer.Add(
                    new CuiElement {
                        Name = NAME_ECO_TEXT,
                        Parent = NAME_ECO_PANEL,
                        Components = {
                            new CuiTextComponent {
                                Color = COLOR_TEXT,
                                Align = TextAnchor.MiddleCenter,
                                Text = TEXT_PLACEHOLDER,
                                FontSize = Instance.settings.Economics.FontSize
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                );

                EconomicsText = CuiHelper.ToJson(reusableContainer);

                UpdateClockText();
                UpdateActivePlayersText();
                UpdateSleepingPlayersText();

                DebugLog("UI building done");
            }

            static CuiRectTransformComponent GetPanelTransform(float x, float y, float scale)
            {
                return new CuiRectTransformComponent {
                    AnchorMin = $"{x} {y}",
                    AnchorMax = $"{x} {y}",
                    OffsetMin = $"{-35 * scale} {-15 * scale}",
                    OffsetMax = $"{35 * scale} {15 * scale}"
                };
            }

            static CuiRectTransformComponent GetIconTransform()
            {
                return new CuiRectTransformComponent {
                    AnchorMin = "0.05 0.1",
                    AnchorMax = "0.4 0.9"
                };
            }

            static string ReplaceText(string ui, string newText)
            {
                DebugLog("Replacing text with {0}", newText);

                return ui.Replace(TEXT_PLACEHOLDER, newText);
            }

            static string FormatTime(TimeSpan time, string format)
            {
                if (format.StartsWith("24::"))
                {
                    return FormatTime(time, format.Substring(4), true);
                }

                if (format.StartsWith("12::"))
                {
                    return FormatTime(time, format.Substring(4), false);
                }

                throw new FormatException($"Unknown time format: {format}");
            }

            static string FormatTime(TimeSpan time, string format, bool _24)
            {
                Builder.Append(format);

                Builder.Replace("hh", _24 ? time.Hours.ToString("00") : (time.Hours % 12.00001).ToString("00"))
                       .Replace("mm", time.Minutes.ToString("00"))
                       .Replace("ss", time.Seconds.ToString("00"));

                Builder.Replace("h", _24 ? time.Hours.ToString("0") : (time.Hours % 12.00001).ToString("0"))
                       .Replace("m", time.Minutes.ToString("0"))
                       .Replace("s", time.Seconds.ToString("0"));

                if (Instance.settings.UiClockFormatAppendAmPm && !_24)
                {
                    Builder.Append(time.Hours > 11 ? "PM" : "AM");
                }

                string str = Builder.ToString();

                DebugLog("Done formatting time: {0}", str);

                Builder.Clear();

                return str;
            }

            static TimeSpan GetServerTime()
            {
                return TOD_Sky.Instance.Cycle.DateTime.TimeOfDay;
            }

            static void UpdateClockText()
            {
                RecentClockText = ReplaceText(ClockText, FormatTime(GetServerTime(), Instance.settings.UiClockFormat));

                DebugLog("New Clock text:\n{0}", RecentClockText);

                lastClockUpdate = Time.realtimeSinceStartup;
            }

            static void UpdateActivePlayersText()
            {
                lastActivePlayers = BasePlayer.activePlayerList.Count;
                RecentApText = ReplaceText(ActivePlayersText, lastActivePlayers.ToString());
            }

            static void UpdateSleepingPlayersText()
            {
                lastSleepingPlayers = BasePlayer.sleepingPlayerList.Count;
                RecentSpText = ReplaceText(SleepingPlayersText, lastSleepingPlayers.ToString());
            }

            #endregion

            #region Unity Messages

            void Awake()
            {
                Assert.IsTrue(Instance.iconsReady, "Initializing component before icons were cached!");
                Assert.IsNotNull(AllComponents, "Initializing component while AllComponents is null!");

                player = GetComponent<BasePlayer>();

                Assert.IsNotNull(player, "Player is null in Awake!");

                AllComponents.Add(this);

                Settings = Instance.settings;

                isSRLoaded = Instance.ServerRewards && Instance.ServerRewards.IsLoaded;
                isEcoLoaded = Instance.Economics && Instance.Economics.IsLoaded;

                ResetMyPts();

                InvokeRepeating(UiTick, Settings.UiUpdateInterval, Settings.UiUpdateInterval);
            }

            void OnDestroy()
            {
                CancelInvoke(UiTick);
                IsVisible = false;

                if (AllComponents != null)
                {
                    AllComponents.Remove(this);
                }
            }

            #endregion

            void ResetMyPts()
            {
                lastSrPts = -1;
                lastEcoPts = -1;
            }

            int GetMySrPts()
            {
                object points = Instance.ServerRewards.Call("CheckPoints", player.userID);

                if (points is int)
                {
                    return (int)points;
                }

                return 0;
            }

            int GetMyEcoPts()
            {
                object balance = Instance.Economics.Call("Balance", player.userID);

                return (int)(double)balance;
            }

            void SetVisible(bool wantsVisible)
            {
                Assert.IsFalse(isVisible == wantsVisible, "Using SetVisible to set the same value!!!");

                DebugLog("Setting panels visible to {0} for player: {1}", wantsVisible, player.displayName);

                if (Settings.Clock.Enable)
                {
                    if (wantsVisible)
                    {
                        CuiHelper.AddUi(player, ClockPanel);
                        CuiHelper.AddUi(player, ClockIcon);
                    }
                    else
                    {
                        CuiHelper.DestroyUi(player, NAME_CLOCK_PANEL);
                    }
                }

                if (Settings.ActivePlayers.Enable)
                {
                    if (wantsVisible)
                    {
                        CuiHelper.AddUi(player, ActivePlayersPanel);
                        CuiHelper.AddUi(player, ActivePlayersIcon);
                    }
                    else
                    {
                        CuiHelper.DestroyUi(player, NAME_AP_PANEL);
                    }
                }

                if (Settings.SleepingPlayers.Enable)
                {
                    if (wantsVisible)
                    {
                        CuiHelper.AddUi(player, SleepingPlayersPanel);
                        CuiHelper.AddUi(player, SleepingPlayersIcon);
                    }
                    else
                    {
                        CuiHelper.DestroyUi(player, NAME_SP_PANEL);
                    }
                }

                if (Settings.ServerRewards.Enable && isSRLoaded)
                {
                    if (wantsVisible)
                    {
                        CuiHelper.AddUi(player, ServerRewardsPanel);
                        CuiHelper.AddUi(player, ServerRewardsIcon);
                    }
                    else
                    {
                        CuiHelper.DestroyUi(player, NAME_SR_PANEL);
                        ResetMyPts();
                    }
                }

                if (Settings.Economics.Enable && isEcoLoaded)
                {
                    if (wantsVisible)
                    {
                        CuiHelper.AddUi(player, EconomicsPanel);
                        CuiHelper.AddUi(player, EconomicsIcon);
                    }
                    else
                    {
                        CuiHelper.DestroyUi(player, NAME_ECO_PANEL);
                        ResetMyPts();
                    }
                }

                isVisible = wantsVisible;
            }

            void UiTick()
            {
                if (!player.IsDead() && !player.IsSleeping())
                {
                    DebugLog(
                        "Ui tick on player {0}",
                        player.displayName
                    );

                    IsVisible = true;
                    UpdateText(
                        Settings.Clock.Enable,
                        Settings.ActivePlayers.Enable,
                        Settings.SleepingPlayers.Enable,
                        Settings.ServerRewards.Enable && isSRLoaded,
                        Settings.Economics.Enable && isEcoLoaded
                    );
                }
            }

            void UpdateText(
                bool updateClock,
                bool updateActivePlayers,
                bool updateSleepingPlayers,
                bool updateServerRewards,
                bool updateEconomics
            )
            {
                if (updateClock)
                {
                    if (Time.realtimeSinceStartup - lastClockUpdate > Settings.UiUpdateInterval)
                    {
                        DebugLog("Calling UpdateClockText");
                        UpdateClockText();
                    }

                    DebugLog("Re-Rendering clock text");
                    CuiHelper.DestroyUi(player, NAME_CLOCK_TEXT);

                    DebugLog("Sending clock text to player {0}:\n{1}", player.displayName, ClockText);

                    CuiHelper.AddUi(player, RecentClockText);
                }

                if (updateActivePlayers)
                {
                    if (BasePlayer.activePlayerList.Count != lastActivePlayers)
                    {
                        UpdateActivePlayersText();
                    }

                    CuiHelper.DestroyUi(player, NAME_AP_TEXT);
                    CuiHelper.AddUi(player, RecentApText);
                }

                if (updateSleepingPlayers)
                {
                    if (BasePlayer.sleepingPlayerList.Count != lastSleepingPlayers)
                    {
                        UpdateSleepingPlayersText();
                    }

                    CuiHelper.DestroyUi(player, NAME_SP_TEXT);
                    CuiHelper.AddUi(player, RecentSpText);
                }

                if (updateServerRewards)
                {
                    int newPts = GetMySrPts();

                    if (newPts != lastSrPts)
                    {
                        CuiHelper.DestroyUi(player, NAME_SR_TEXT);
                        CuiHelper.AddUi(player, ReplaceText(ServerRewardsText, newPts.ToString()));
                        lastSrPts = newPts;
                    }
                }

                if (updateEconomics)
                {
                    int newPts = GetMyEcoPts();

                    if (newPts != lastEcoPts)
                    {
                        CuiHelper.DestroyUi(player, NAME_ECO_TEXT);
                        CuiHelper.AddUi(player, ReplaceText(EconomicsText, newPts.ToString()));
                        lastEcoPts = newPts;
                    }
                }
            }
        }

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings {
                Clock = new PanelSettings {
                    Enable = true,
                    PosX = 0.04f,
                    PosY = 0.046f,
                    Scale = 1f,
                    IconUrl = ICON_CLOCK,
                    FontSize = 15
                },
                ActivePlayers = new PanelSettings {
                    Enable = true,
                    PosX = 0.097f,
                    PosY = 0.046f,
                    Scale = 1f,
                    IconUrl = ICON_ACTIVE_PLAYERS,
                    FontSize = 15
                },
                SleepingPlayers = new PanelSettings {
                    Enable = true,
                    PosX = 0.154f,
                    PosY = 0.046f,
                    Scale = 1f,
                    IconUrl = ICON_SLEEPING_PLAYERS,
                    FontSize = 15
                },
                ServerRewards = new PanelSettings {
                    Enable = false,
                    PosX = 0.211f,
                    PosY = 0.046f,
                    Scale = 1f,
                    IconUrl = ICON_SERVER_REWARDS,
                    FontSize = 15
                },
                Economics = new PanelSettings {
                    Enable = false,
                    PosX = 0.268f,
                    PosY = 0.046f,
                    Scale = 1f,
                    IconUrl = ICON_ECONOMICS,
                    FontSize = 15
                },
                UiUpdateInterval = 2f,
                UiClockFormat = "24::hh:mm", // 12::/24::, (hh):(mm):(ss) (h|m|s)
                UiClockFormatAppendAmPm = false
            };

            [JsonProperty("Clock")] public PanelSettings Clock { get; set; }
            [JsonProperty("Active players")] public PanelSettings ActivePlayers { get; set; }
            [JsonProperty("Sleeping players")] public PanelSettings SleepingPlayers { get; set; }
            [JsonProperty("Server rewards")] public PanelSettings ServerRewards { get; set; }
            [JsonProperty("Economics")] public PanelSettings Economics { get; set; }

            [JsonProperty("Ui update interval")] public float UiUpdateInterval { get; set; }
            [JsonProperty("Ui time format")] public string UiClockFormat { get; set; }

            [JsonProperty("Append AM/PM to 12 hr time format")]
            public bool UiClockFormatAppendAmPm { get; set; }

            public class PanelSettings
            {
                [JsonProperty("Enable")] public bool Enable { get; set; }
                [JsonProperty("Position X")] public float PosX { get; set; }
                [JsonProperty("Position Y")] public float PosY { get; set; }
                [JsonProperty("Scale")] public float Scale { get; set; }
                [JsonProperty("Icon URL")] public string IconUrl { get; set; }
                [JsonProperty("Font size")] public int FontSize { get; set; }
            }
        }
    }
}
