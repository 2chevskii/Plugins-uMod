using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms.VisualStyles;
using ConVar;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace Oxide.Plugins
{
    [Info("gMonetize", "2CHEVSKII", "1.0.0")]
    public class gMonetize : CovalencePlugin
    {
        private static gMonetize Instance;
        private        Api       _api;
        private        Settings  _settings;

        private void Init()
        {
            Instance = this;
            permission.RegisterPermission("gmonetize.use", this);

            covalence.RegisterCommand(
                "gmonetize.open",
                this,
                (caller, cmd, args) => {
                    CmdGm(caller, cmd, args);
                    return true;
                }
            );
            covalence.RegisterCommand(
                "gmonetize.close",
                this,
                (caller, command, args) => {
                    var basePlayer = (BasePlayer) caller.Object;

                    basePlayer.SendMessage(
                        "gMonetize_UiState_Closed",
                        SendMessageOptions.RequireReceiver
                    );
                    basePlayer.SendMessage(
                        "gMonetize_ClearInventory",
                        SendMessageOptions.RequireReceiver
                    );

                    return true;
                }
            );

            _api = new Api();
        }

        private void OnServerInitialized()
        {
            timer.Every(10f, _api.SendHeartbeat);

            foreach ( IPlayer player in players.Connected )
            {
                OnUserConnected(player);
            }
        }

        private void Unload()
        {
            foreach ( IPlayer player in players.Connected )
            {
                OnUserDisconnected(player);
            }
        }

        private void OnUserConnected(IPlayer player)
        {
            ((BasePlayer) player.Object).gameObject.AddComponent<Ui>();
        }

        private void OnUserDisconnected(IPlayer player)
        {
            GameObject.Destroy(((BasePlayer) player.Object).GetComponent<Ui>());
        }

        [Command("gm")]
        private void CmdGm(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer) player.Object;

            basePlayer.SendMessage(nameof(Ui.gMonetize_UiState_ItemsLoading));

            _api.GetInventory(
                player.Id,
                items => {
                    player.Message(
                        $"Great Success! Strike!: {JsonConvert.SerializeObject(items, Formatting.Indented)}"
                    );
                    basePlayer.SendMessage(
                        "gMonetize_UpdateInventory",
                        items,
                        SendMessageOptions.RequireReceiver
                    );
                },
                code => {
                    player.Message($"Failed: {code}");
                    basePlayer.SendMessage(
                        "gMonetize_UpdateInventory",
                        new Api.InventoryEntry[] { },
                        SendMessageOptions.RequireReceiver
                    );
                    basePlayer.SendMessage(
                        "gMonetize_UiState_ItemsLoadError",
                        code,
                        SendMessageOptions.RequireReceiver
                    );
                }
            );
        }

#region Settings

        protected override void LoadDefaultConfig()
        {
            _settings = Settings.GetDefault();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            _settings = Config.ReadObject<Settings>();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_settings);
        }

#endregion

#region Settings class

        private class Settings
        {
            public string ApiKey { get; set; }

            public static Settings GetDefault()
            {
                return new Settings { ApiKey = "Change me" };
            }
        }

#endregion

