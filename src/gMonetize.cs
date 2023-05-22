using System;
using System.Collections.Generic;
using System.Linq;
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
        private        Timer     _heartbeatTimer;

        #region Oxide hook handlers

        private void Init()
        {
            Instance = this;
            permission.RegisterPermission("gmonetize.use", this);

            covalence.RegisterCommand("gmonetize.open", this, gMonetizeCommandHandler);
            covalence.RegisterCommand("gmonetize.close", this, gMonetizeCommandHandler);
            covalence.RegisterCommand("gmonetize.nextpage", this, gMonetizeCommandHandler);
            covalence.RegisterCommand("gmonetize.prevpage", this, gMonetizeCommandHandler);
            covalence.RegisterCommand("gmonetize.redeemitem", this, gMonetizeCommandHandler);
            covalence.RegisterCommand("shop", this, gMonetizeCommandHandler);

            _api = new Api();
        }

        private void OnServerInitialized()
        {
            _heartbeatTimer = timer.Every(60f, _api.SendHeartbeat);

            foreach (IPlayer player in players.Connected)
                OnUserConnected(player);
        }

        private void Unload()
        {
            _heartbeatTimer.Destroy();
            foreach (IPlayer player in players.Connected)
                OnUserDisconnected(player);
        }

        private void OnUserConnected(IPlayer player) => ((BasePlayer)player.Object).gameObject.AddComponent<Ui>();

        private void OnUserDisconnected(IPlayer player) =>
            UnityEngine.Object.Destroy(((BasePlayer)player.Object).GetComponent<Ui>());

        #endregion

        #region Command handler

        private bool gMonetizeCommandHandler(IPlayer player, string command, string[] args)
        {
            BasePlayer basePlayer = (BasePlayer)player.Object;

            switch (command)
            {
                case "shop":
                case "gmonetize.open":
                    basePlayer.SendMessage("gMonetize_Open", SendMessageOptions.RequireReceiver);
                    break;

                case "gmonetize.close":
                    basePlayer.SendMessage("gMonetize_Close", SendMessageOptions.RequireReceiver);
                    break;

                case "gmonetize.nextpage":
                    basePlayer.SendMessage("gMonetize_NextPage", SendMessageOptions.RequireReceiver);
                    break;

                case "gmonetize.prevpage":
                    basePlayer.SendMessage("gMonetize_PrevPage", SendMessageOptions.RequireReceiver);
                    break;

                case "gmonetize.redeemitem":
                    basePlayer.SendMessage("gMonetize_RedeemItem", args[0], SendMessageOptions.RequireReceiver);
                    break;
            }

            return true;

            /*

            basePlayer.SendMessage(nameof(Ui.gMonetize_UiState_ItemsLoading));

            _api.GetInventory(
                player.Id,
                items =>
                {
                    player.Message(
                        $"Great Success! Strike!: {JsonConvert.SerializeObject(items, Formatting.Indented)}"
                    );
                    basePlayer.SendMessage("gMonetize_UpdateInventory", items, SendMessageOptions.RequireReceiver);
                },
                code =>
                {
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
            );*/
        }

        #endregion

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
                return new Settings {ApiKey = "Change me"};
            }
        }

        #endregion

        #region Api class

        private class Api
        {
            private const string MAIN_API_URL   = "https://api.gmonetize.ru/main/v3";
            private const string STATIC_API_URL = "https://api.gmonetize.ru/static/v2";

            public void GetInventory(string userId, Action<List<InventoryEntry>> callback, Action<int> errorCallback)
            {
                string requestPath = $"{MAIN_API_URL}/plugin/customer/STEAM/{userId}/inventory";

                Interface.Oxide.LogInfo("Request path: {0}", requestPath);

                Instance.webrequest.Enqueue(
                    requestPath,
                    string.Empty,
                    (code, body) =>
                    {
                        if (code != 200)
                        {
                            errorCallback(code);
                        }
                        else
                        {
                            Instance.Log("GetInventory response: {0}", body);

                            List<InventoryEntry> items = JsonConvert.DeserializeObject<List<InventoryEntry>>(
                                body,
                                new JsonSerializerSettings {
                                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                                }
                            );

                            /*List<InventoryEntry> items = new List<InventoryEntry> {
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                                new InventoryEntry {
                                    Id = "1",
                                    Good = new BaseGoodInfo {
                                        Name = "Some good",
                                        IconId = "123"
                                    }
                                },
                            };*/

                            callback(items);
                        }
                    },
                    Instance,
                    headers: new Dictionary<string, string> {{"Authorization", "ApiKey " + Instance._settings.ApiKey}}
                );
            }

            public void RedeemItem(string userId, string entryId, Action<int, string> callback)
            {
                // string requestPath = MAIN_API_URL.Replace("{userid}", userId) + $"/inventory/{entryId}/redeem";

                string requestPath = MAIN_API_URL + $"/plugin/customer/STEAM/{userId}/inventory/{entryId}/redeem";

                Interface.Oxide.LogInfo("Request path: {0}", requestPath);

                Instance.webrequest.Enqueue(
                    requestPath,
                    string.Empty,
                    callback,
                    Instance,
                    headers: new Dictionary<string, string> {
                        {"Authorization", "ApiKey " + Instance._settings.ApiKey},
                        {"Content-Type", "application/json"}
                    },
                    method: RequestMethod.POST
                );
            }

            public void SendHeartbeat()
            {
                string requestPath = "https://api.gmonetize.ru/main/v3/plugin/server/ping";

                var requestObject = new {
                    motd = Server.description,
                    map = new {
                        name = Server.level,
                        width = World.Size,
                        height = World.Size,
                        seed = World.Seed,
                        lastWipe = SaveRestore.SaveCreatedTime.ToString("O").TrimEnd('Z')
                    },
                    players = new {
                        online = Instance.players.Connected.Count(),
                        max = Server.maxplayers
                    }
                };

                string body = JsonConvert.SerializeObject(requestObject);

                Instance.webrequest.Enqueue(
                    requestPath,
                    body,
                    (code, response) => { Interface.Oxide.LogInfo("Response: {0}, {1}", code, response); },
                    Instance,
                    RequestMethod.POST,
                    headers: new Dictionary<string, string> {
                        {"Authorization", "ApiKey " + Instance._settings.ApiKey},
                        {"Content-Type", "application/json"}
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
            private BasePlayer               _player;
            private State                    _state;
            private List<Api.InventoryEntry> _inventory;
            private int                      _currentPageIndex;

            #region Unity event functions

            private void Start()
            {
                _inventory = new List<Api.InventoryEntry>();
                _player = GetComponent<BasePlayer>();
            }

            private void OnDestroy() => gMonetize_Close();

            #endregion

            #region Command message handlers

            private void gMonetize_Open()
            {
                if (_state != State.Closed)
                {
                    return;
                }

                CuiHelper.AddUi(_player, Builder.GetMainContainer());
                CuiHelper.AddUi(_player, Builder.GetMainContainerNotification("Loading items...", "1 1 1 1"));
                _state = State.LoadingItems;

                /*call api to load items*/
                Instance._api.GetInventory(
                    _player.UserIDString,
                    items =>
                    {
                        Instance.Log(
                            "Received inventory of player {0}: [{1}]",
                            _player.displayName,
                            string.Join(", ", items.Select(x => x.Id))
                        );

                        _inventory.Clear();
                        _inventory.AddRange(items);

                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);

                        if (_inventory.IsEmpty())
                        {
                            CuiHelper.AddUi(
                                _player,
                                Builder.GetMainContainerNotification("Inventory is empty =(", "1 1 1 1")
                            );
                            _state = State.NoItems;
                        }
                        else
                        {
                            /*CuiHelper.AddUi(_player, Builder.GetItemListContainer());
                            var cards = _inventory.Select(
                                (entry, index) =>
                                {
                                    var onPageIndex = index % Builder.ITEMS_PER_PAGE;
                                    return Builder.GetCard(onPageIndex, entry);
                                }
                            );
                            foreach (var card in cards)
                            {
                                CuiHelper.AddUi(_player, card);
                            }

                            _state = State.ItemPageDisplay;*/
                            _currentPageIndex = 0;
                            DisplayCurrentPage();
                        }
                    },
                    error =>
                    {
                        Instance.LogError(
                            "Error while receiving inventory for player {0}: {1}",
                            _player.displayName,
                            error
                        );

                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);

                        CuiHelper.AddUi(
                            _player,
                            Builder.GetMainContainerNotification($"Fuck: {error}", "1 0.2 0.2 0.6")
                        );
                        _state = State.ItemsLoadError;
                    }
                );
            }

            private void gMonetize_Close()
            {
                if (_state == State.Closed)
                {
                    return;
                }

                CuiHelper.DestroyUi(_player, Builder.Names.Main.CONTAINER);
                _state = State.Closed;
            }

            private void gMonetize_NextPage()
            {
                if (_state != State.ItemPageDisplay)
                    return;

                var pageCount = Mathf.CeilToInt(_inventory.Count / (float)Builder.ITEMS_PER_PAGE);

                if (_currentPageIndex == pageCount - 1)
                    return;

                _currentPageIndex++;
                DisplayCurrentPage();
            }

            private void gMonetize_PrevPage()
            {
                if (_state != State.ItemPageDisplay)
                    return;

                if (_currentPageIndex == 0)
                    return;

                _currentPageIndex--;
                DisplayCurrentPage();
            }

            private void gMonetize_RedeemItem(string id)
            {
                var entryIndex = _inventory.FindIndex(x => x.Id == id);

                if (entryIndex == -1)
                {
                    Instance.LogError(
                        "Player {0} is trying to receive item not from his inventory: {1}",
                        _player.UserIDString,
                        id
                    );
                    return;
                }

                Instance.Log("Redeeming item {0}", id);

                CuiHelper.DestroyUi(_player, Builder.Names.ItemList.Card(id).RedeemButton);
                /*CuiHelper.AddUi(
                    _player,
                    new List<CuiElement> {
                        new CuiElement {
                            Parent = Builder.Names.ItemList.Card(id).RedeemButton,
                            Name = Builder.Names.ItemList.Card(id).RedeemButtonLabel,
                            Components = {
                                new CuiTextComponent {
                                    Text = "REDEEMING...",
                                    Align = TextAnchor.MiddleCenter
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        }
                    }
                );*/

                CuiHelper.AddUi(_player, Builder.GetRedeemingButton(id));

                Instance._api.RedeemItem(_player.UserIDString, id,
                    (code, _) =>
                    {
                        if (code != 204)
                        {
                            Instance.LogError("Player {0} failed to receive item {1} ({2})", _player.UserIDString, id, code);
                            return;
                        }


                        Instance.Log("Redeemed item {0}", id);
                        /*var indexOnPage = entryIndex % Builder.ITEMS_PER_PAGE;

                        var entry = _inventory[entryIndex];*/
                    });
            }

            #endregion

            private void DisplayCurrentPage()
            {
                if (_state != State.ItemPageDisplay)
                {
                    CuiHelper.AddUi(_player, Builder.GetItemListContainer());
                }

                var currentPageItems = _inventory.Skip(Builder.ITEMS_PER_PAGE * _currentPageIndex);
                currentPageItems.Select(
                                    (item, index) =>
                                    {
                                        var indexOnPage = index % Builder.ITEMS_PER_PAGE;

                                        return Builder.GetCard(indexOnPage, item);
                                    }
                                )
                                .ToList()
                                .ForEach(card => CuiHelper.AddUi(_player, card));

                _state = State.ItemPageDisplay;
            }

            private enum State
            {
                Closed,
                LoadingItems,
                ItemPageDisplay,
                NoItems,
                ItemsLoadError,
                RedeemingItem,
                ItemRedeemError
            }

            private struct DisplayedInventoryEntry
            {
                public int AbsoluteIndex { get; set; }
                public int IndexOnPage { get; set; }
                public Api.InventoryEntry Entry { get; set; }
                public bool IsIconLoaded { get; set; }
            }

            private static class Builder
            {
                public const  int   COLUMN_COUNT   = 7;
                public const  int   ROW_COUNT      = 3;
                public const  int   ITEMS_PER_PAGE = COLUMN_COUNT * ROW_COUNT;
                private const float COLUMN_GAP     = .005f;
                private const float ROW_GAP        = .01f;

                public static string GetRedeemingButton(string id)
                {
                    /*new CuiElement {
                        Parent = ncard.Container,
                        Name = ncard.RedeemButton,
                        Components = {
                            new CuiButtonComponent {
                                Command = $"gmonetize.redeemitem {inventoryEntry.Id}",
                                Color = "0.2 0.6 0.2 0.6"
                            },
                            GetTransform(
                                .05f,
                                .02f,
                                .95f,
                                .18f
                            )
                        }
                    }*/
                    var ncard = Names.ItemList.Card(id);

                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = ncard.Container,
                                Name = ncard.RedeemButton,
                                Components = {
                                    new CuiButtonComponent {Color = "0.4 0.4 0.4 0.5"},
                                    GetTransform(
                                        .05f,
                                        .02f,
                                        .95f,
                                        .18f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = ncard.RedeemButton,
                                Name = ncard.RedeemButtonLabel,
                                Components = {
                                    new CuiTextComponent {
                                        Text = "REDEEMING...",
                                        Align = TextAnchor.MiddleCenter,
                                        Color = "0.6 0.6 0.6 0.8"
                                    }
                                }
                            }
                        }
                    );
                }

                public static string GetMainContainer()
                {
                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = "Hud",
                                Name = Names.Main.CONTAINER,
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
                                Name = Names.Main.Button.CLOSE,
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
                                Name = Names.Main.Label.CLOSE_TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = Colors.Build(
                                            1,
                                            1,
                                            1,
                                            0.5f
                                        ),
                                        Align = TextAnchor.MiddleCenter,
                                        Text = "CLOSE",
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
                                Name = Names.Main.Label.NOTIFICATION_TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = color,
                                        Align = TextAnchor.MiddleCenter,
                                        FontSize = 18,
                                        Text = message
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
                                Name = Names.ItemList.CONTAINER,
                                Components = {
                                    new CuiImageComponent {Color = "0.4 0.4 0.4 0.5"},
                                    GetTransform(
                                        0.005f,
                                        0.01f,
                                        0.995f,
                                        0.94f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name = Names.Main.Button.PREV,
                                Components = {
                                    new CuiButtonComponent {Color = "0.4 0.4 0.4 0.4"},
                                    GetTransform(
                                        0.005f,
                                        0.95f,
                                        0.05f,
                                        0.99f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.Button.PREV,
                                Name = Names.Main.Button.PREV + ":text",
                                Components = {
                                    new CuiTextComponent {
                                        Text = "PREVIOUS",
                                        Align = TextAnchor.MiddleCenter,
                                        Color = "1 1 1 0.4",
                                        FontSize = 12
                                    },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name = Names.Main.Button.NEXT,
                                Components = {
                                    new CuiButtonComponent {Color = "1 1 1 0.4"},
                                    GetTransform(
                                        0.055f,
                                        0.95f,
                                        0.1f,
                                        0.99f
                                    )
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.Button.NEXT,
                                Name = Names.Main.Button.NEXT + ":text",
                                Components = {
                                    new CuiTextComponent {
                                        Text = "NEXT",
                                        Align = TextAnchor.MiddleCenter,
                                        Color = "1 1 1 0.4",
                                        FontSize = 12
                                    },
                                    GetTransform()
                                }
                            },
                        }
                    );
                }

                public static string GetCard(int indexOnPage, Api.InventoryEntry inventoryEntry)
                {
                    const string defaultIcon = "https://cdn.icon-icons.com/icons2/1381/PNG/512/rust_94773.png";
                    var ncard = Names.ItemList.Card(inventoryEntry.Id);

                    CuiElementContainer container = new CuiElementContainer {
                        new CuiElement {
                            Parent = Names.ItemList.CONTAINER,
                            Name = ncard.Container,
                            Components = {
                                new CuiImageComponent {Color = "0.4 0.4 0.4 0.6"},
                                GetGridTransform(indexOnPage)
                            }
                        },
                        new CuiElement {
                            Parent = ncard.Container,
                            Name = ncard.InnerContainer,
                            Components = {
                                new CuiImageComponent {Color = "1 1 1 0"},
                                GetTransform(
                                    .05f,
                                    .2f,
                                    .95f,
                                    .95f
                                )
                            }
                        },
                        new CuiElement {
                            Parent = ncard.Container,
                            Name = ncard.NameLabel,
                            Components = {
                                new CuiTextComponent {
                                    Text = string.IsNullOrEmpty(inventoryEntry.Good.Name)
                                        ? "ITEM"
                                        : inventoryEntry.Good.Name,
                                    Align = TextAnchor.MiddleCenter,
                                    Color = "1 1 1 0.4"
                                },
                                GetTransform(yMin: .91f, yMax: 1f)
                            }
                        },
                        new CuiElement {
                            Parent = ncard.Container,
                            Name = ncard.RedeemButton,
                            Components = {
                                new CuiButtonComponent {
                                    Command = $"gmonetize.redeemitem {inventoryEntry.Id}",
                                    Color = "0.2 0.6 0.2 0.6"
                                },
                                GetTransform(
                                    .05f,
                                    .02f,
                                    .95f,
                                    .18f
                                )
                            }
                        },
                        new CuiElement {
                            Parent = ncard.RedeemButton,
                            Name = ncard.RedeemButtonLabel,
                            Components = {
                                new CuiTextComponent {
                                    Text = "REDEEM",
                                    Color = "1 1 1 0.5",
                                    Align = TextAnchor.MiddleCenter
                                },
                                GetTransform()
                            }
                        }
                    };
                    var iconEl = new CuiElement {
                        Parent = ncard.InnerContainer,
                        Name = ncard.Icon,
                        Components = {
                            GetTransform()
                        }
                    };
                    if (string.IsNullOrEmpty(inventoryEntry.Good.IconId))
                    {
                        switch (inventoryEntry.Type)
                        {
                            case Api.InventoryEntry.EntryType.ITEM:
                                iconEl.Components.Insert(0, new CuiImageComponent {
                                    ItemId = inventoryEntry.Item.ItemId
                                });
                                break;

                            case Api.InventoryEntry.EntryType.KIT:
                                iconEl.Components.Insert(0, new CuiImageComponent {
                                    ItemId = inventoryEntry.Items[0].ItemId
                                });
                                break;

                            default:
                                iconEl.Components.Insert(0, new CuiRawImageComponent {
                                    Url = defaultIcon
                                });
                                break;
                        }
                    }
                    else
                    {
                        iconEl.Components.Insert(0, new CuiRawImageComponent {
                            Url = "https://api.gmonetize.ru/static/v2/image/" + inventoryEntry.Good.IconId
                        });
                    }

                    container.Add(iconEl);

                    return CuiHelper.ToJson(container);
                }

                public static IEnumerable<CuiElement> GetNotificationWindow(string text, string color)
                {
                    return new[] {
                        new CuiElement {
                            Parent = Names.Main.CONTAINER,
                            Name = "gm_main_notification_container",
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
                            Name = "gm_main_notification_header",
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
                            Name = "gm_main_notification_header:text",
                            Components = {
                                new CuiTextComponent {
                                    Text = "Notification",
                                    Align = TextAnchor.MiddleCenter
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0",
                                    AnchorMax = "1 1"
                                }
                            }
                        },
                        new CuiElement {
                            Parent = "gm_main_notification_container",
                            Name = "gm_main_notification_container:text",
                            Components = {
                                new CuiTextComponent {
                                    Text = text,
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

                private static CuiRectTransformComponent GetGridTransform(int itemIndex)
                {
                    const float totalColumnGap = COLUMN_GAP * (COLUMN_COUNT - 1);
                    const float totalRowGap = ROW_GAP * (ROW_COUNT - 1);

                    const float cardWidth = (0.999f - totalColumnGap) / COLUMN_COUNT;
                    const float cardHeight = (0.998f - totalRowGap) / ROW_COUNT;

                    int rowIndex = itemIndex / COLUMN_COUNT;
                    int columnIndex = itemIndex % COLUMN_COUNT;

                    float columnGapSum = COLUMN_GAP * columnIndex;
                    float cardWidthSum = cardWidth * columnIndex;
                    float xPosition = columnGapSum + cardWidthSum;

                    float rowGapSum = ROW_GAP * rowIndex;
                    float cardHeightSum = cardHeight * (rowIndex + 1);

                    float yPosition = 0.998f - (rowGapSum + cardHeightSum);

                    return GetTransform(
                        xPosition,
                        yPosition,
                        xPosition + cardWidth,
                        yPosition + cardHeight
                    );
                }

                public static class Names
                {
                    public static class Main
                    {
                        public const string CONTAINER = "gmonetize.main.container";

                        public static class Label
                        {
                            public const string CLOSE_TEXT = "gmonetize.main.label.close";

                            public const string NOTIFICATION_TEXT = "gmonetize.main.label.notification";
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
                            public string NameLabel => Base + "label.name";

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
