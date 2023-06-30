#define DEBUG
//test actions
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using ConVar;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

// ReSharper disable StringLiteralTypo

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace Oxide.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    [Info("gMonetize", "2CHEVSKII", "1.0.1")]
    public class gMonetize : CovalencePlugin
    {
        private static gMonetize Instance;
        private Api _api;
        private PluginConfiguration _configuration;
        private Timer _heartbeatTimer;

        [Conditional("DEBUG")]
        private static void LogDebug(string format, params object[] args) => Interface.Oxide.LogDebug(format, args);

        #region Oxide hook handlers

        private void Init()
        {
            Instance = this;
            // TODO
            permission.RegisterPermission("gmonetize.use", this);

            covalence.RegisterCommand("gmonetize.open", this, HandleCommand);
            covalence.RegisterCommand("gmonetize.close", this, HandleCommand);
            covalence.RegisterCommand("gmonetize.nextpage", this, HandleCommand);
            covalence.RegisterCommand("gmonetize.prevpage", this, HandleCommand);
            covalence.RegisterCommand("gmonetize.redeemitem", this, HandleCommand);

            foreach (string chatCommand in _configuration.ChatCommands)
            {
                covalence.RegisterCommand(chatCommand, this, HandleCommand);
            }

            _api = new Api();
        }

        private void OnServerInitialized()
        {
            _heartbeatTimer = timer.Every(60f, _api.SendHeartbeat);
            foreach (IPlayer player in players.Connected)
            {
                OnUserConnected(player);
            }
        }

        private void Unload()
        {
            _heartbeatTimer.Destroy();
            foreach (IPlayer player in players.Connected)
            {
                OnUserDisconnected(player);
            }
        }

        private void OnUserConnected(IPlayer player) => ((BasePlayer)player.Object).gameObject.AddComponent<Ui>();

        private void OnUserDisconnected(IPlayer player) =>
            UnityEngine.Object.Destroy(((BasePlayer)player.Object).GetComponent<Ui>());

        #endregion

        #region Command handler

        private bool HandleCommand(IPlayer player, string command, string[] args)
        {
            BasePlayer basePlayer = (BasePlayer)player.Object;

            switch (command)
            {
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

                default:
                    if (command == "gmonetize.open" || _configuration.ChatCommands.Contains(command))
                    {
                        basePlayer.SendMessage("gMonetize_Open", SendMessageOptions.RequireReceiver);
                    }

                    break;
            }

            return true;
        }

        #endregion

        #region Configuration handling

        protected override void LoadDefaultConfig()
        {
            _configuration = PluginConfiguration.GetDefault();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _configuration = Config.ReadObject<PluginConfiguration>();

                if (_configuration == null)
                {
                    throw new Exception("Failed to load configuration: configuration object is null");
                }

                if (_configuration.ChatCommands == null || _configuration.ChatCommands.Length == 0)
                {
                    LogWarning("No chat commands were specified in configuration");
                    _configuration.ChatCommands = Array.Empty<string>();
                    SaveConfig();
                }

                LogDebug("ApiKey: {0}", _configuration.ApiKey);
                LogDebug("Chat commands: {0}", string.Join(", ", _configuration.ChatCommands));
            }
            catch (Exception e)
            {
                LogError(e.ToString());
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_configuration);
        }

        #endregion

        #region Configuration class

        private class PluginConfiguration
        {
            [JsonProperty("API key")]
            public string ApiKey { get; set; }

            [JsonProperty("Api base URL")]
            public string ApiBaseUrl { get; set; }

            [JsonProperty("Chat commands")]
            public string[] ChatCommands { get; set; }

            public static PluginConfiguration GetDefault() => new PluginConfiguration
            {
                ApiKey = "Change me",
                ApiBaseUrl = "https://api.gmonetize.ru",
                ChatCommands = new[] { "shop" }
            };
        }

        #endregion

        #region Api class

        private class Api
        {
            private readonly JsonSerializerSettings _serializerSettings;
            private readonly Dictionary<string, string> _requestHeaders;

            private string MainApiUrl => Instance._configuration.ApiBaseUrl + "/main/v3/plugin";
            private string StaticApiUrl => Instance._configuration.ApiBaseUrl + "/static/v2/image";
            private string HeartbeatRequestUrl => MainApiUrl + "/server/ping";

            public Api()
            {
                if (string.IsNullOrEmpty(Instance._configuration.ApiKey))
                {
                    throw new Exception("No API Key found in config");
                }

                _serializerSettings = new JsonSerializerSettings
                {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                _requestHeaders = new Dictionary<string, string> {
                    {"Authorization", "ApiKey " + Instance._configuration.ApiKey},
                    {"Content-Type", "application/json"}
                };
            }

            public void GetInventory(string userId, Action<List<InventoryEntry>> onSuccess, Action<int> onError)
            {
                Instance.webrequest.Enqueue(
                    GetInventoryUrl(userId),
                    string.Empty,
                    (code, body) =>
                    {
                        LogDebug("GetInventory result: {0}:{1}", code, body);
                        if (code == 200)
                        {
                            List<InventoryEntry> items =
                                JsonConvert.DeserializeObject<List<InventoryEntry>>(body, _serializerSettings);

                            LogDebug("Inventory:\n{0}", JsonConvert.SerializeObject(items, Formatting.Indented));
                            onSuccess(items);
                            return;
                        }

                        onError(code);
                    },
                    Instance,
                    headers: _requestHeaders
                );
            }

            public void RedeemItem(
                string userId,
                string entryId,
                Action onSuccess,
                Action<int> onError
            )
            {
                Instance.webrequest.Enqueue(
                    GetRedeemUrl(userId, entryId),
                    string.Empty,
                    (code, body) =>
                    {
                        LogDebug("Item redeem result: {0}:{1}", code, body);
                        if (code == 204)
                        {
                            onSuccess();
                            return;
                        }

                        onError(code);
                    },
                    Instance,
                    headers: _requestHeaders,
                    method: RequestMethod.POST
                );
            }

            public void SendHeartbeat()
            {
                ServerHeartbeat heartbeat = new ServerHeartbeat(
                    Server.description,
                    new ServerHeartbeat.ServerMap(
                        Server.level,
                        World.Size,
                        World.Seed,
                        SaveRestore.SaveCreatedTime
                    ),
                    new ServerHeartbeat.ServerPlayers(BasePlayer.activePlayerList.Count, Server.maxplayers)
                );

                Instance.webrequest.Enqueue(
                    HeartbeatRequestUrl,
                    JsonConvert.SerializeObject(heartbeat),
                    (code, body) =>
                    {
                        if (code != 204)
                        {
                            Instance.LogWarning("Failed to send heartbeat ({0}:{1})", code, body);
                            return;
                        }

                        LogDebug("Heartbeat sent: {0}", code);
                    },
                    Instance,
                    RequestMethod.POST,
                    headers: _requestHeaders
                );
            }

            public string GetIconUrl(string iconId) => $"{StaticApiUrl}/{iconId}";

            private string GetInventoryUrl(string userId)
            {
                return $"{MainApiUrl}/customer/STEAM/{userId}/inventory";
            }

            private string GetRedeemUrl(string userId, string entryId)
            {
                return $"{MainApiUrl}/customer/STEAM/{userId}/inventory/{entryId}/redeem";
            }

            [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
            public class InventoryEntry
            {
                public string Id { get; set; }
                public EntryType Type { get; set; }
                public string Name { get; set; }
                public string IconId { get; set; }
                public TimeSpan? WipeBlockDuration { get; set; }
                public bool IsRefundable { get; set; }
                public string GoodId { get; set; }
                public RustItem Item { get; set; }

                [SuppressMessage("ReSharper", "CollectionNeverUpdated.Local")]
                public List<RustItem> Items { get; set; }

                public Rank Rank { get; set; }
                public Research Research { get; set; }

                public enum EntryType
                {
                    ITEM,
                    KIT,
                    RANK,
                    RESEARCH,
                    CUSTOM
                }
            }

            [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Local")]
            public class RustItem
            {
                public string Id { get; set; }
                public int ItemId { get; set; }
                public int Amount { get; set; }
                public RustItemMeta Meta { get; set; }
                public string IconId { get; set; }

                public ItemDefinition GetItemDefinition()
                {
                    return ItemManager.FindItemDefinition(ItemId);
                }

                public Item ToItem()
                {
                    var item = ItemManager.Create(GetItemDefinition(), Amount, Meta.SkinId ?? 0ul);
                    if (item.hasCondition)
                    {
                        item.conditionNormalized = Mathf.Max(0.1f, Meta.Condition);
                    }

                    return item;
                }

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
                public int ResearchId { get; set; }
            }

            /// <summary>
            /// DTO to update server's state in the store
            /// </summary>
            private struct ServerHeartbeat
            {
                [JsonProperty("motd")]
                public string Description { get; }

                [JsonProperty("map")]
                public ServerMap Map { get; }

                [JsonProperty("players")]
                public ServerPlayers Players { get; }

                public ServerHeartbeat(string description, ServerMap map, ServerPlayers players)
                {
                    Description = description;
                    Map = map;
                    Players = players;
                }

                public struct ServerMap
                {
                    [JsonProperty("name")]
                    public string Name { get; }

                    [JsonProperty("width")]
                    public uint Width { get; }

                    [JsonProperty("height")]
                    public uint Height { get; }

                    [JsonProperty("seed")]
                    public uint Seed { get; }

                    [JsonProperty("lastWipe")]
                    public string LastWipe { get; }

                    public ServerMap(
                        string name,
                        uint size,
                        uint seed,
                        DateTime lastWipeDate
                    )
                    {
                        Name = name;
                        Width = Height = size;
                        Seed = seed;
                        LastWipe = lastWipeDate.ToString("O").TrimEnd('Z');
                    }
                }

                public struct ServerPlayers
                {
                    [JsonProperty("online")]
                    public int Online { get; }

                    [JsonProperty("max")]
                    public int Max { get; }

                    public ServerPlayers(int online, int max)
                    {
                        Online = online;
                        Max = max;
                    }
                }
            }
        }

        #endregion

        private class Ui : MonoBehaviour
        {
            private BasePlayer _player;
            private State _state;
            private List<Api.InventoryEntry> _inventory;
            private int _currentPageIndex;

            private int PageCount => GetPageCount(_inventory.Count);

            private static int GetPageCount(int itemCount)
            {
                return itemCount / Builder.ITEMS_PER_PAGE + (itemCount % Builder.ITEMS_PER_PAGE == 0 ? 0 : 1);
            }

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
                State_LoadingItems();

                Instance._api.GetInventory(
                    _player.UserIDString,
                    items =>
                    {
                        _inventory.Clear();
                        _inventory.AddRange(items);

                        if (_inventory.IsEmpty())
                        {
                            State_NoItems();
                        }
                        else
                        {
                            State_ItemPageDisplay();
                            _currentPageIndex = 0;
                            DisplayCurrentItemPage();
                        }
                    },
                    errorCode =>
                    {
                        Instance.LogError(
                            "Error while receiving inventory for player {0}: {1}",
                            _player.displayName,
                            errorCode
                        );

                        State_ItemsLoadError(errorCode);
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
                if (_state == State.Closed)
                {
                    Instance.LogWarning(nameof(gMonetize_NextPage) + " called while UI was closed");
                }

                bool hasNextPage = _currentPageIndex < PageCount - 1;

                if (!hasNextPage)
                    return;

                State_ItemPageDisplay();
                RemoveCurrentPageItems();
                _currentPageIndex++;
                DisplayCurrentItemPage();
            }

            private void gMonetize_PrevPage()
            {
                if (_state == State.Closed)
                {
                    Instance.LogWarning(nameof(gMonetize_PrevPage) + " called while UI was closed");
                }

                bool hasPrevPage = _currentPageIndex > 0;

                if (!hasPrevPage)
                    return;

                State_ItemPageDisplay();
                RemoveCurrentPageItems();
                _currentPageIndex--;
                DisplayCurrentItemPage();
            }

            private void gMonetize_RedeemItem(string id)
            {
                int entryIndex = _inventory.FindIndex(x => x.Id == id);

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
                CuiHelper.AddUi(_player, Builder.GetRedeemingButton(id));

                Instance._api.RedeemItem(
                    _player.UserIDString,
                    id,
                    () =>
                    {
                        Api.InventoryEntry entry = _inventory[entryIndex];
                        GiveRedeemedItems(entry);
                        RemoveCurrentPageItems();

                        _inventory.RemoveAt(entryIndex);

                        if (_inventory.Count == 0)
                        {
                            _currentPageIndex = 0;
                            State_NoItems();
                        }
                        else
                        {
                            if (_currentPageIndex >= PageCount)
                            {
                                _currentPageIndex = PageCount - 1;
                            }

                            DisplayCurrentItemPage();
                        }
                    },
                    code => Instance.LogError(
                        "Failed to redeem item {0} for player {1}: {2}",
                        id,
                        _player.UserIDString,
                        code
                    )
                );
            }

            #endregion

            void GiveRedeemedItems(Api.InventoryEntry entry)
            {
                switch (entry.Type)
                {
                    case Api.InventoryEntry.EntryType.ITEM:
                        Item item = ItemManager.CreateByItemID(
                            entry.Item.ItemId,
                            entry.Item.Amount,
                            entry.Item.Meta.SkinId ?? 0
                        );
                        _player.GiveItem(item);
                        break;
                    case Api.InventoryEntry.EntryType.KIT:
                        entry.Items.Select(i => ItemManager.CreateByItemID(i.ItemId, i.Amount, i.Meta.SkinId ?? 0))
                             .ToList()
                             .ForEach(i => _player.GiveItem(i));
                        break;
                    case Api.InventoryEntry.EntryType.RANK:
                        break;
                    case Api.InventoryEntry.EntryType.RESEARCH:
                        RedeemResearchItem(entry.Research);
                        break;
                }
            }

            private void RedeemRustItem(Api.RustItem rustItem)
            {
                var item = rustItem.ToItem();

                if (item == null)
                {
                    Instance.LogError("Failed to create Item object from Api.RustItem[{0}:{1}]", rustItem.Id, rustItem.ItemId);
                    return;
                }

                _player.GiveItem(item);
            }

            private void RedeemResearchItem(Api.Research research)
            {
                int itemId = research.ResearchId;
                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemId);
                _player.blueprints.Unlock(itemDefinition);
            }

            private void State_ItemsLoadError(int errorCode)
            {
                EnsureMainContainerIsDisplayed();

                switch (_state)
                {
                    case State.ItemsLoadError:
                    case State.NoItems:
                    case State.LoadingItems:
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                        break;

                    case State.ItemPageDisplay:
                        CuiHelper.DestroyUi(_player, Builder.Names.ItemList.CONTAINER);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.PREV);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.NEXT);
                        break;
                }

                CuiHelper.AddUi(
                    _player,
                    Builder.GetMainContainerNotification($"Failed to load items: {errorCode}", "1 0 0 1")
                );
                _state = State.ItemsLoadError;
            }

            private void State_LoadingItems()
            {
                EnsureMainContainerIsDisplayed();

                switch (_state)
                {
                    case State.LoadingItems:
                        return;
                    case State.ItemPageDisplay:
                        CuiHelper.DestroyUi(_player, Builder.Names.ItemList.CONTAINER);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.PREV);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.NEXT);
                        break;
                    case State.NoItems:
                    case State.ItemsLoadError:
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                        break;
                }

                CuiHelper.AddUi(_player, Builder.GetMainContainerNotification("Loading items...", "1 1 1 1"));
                _state = State.LoadingItems;
            }

            private void State_NoItems()
            {
                EnsureMainContainerIsDisplayed();

                switch (_state)
                {
                    case State.NoItems:
                        return;
                    case State.ItemsLoadError:
                    case State.LoadingItems:
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                        break;
                    case State.ItemPageDisplay:
                        CuiHelper.DestroyUi(_player, Builder.Names.ItemList.CONTAINER);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.PREV);
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.NEXT);
                        break;
                }

                CuiHelper.AddUi(_player, Builder.GetMainContainerNotification("Inventory is empty =(", "1 1 1 1"));
                _state = State.NoItems;
            }

            private void State_ItemPageDisplay()
            {
                EnsureMainContainerIsDisplayed();

                switch (_state)
                {
                    case State.ItemPageDisplay:
                        return;
                    case State.LoadingItems:
                    case State.NoItems:
                    case State.ItemsLoadError:
                        CuiHelper.DestroyUi(_player, Builder.Names.Main.Label.NOTIFICATION_TEXT);
                        break;
                }

                CuiHelper.AddUi(_player, Builder.GetItemListContainer());
                _state = State.ItemPageDisplay;
            }

            private void DisplayCurrentItemPage()
            {
                if (_state == State.ItemPageDisplay)
                {
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.PREV);
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.NEXT);
                }

                bool hasPrevPage = _currentPageIndex > 0;
                bool hasNextPage = _currentPageIndex < PageCount - 1;

                CuiHelper.AddUi(_player, Builder.GetItemListButtons(hasPrevPage, hasNextPage));

                IEnumerable<Api.InventoryEntry> currentPageItems =
                    _inventory.Skip(Builder.ITEMS_PER_PAGE * _currentPageIndex).Take(Builder.ITEMS_PER_PAGE);

                CuiHelper.AddUi(
                    _player,
                    currentPageItems.Select(
                                        (item, index) =>
                                        {
                                            int indexOnPage = index % Builder.ITEMS_PER_PAGE;

                                            bool canReceive = CanReceiveItem(item);
                                            string text = !canReceive ? "CANNOT REDEEM" : "REDEEM";

                                            IEnumerable<CuiElement> card = Builder.GetCard(indexOnPage, item);
                                            IEnumerable<CuiElement> button = Builder.GetCardButton(
                                                item.Id,
                                                !canReceive,
                                                text,
                                                "gmonetize.redeemitem " + item.Id
                                            );
                                            return card.Concat(button);
                                        }
                                    )
                                    .SelectMany(x => x)
                                    .ToList()
                );
            }

            private void EnsureMainContainerIsDisplayed()
            {
                if (_state == State.Closed)
                {
                    CuiHelper.AddUi(_player, Builder.GetMainContainer());
                }
            }

            private void RemoveCurrentPageItems()
            {
                IEnumerable<Api.InventoryEntry> currentPageItems =
                    _inventory.Skip(Builder.ITEMS_PER_PAGE * _currentPageIndex).Take(Builder.ITEMS_PER_PAGE);

                currentPageItems.Select(item => item.Id)
                                .ToList()
                                .ForEach(id => CuiHelper.DestroyUi(_player, Builder.Names.ItemList.Card(id).Container));
            }

            private int GetAvailableSlots()
            {
                int totalSlots = _player.inventory.containerMain.capacity + _player.inventory.containerBelt.capacity;
                int claimedSlots = _player.inventory.containerMain.itemList.Count +
                                   _player.inventory.containerBelt.itemList.Count;

                return totalSlots - claimedSlots;
            }

            private bool IsAvailableForResearch(Api.Research research)
            {
                int itemId = research.ResearchId;

                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemId);

                if (itemDefinition == null)
                {
                    Instance.LogWarning("Not found ItemDefinition for itemid {0} in IsAvailableForResearch", itemId);
                    return false;
                }


                bool isUnlocked = _player.blueprints.IsUnlocked(itemDefinition);
                LogDebug(
                    "Player {0} item {1} is unlocked: {2}",
                    _player.displayName,
                    itemDefinition.displayName.english,
                    isUnlocked
                );

                return !isUnlocked;
            }

            private bool CanReceiveItem(Api.InventoryEntry item)
            {
                switch (item.Type)
                {
                    case Api.InventoryEntry.EntryType.ITEM:
                        return GetAvailableSlots() > 0;
                    case Api.InventoryEntry.EntryType.KIT:
                        return GetAvailableSlots() >= item.Items.Count;
                    case Api.InventoryEntry.EntryType.RANK:
                        return false;
                    case Api.InventoryEntry.EntryType.RESEARCH:
                        return IsAvailableForResearch(item.Research);
                    case Api.InventoryEntry.EntryType.CUSTOM:
                        return false;
                    default: return false;
                }
            }

            private enum State
            {
                Closed,
                LoadingItems,
                ItemPageDisplay,
                NoItems,
                ItemsLoadError,
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
                public const int COLUMN_COUNT = 7;
                public const int ROW_COUNT = 3;
                public const int ITEMS_PER_PAGE = COLUMN_COUNT * ROW_COUNT;
                private const float COLUMN_GAP = .005f;
                private const float ROW_GAP = .01f;
                private const string DEFAULT_ICON_URL = "https://cdn.icon-icons.com/icons2/1381/PNG/512/rust_94773.png";

                public static string GetRedeemingButton(string id)
                {
                    Names.ItemList.ItemListCard ncard = Names.ItemList.Card(id);

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
                                            0.4f
                                        ),
                                        Material = Materials.BLUR
                                    },
                                    GetTransform(
                                        0.013f,
                                        0.14f,
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
                                            0.8f,
                                            0.3f,
                                            0.3f,
                                            0.6f
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
                                            0.8f,
                                            0.8f,
                                            0.8f,
                                            0.5f
                                        ),
                                        Align = TextAnchor.MiddleCenter,
                                        Text = "CLOSE",
                                        FontSize = 15
                                    },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name = Names.Main.CONTAINER + ":needscursor",
                                Components = {new CuiNeedsCursorComponent()}
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

                public static string GetItemListButtons(bool hasPrevPage, bool hasNextPage)
                {
                    const string COLOR_DISABLED = "0.4 0.4 0.4 0.5";
                    const string COLOR_ENABLED = "0.7 0.7 0.7 0.35";
                    const string COLOR_TEXT = "0.8 0.8 0.8 0.4";

                    string prevButtonColor = hasPrevPage ? COLOR_ENABLED : COLOR_DISABLED;
                    string nextButtonColor = hasNextPage ? COLOR_ENABLED : COLOR_DISABLED;

                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name = Names.Main.Button.PREV,
                                Components = {
                                    new CuiButtonComponent {
                                        Color = prevButtonColor,
                                        Command = hasPrevPage ? "gmonetize.prevpage" : string.Empty
                                    },
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
                                        Color = COLOR_TEXT,
                                        FontSize = 12
                                    },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name = Names.Main.Button.NEXT,
                                Components = {
                                    new CuiButtonComponent {
                                        Color = nextButtonColor,
                                        Command = hasNextPage ? "gmonetize.nextpage" : string.Empty
                                    },
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
                                        Color = COLOR_TEXT,
                                        FontSize = 12
                                    },
                                    GetTransform()
                                }
                            },
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
                            }
                        }
                    );
                }

                public static IEnumerable<CuiElement> GetCardButton(
                    string cardId,
                    bool isDisabled,
                    string text,
                    string command
                )
                {
                    CuiRectTransformComponent buttonTransform = GetTransform(
                        .05f,
                        .02f,
                        .95f,
                        .18f
                    );

                    string buttonColor = isDisabled ? "0.4 0.4 0.4 0.5" : "0.25 0.5 0.3 0.5";

                    CuiElement button = new CuiElement
                    {
                        Parent = Names.ItemList.Card(cardId).Container,
                        Name = Names.ItemList.Card(cardId).RedeemButton,
                        Components = {
                            new CuiButtonComponent {
                                Command = isDisabled ? null : command,
                                Color = buttonColor
                            },
                            buttonTransform
                        }
                    };

                    CuiElement buttonLabel = new CuiElement
                    {
                        Parent = Names.ItemList.Card(cardId).RedeemButton,
                        Name = Names.ItemList.Card(cardId).RedeemButtonLabel,
                        Components = {
                            new CuiTextComponent {
                                Text = text,
                                Color = "0.6 0.6 0.6 0.8",
                                Align = TextAnchor.MiddleCenter
                            },
                            GetTransform()
                        }
                    };

                    return new List<CuiElement> {
                        button,
                        buttonLabel
                    };
                }

                public static IEnumerable<CuiElement> GetCard(int indexOnPage, Api.InventoryEntry inventoryEntry)
                {
                    Names.ItemList.ItemListCard ncard = Names.ItemList.Card(inventoryEntry.Id);

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
                                new CuiImageComponent {Color = "0 0 0 0"},
                                GetTransform(
                                    .03f,
                                    .2f,
                                    .97f,
                                    .90f
                                )
                            }
                        },
                        new CuiElement {
                            Parent = ncard.Container,
                            Name = ncard.NameLabel + ":container",
                            Components = {
                                new CuiImageComponent {Color = "0.7 0.7 0.7 0.25"},
                                GetTransform(yMin: 0.91f, yMax: 1f)
                            }
                        },
                        new CuiElement {
                            Parent = ncard.NameLabel + ":container",
                            Name = ncard.NameLabel,
                            Components = {
                                new CuiTextComponent {
                                    Text = string.IsNullOrEmpty(inventoryEntry.Name) ? "ITEM" : inventoryEntry.Name,
                                    Align = TextAnchor.MiddleCenter,
                                    Color = "0.8 0.8 0.8 0.4"
                                },
                                GetTransform()
                            }
                        }
                    };
                    CuiElement iconEl = new CuiElement
                    {
                        Parent = ncard.InnerContainer,
                        Name = ncard.Icon,
                        Components = { GetTransform() }
                    };

                    LogDebug("Iconid for good {0}: {1}", inventoryEntry.Name, inventoryEntry.IconId);
                    if (string.IsNullOrEmpty(inventoryEntry.IconId))
                    {
                        switch (inventoryEntry.Type)
                        {
                            case Api.InventoryEntry.EntryType.ITEM:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiImageComponent
                                    {
                                        ItemId = inventoryEntry.Item.ItemId,
                                        Color = "1 1 1 0.8"
                                    }
                                );
                                break;

                            case Api.InventoryEntry.EntryType.KIT:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiImageComponent
                                    {
                                        ItemId = inventoryEntry.Items[0].ItemId,
                                        Color = "1 1 1 0.8"
                                    }
                                );
                                break;

                            default:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiRawImageComponent
                                    {
                                        Url = DEFAULT_ICON_URL,
                                        Color = "1 1 1 0.8"
                                    }
                                );
                                break;
                        }
                    }
                    else
                    {
                        iconEl.Components.Insert(
                            0,
                            new CuiRawImageComponent { Url = Instance._api.GetIconUrl(inventoryEntry.IconId) }
                        );
                    }

                    container.Add(iconEl);

                    return container;
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
                    return new CuiRectTransformComponent
                    {
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
                            public const string NEXT = "gmonetize.main.button.nextpage";
                            public const string PREV = "gmonetize.main.button.prevpage";
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