#region Api class

        private class Api
        {
            private const string MAIN_API_URL   = "https://api.gmonetize.ru/main/v3";
            private const string STATIC_API_URL = "https://api.gmonetize.ru/static/v2";

            public void GetInventory(
                string userId,
                Action<List<InventoryEntry>> callback,
                Action<int> errorCallback
            )
            {
                var requestPath = $"{MAIN_API_URL}/plugin/customer/STEAM/{userId}/inventory";

                Interface.Oxide.LogInfo("Request path: {0}", requestPath);

                Instance.webrequest.Enqueue(
                    requestPath,
                    string.Empty,
                    (code, body) => {
                        if ( code != 200 )
                        {
                            errorCallback(code);
                        }
                        else
                        {
                            var items = JsonConvert.DeserializeObject<List<InventoryEntry>>(
                                body,
                                new JsonSerializerSettings {
                                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                                }
                            );

                            callback(items);
                        }
                    },
                    Instance,
                    headers: new Dictionary<string, string> {
                        { "Authorization", "ApiKey " + Instance._settings.ApiKey }
                    }
                );
            }

            public void RedeemItem(string userId, string entryId, Action<int, string> callback)
            {
                var requestPath = MAIN_API_URL.Replace("{userid}", userId) +
                                  $"/inventory/{entryId}/redeem";

                Interface.Oxide.LogInfo("Request path: {0}", requestPath);

                Instance.webrequest.Enqueue(
                    requestPath,
                    string.Empty,
                    callback,
                    Instance,
                    headers: new Dictionary<string, string> {
                        {
                            "Authorization",
                            "ApiKey " + Instance._settings.ApiKey
                        },
                        { "Content-Type", "application/json" }
                    },
                    method: RequestMethod.POST
                );
            }

            public void SendHeartbeat()
            {
                var requestPath = "https://api.gmonetize.ru/main/v3/plugin/server/ping";

                var requestObject = new {
                    motd = Server.description,
                    map = new {
                        name     = Server.level,
                        width    = World.Size,
                        height   = World.Size,
                        seed     = World.Seed,
                        lastWipe = SaveRestore.SaveCreatedTime.ToString("O").TrimEnd('Z')
                    },
                    players = new {
                        online = Instance.players.Connected.Count(),
                        max    = Server.maxplayers
                    }
                };

                var body = JsonConvert.SerializeObject(requestObject);

                Instance.webrequest.Enqueue(
                    requestPath,
                    body,
                    (code, response) => {
                        Interface.Oxide.LogInfo("Response: {0}, {1}", code, response);
                    },
                    Instance,
                    RequestMethod.POST,
                    headers: new Dictionary<string, string> {
                        { "Authorization", "ApiKey " + Instance._settings.ApiKey },
                        { "Content-Type", "application/json" }
                    }
                );
            }

            public class InventoryEntry
            {
                public string Id { get; set; }
                public EntryType Type { get; set; }
                public BaseGoodInfo Good { get; set; }
                public RustItem Item { get; set; }
                public List<RustItem> Items { get; set; }
                public Rank Rank { get; set; }
                public Research Research { get; set; }

                public enum EntryType
                {
                    ITEM,
                    KIT,
                    RANK,
                    RESEARCH
                }
            }

            public class BaseGoodInfo
            {
                public string Id { get; set; }
                public string Name { get; set; }
                public string IconId { get; set; }
            }

            public class RustItem
            {
                public string Id { get; set; }
                public int ItemId { get; set; }
                public int Amount { get; set; }
                public RustItemMeta Meta { get; set; }

                public class RustItemMeta
                {
                    public ulong? SkinId { get; set; }
                    public float Condition { get; set; }
                }
            }

            public class Rank
            {
                public string Id { get; set; }
                public string GroupName { get; set; }
                public TimeSpan? Duration { get; set; }
            }

            public class Research
            {
                public string Id { get; set; }
                public string ResearchId { get; set; }
            }
        }

#endregion

        private class Ui : MonoBehaviour
        {
            private const int ITEMS_PER_PAGE = 20;

            private BasePlayer               _player;
            private State                    _state;
            private List<Api.InventoryEntry> _inventory;

#region Unity event functions

            private void Start()
            {
                _inventory = new List<Api.InventoryEntry>();
                _player    = GetComponent<BasePlayer>();
            }

            private void OnDestroy() => gMonetize_UiState_Closed();

#endregion

            public void gMonetize_UiState_ItemsLoading()
            {
                EnsureMainContainerDisplayed();

                switch (_state)
                {
                    case State.Closed:

                        break;
                    case State.ItemListError:

                        break;
                }

                if ( _state == State.ItemListLoading )
                {
                    return;
                }

                if ( _state == State.Closed )
                {
                    CuiHelper.AddUi(_player, Builder.GetMainContainer());
                }
                else if ( _state == State.ItemListError )
                {
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                }

                CuiHelper.AddUi(
                    _player,
                    Builder.GetMainContainerNotification(
                        "Loading items...",
                        Builder.Colors.Build(alpha: 0.4f)
                    )
                );
                _state = State.ItemListLoading;
            }

            public void gMonetize_UiState_ItemsLoadError(int errorCode)
            {
                EnsureMainContainerDisplayed();

                if ( _state == State.ItemListDisplay )
                {
                    CuiHelper.DestroyUi(_player, "gm_main_notification_container");
                }

                if ( _state == State.ItemListError || _state == State.ItemListLoading )
                {
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                }

                /*CuiHelper.AddUi(
                    _player,
                    Builder.GetMainContainerNotification(
                        $"Could not load items =( (code: {errorCode})",
                        Builder.Colors.Build(
                            0.7f,
                            0.4f,
                            0.4f,
                            0.7f
                        )
                    )
                );*/

                CuiHelper.AddUi(
                    _player,
                    CuiHelper.ToJson(
                        Builder.GetNotificationWindow(
                                   $"Could not load items =( (code: {errorCode})",
                                   Builder.Colors.Build(
                                       0.7f,
                                       0.4f,
                                       0.4f,
                                       0.7f
                                   )
                               )
                               .ToList()
                    )
                );
                _state = State.ItemListError;
            }

            public void gMonetize_UiState_Closed()
            {
                if ( _state == State.Closed )
                {
                    return;
                }

                CuiHelper.DestroyUi(_player, Builder.Names.Main.CONTAINER);
                _state = State.Closed;
            }

            private void RemoveNotification()
            {
                CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
            }

            private void EnsureMainContainerDisplayed()
            {
                if ( _state == State.Closed )
                {
                    CuiHelper.AddUi(_player, Builder.GetMainContainer());
                }
            }

            private int GetPageCount() =>
            Mathf.CeilToInt(_inventory.Count / (float) ITEMS_PER_PAGE);

            private int GetItemPage(int index)
            {
                return Mathf.CeilToInt(index / (float) ITEMS_PER_PAGE);
            }

            private enum State
            {
                Closed,
                ItemListLoading,
                ItemListDisplay,
                ItemListError,
                RedeemingItemLoading,
                RedeemingItemError
            }

            private enum ItemState
            {
                NoDisplay,
                Display,
                Redeeming,
                RedeemError
            }

            private static class Builder
            {
                public static string GetMainContainer()
                {
                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = "Hud",
                                Name   = Names.Main.CONTAINER,
                                Components = {
                                    new CuiImageComponent {
                                        Color = Colors.Build(
                                            0.4f,
                                            0.4f,
                                            0.4f,
                                            0.7f
                                        ),
                                        Material = Materials.BLUR
                                    },
                                    GetTransform(
                                        0.013f,
                                        0.15f,
                                        0.987f,
                                        0.95f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Button.CLOSE,
                                Components = {
                                    new CuiButtonComponent {
                                        Color = Colors.Build(
                                            0.7f,
                                            0.4f,
                                            0.4f,
                                            0.95f
                                        ),
                                        Command = "gmonetize.close"
                                    },
                                    GetTransform(
                                        0.95f,
                                        0.95f,
                                        0.995f,
                                        0.99f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.Button.CLOSE,
                                Name   = Names.Main.Label.CLOSE_TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = Colors.Build(
                                            1,
                                            1,
                                            1,
                                            0.5f
                                        ),
                                        Align    = TextAnchor.MiddleCenter,
                                        Text     = "CLOSE",
                                        FontSize = 15
                                    },
                                    GetTransform()
                                }
                            }
                        }
                    );
                }

                public static string GetMainContainerNotification(string message, string color)
                {
                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Label.NOTIFICATION_TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = color,
                                        Align =
                                        TextAnchor.MiddleCenter,
                                        FontSize = 18,
                                        Text     = message
                                    },
                                    GetTransform(yMax: 0.95f)
                                }
                            }
                        }
                    );
                }

                public static string GetItemListContainer()
                {
                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.ItemList.CONTAINER,
                                Components = {
                                    new CuiImageComponent { Color = "0.4 0.4 0.4 0.5" },
                                    /*new CuiRectTransformComponent {
                                        AnchorMin = "0.05 0.1",
                                        AnchorMax = "0.95 0.9"
                                    }*/
                                    GetTransform(
                                        0.05f,
                                        0.1f,
                                        0.95f,
                                        0.9f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Button.PREV,
                                Components = {
                                    new CuiButtonComponent { Color = "0.4 0.4 0.4 1" },
                                    GetTransform(
                                        0,
                                        0.5f,
                                        0.1f,
                                        0.7f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.Button.PREV,
                                Name   = Names.Main.Button.PREV + ":text",
                                Components = {
                                    new CuiTextComponent { Text = "<-" },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Button.NEXT,
                                Components = {
                                    new CuiButtonComponent { Color = "0.4 0.4 0.4 1" },
                                    GetTransform(
                                        0.9f,
                                        0.5f,
                                        1,
                                        0.7f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.Button.NEXT,
                                Name   = Names.Main.Button.NEXT + ":text",
                                Components = {
                                    new CuiTextComponent { Text = "->" },
                                    GetTransform()
                                }
                            },
                        }
                    );
                }

                public static string GetCard(Api.InventoryEntry inventoryEntry) { }

                public static IEnumerable<CuiElement> GetNotificationWindow(
                    string text,
                    string color
                )
                {
                    return new[] {
                        new CuiElement {
                            Parent = Names.Main.CONTAINER,
                            Name =
                            "gm_main_notification_container",
                            Components = {
                                new CuiImageComponent {
                                    Color = "0.6 0.6 0.6 0.7",
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0.35 0.3",
                                    AnchorMax = "0.65 0.7"
                                }
                            }
                        },
                        new CuiElement {
                            Parent = "gm_main_notification_container",
                            Name   = "gm_main_notification_header",
                            Components = {
                                new CuiImageComponent {
                                    Color = "0.6 0.6 0.6 0.9",
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0.9",
                                    AnchorMax = "1 1"
                                }
                            }
                        },
                        new CuiElement {
                            Parent = "gm_main_notification_header",
                            Name   = "gm_main_notification_header:text",
                            Components = {
                                new CuiTextComponent {
                                    Text = "Notification",
                                    Align =
                                    TextAnchor.MiddleCenter
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        },
                        new CuiElement {
                            Parent = "gm_main_notification_container",
                            Name   = "gm_main_notification_container:text",
                            Components = {
                                new CuiTextComponent {
                                    Text  = text,
                                    Color = color
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 0.9"
                                }
                            }
                        }
                    };
                }

                private static CuiRectTransformComponent GetTransform(
                    float xMin = 0f,
                    float yMin = 0f,
                    float xMax = 1f,
                    float yMax = 1f
                )
                {
                    return new CuiRectTransformComponent {
                        AnchorMin = $"{xMin} {yMin}",
                        AnchorMax = $"{xMax} {yMax}"
                    };
                }

                public static class Names
                {
                    public static class Main
                    {
                        public const string CONTAINER = "gmonetize.main.container";

                        public static class Label
                        {
                            public const string CLOSE_TEXT = "gmonetize.main.label.close";

                            public const string NOTIFICATION_TEXT =
                            "gmonetize.main.label.notification";
                        }

                        public static class Button
                        {
                            public const string CLOSE = "gmonetize.main.button.close";
                            public const string NEXT  = "gmonetize.main.button.nextpage";
                            public const string PREV  = "gmonetize.main.button.prevpage";
                        }
                    }

                    public static class ItemList
                    {
                        public const string CONTAINER = "gmonetize.itemlist.container";

                        public static ItemListCard Card(string id)
                        {
                            return new ItemListCard(id);
                        }

                        public struct ItemListCard
                        {
                            private readonly string _id;

                            public string Container => Base + "container";
                            public string InnerContainer => Base + "inner-container";
                            public string Icon => InnerContainer + "icon";
                            public string AmountLabel => InnerContainer + "amount-label";
                            public string ConditionBar => InnerContainer + "condition-bar";
                            public string RedeemButton => Container + "redeem-button";
                            public string RedeemButtonLabel => Container + "redeem-button-label";
                            public string RedeemStatusText => InnerContainer + "redeem-status-text";

                            private string Base => $"gmonetize.itemlist.card.{_id}.";

                            public ItemListCard(string id)
                            {
                                _id = id;
                            }
                        }
                    }
                }

                private static class Materials
                {
                    public const string BLUR = "assets/content/ui/uibackgroundblur.mat";
                }

                public static class Colors
                {
                    public static string Build(
                        float red = 1f,
                        float green = 1f,
                        float blue = 1f,
                        float alpha = 1f
                    )
                    {
                        return $"{red} {green} {blue} {alpha}";
                    }
                }
            }
        }
    }
}
