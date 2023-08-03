// #define DEBUG

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reflection;
using ConVar;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngineInternal;

// ReSharper disable StringLiteralTypo

// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace Oxide.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    [Info("gMonetize", "2CHEVSKII", "1.0.2")]
    public class gMonetize : CovalencePlugin
    {
        private const string PERM_USE = "gmonetize.use";

        private const string CMD_OPEN   = "gmonetize.open";
        private const string CMD_CLOSE  = "gmonetize.close";
        private const string CMD_NEXTP  = "gmonetize.nextpage";
        private const string CMD_PREVP  = "gmonetize.prevpage";
        private const string CMD_REDEEM = "gmonetize.redeemitem";

        private static gMonetize           Instance;
        private        Api                 _api;
        private        PluginConfiguration _configuration;
        private        Timer               _heartbeatTimer;

        [Conditional("DEBUG")]
        private static void LogDebug(string format, params object[] args) =>
        Interface.Oxide.LogDebug(format, args);

        private static void LogMessage(string format, params object[] args)
        {
            string text = string.Format(format, args);
            Instance.LogToFile(
                "log",
                text,
                Instance,
                true,
                true
            );
        }

#region Oxide hook handlers

        private void Init()
        {
            Instance = this;
            // TODO
            permission.RegisterPermission(PERM_USE, this);

            covalence.RegisterCommand(CMD_OPEN, this, HandleCommand);
            covalence.RegisterCommand(CMD_CLOSE, this, HandleCommand);
            covalence.RegisterCommand(CMD_NEXTP, this, HandleCommand);
            covalence.RegisterCommand(CMD_PREVP, this, HandleCommand);
            covalence.RegisterCommand(CMD_REDEEM, this, HandleCommand);

            foreach ( string chatCommand in _configuration.ChatCommands )
            {
                covalence.RegisterCommand(chatCommand, this, HandleCommand);
            }

            _api = new Api();
        }

        private void OnServerInitialized()
        {
            SetupServerTags();
            gAPI.Init(this);
            SetupAPICallbacks();
            StartSendingHeartbeats();

            foreach ( IPlayer player in players.Connected )
            {
                OnUserConnected(player);
            }
        }

        private void Unload()
        {
            CleanupServerTags();
            StopSendingHeartbeats();
            CleanupAPICallbacks();

            foreach ( IPlayer player in players.Connected )
            {
                OnUserDisconnected(player);
            }
        }

        private void OnUserConnected(IPlayer player) =>
        ((BasePlayer) player.Object).gameObject.AddComponent<Ui>();

        private void OnUserDisconnected(IPlayer player) =>
        UnityEngine.Object.Destroy(((BasePlayer) player.Object).GetComponent<Ui>());

#endregion

        private void SetupAPICallbacks()
        {
            gAPI.OnHeartbeat += HandleOnHeartbeat;
        }

        private void CleanupAPICallbacks()
        {
            gAPI.OnHeartbeat -= HandleOnHeartbeat;
        }

        private void StartSendingHeartbeats()
        {
            _heartbeatTimer = timer.Every(60, SendHeartbeat);
            SendHeartbeat();
        }

        private void StopSendingHeartbeats()
        {
            _heartbeatTimer.Destroy();
        }

        private void SendHeartbeat()
        {
            ServerHeartbeatRequest request = new ServerHeartbeatRequest(
                Server.description,
                new ServerHeartbeatRequest.ServerMapRequest(
                    Server.level,
                    World.Size,
                    World.Seed,
                    SaveRestore.SaveCreatedTime
                ),
                new ServerHeartbeatRequest.ServerPlayersRequest(
                    BasePlayer.activePlayerList.Count,
                    Server.maxplayers
                )
            );

            gAPI.SendHeartbeat(request);
        }

        private void SetupServerTags()
        {
            if ( !Server.tags.Contains("gmonetize") )
            {
                Server.tags = string.Join(
                    ",",
                    Server.tags.Split(new[] { "," }, StringSplitOptions.RemoveEmptyEntries)
                          .Concat(new[] { "gmonetize" })
                );

                LogMessage("Added gmonetize tag to server tags");
                LogMessage("Server tags are now: {0}", Server.tags);
            }
        }

        private void CleanupServerTags()
        {
            if ( Server.tags.Contains("gmonetize") )
            {
                Server.tags = string.Join(
                    ",",
                    Server.tags.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                          .Except(new[] { "gmonetize" })
                );

                LogMessage("Removed gmonetize tags from server tags");
                LogMessage("Server tags are now: {0}", Server.tags);
            }
        }

        private void HandleOnHeartbeat(HeartbeatApiResult result)
        {
            if ( !result.IsSuccess )
            {
                LogMessage(
                    "Server heartbeat request failed with code {0}\nHeartbeat payload was:\n{1}",
                    result.StatusCode,
                    JsonConvert.SerializeObject(result.Request, Formatting.Indented)
                );
            }
            else
            {
                LogMessage("Server heartbeat was sent successfully");
            }
        }

        private void HandleOnInventory(InventoryApiResult result)
        {
            if ( !result.IsSuccess )
            {
                LogMessage(
                    "Failed to receive inventory for player {0}, request failed with code {1}",
                    result.UserId,
                    result.StatusCode
                );
            }
            else
            {
                LogMessage(
                    "Received inventory for player {0}:\n{1}",
                    result.UserId,
                    JsonConvert.SerializeObject(result.Inventory, Formatting.Indented)
                );
            }

            BasePlayer player =
            BasePlayer.activePlayerList.FirstOrDefault(x => x.UserIDString == result.UserId);

            if ( !player )
            {
                LogMessage(
                    "OnInventory callback handler could not find the player with userId {0}",
                    result.UserId
                );
                return;
            }

            player.SendMessage("gMonetize_InventoryReceived", SendMessageOptions.RequireReceiver);
        }

#region Command handler

        private bool HandleCommand(IPlayer player, string command, string[] args)
        {
            if ( player.IsServer )
            {
                player.Message("This command cannot be executed in server console");
                return true;
            }

            BasePlayer basePlayer = (BasePlayer) player.Object;

            LogMessage(
                "HandleCommand({0}:{1}, {2}, {3})",
                player.Name,
                player.Id,
                command,
                $"[{string.Join(", ", args)}]"
            );

            switch (command)
            {
                case CMD_CLOSE:
                    basePlayer.SendMessage("gMonetize_Close", SendMessageOptions.RequireReceiver);
                    break;

                case CMD_NEXTP:
                    basePlayer.SendMessage(
                        "gMonetize_NextPage",
                        SendMessageOptions.RequireReceiver
                    );
                    break;

                case CMD_PREVP:
                    basePlayer.SendMessage(
                        "gMonetize_PrevPage",
                        SendMessageOptions.RequireReceiver
                    );
                    break;

                case CMD_REDEEM:
                    basePlayer.SendMessage(
                        "gMonetize_RedeemItem",
                        args[0],
                        SendMessageOptions.RequireReceiver
                    );
                    break;

                default:
                    if ( command == CMD_OPEN || _configuration.ChatCommands.Contains(command) )
                    {
                        basePlayer.SendMessage(
                            "gMonetize_Open",
                            SendMessageOptions.RequireReceiver
                        );
                    }

                    break;
            }

            return true;
        }

        private bool CheckPermission(IPlayer player) => player.HasPermission("gmonetize.use");

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

                if ( _configuration == null )
                {
                    throw new Exception(
                        "Failed to load configuration: configuration object is null"
                    );
                }

                if ( _configuration.ChatCommands == null ||
                     _configuration.ChatCommands.Length == 0 )
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

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string> {
                    [PlayerMessages.CHAT_PREFIX]   = "[gMonetize] ",
                    [PlayerMessages.NO_PERMISSION] = "You are not allowed to use this command",
                    [PlayerMessages.INV_EMPTY]     = "Your inventory is empty",
                    [PlayerMessages.ERROR_LOADING_ITEMS] =
                    "Error occured while loading your inventory\n(code: {0})"
                },
                this
            );
            lang.RegisterMessages(new Dictionary<string, string> { }, this, "ru");
        }

#region Configuration class

        private class PluginConfiguration
        {
            [JsonProperty("API key")] public string ApiKey { get; set; }

            [JsonProperty("Api base URL")] public string ApiBaseUrl { get; set; }

            [JsonProperty("Chat commands")] public string[] ChatCommands { get; set; }

            public static PluginConfiguration GetDefault() => new PluginConfiguration {
                ApiKey       = "Change me",
                ApiBaseUrl   = "https://api.gmonetize.ru",
                ChatCommands = new[] { "shop" }
            };
        }

#endregion

#region Api class

        private class Api
        {
            private readonly JsonSerializerSettings     _serializerSettings;
            private readonly Dictionary<string, string> _requestHeaders;

            private string MainApiUrl => Instance._configuration.ApiBaseUrl + "/main/v3/plugin";
            private string StaticApiUrl => Instance._configuration.ApiBaseUrl + "/static/v2/image";
            private string HeartbeatRequestUrl => MainApiUrl + "/server/ping";

            public Api()
            {
                LogMessage("Initializing API...");
                if ( string.IsNullOrEmpty(Instance._configuration.ApiKey) )
                {
                    LogMessage("Failed to initialize API: API key is missing");
                    throw new Exception("No API Key found in config");
                }

                _serializerSettings = new JsonSerializerSettings {
                    ContractResolver = new CamelCasePropertyNamesContractResolver()
                };
                _requestHeaders = new Dictionary<string, string> {
                    { "Authorization", "ApiKey " + Instance._configuration.ApiKey },
                    { "Content-Type", "application/json" }
                };
            }

            public void GetInventory(
                string userId,
                Action<List<InventoryEntry>> onSuccess,
                Action<int> onError
            )
            {
                Instance.webrequest.Enqueue(
                    GetInventoryUrl(userId),
                    string.Empty,
                    (code, body) => {
                        LogDebug("GetInventory result: {0}:{1}", code, body);
                        LogMessage(
                            "GetInventory({0}) = {1}:{2}",
                            userId,
                            code,
                            body
                        );

                        if ( code == 200 )
                        {
                            List<InventoryEntry> items =
                            JsonConvert.DeserializeObject<List<InventoryEntry>>(
                                body,
                                _serializerSettings
                            );

                            LogDebug(
                                "Inventory:\n{0}",
                                JsonConvert.SerializeObject(items, Formatting.Indented)
                            );
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
                    (code, body) => {
                        LogDebug("Item redeem result: {0}:{1}", code, body);

                        LogMessage(
                            "RedeemItem({0}, {1}) = {2}:{3}",
                            userId,
                            entryId,
                            code,
                            body
                        );

                        if ( code == 204 )
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
                    ItemDefinition itemDefinition = GetItemDefinition();

                    if ( itemDefinition == null )
                    {
                        throw new Exception(
                            $"ItemDefinition not found for item {ItemId} (good.item.id={Id})"
                        );
                    }

                    Item item = ItemManager.Create(GetItemDefinition(), Amount, Meta.SkinId ?? 0ul);
                    if ( item.hasCondition )
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
                [JsonProperty("motd")] public string Description { get; }

                [JsonProperty("map")] public ServerMap Map { get; }

                [JsonProperty("players")] public ServerPlayers Players { get; }

                public ServerHeartbeat(string description, ServerMap map, ServerPlayers players)
                {
                    Description = description;
                    Map         = map;
                    Players     = players;
                }

                public struct ServerMap
                {
                    [JsonProperty("name")] public string Name { get; }

                    [JsonProperty("width")] public uint Width { get; }

                    [JsonProperty("height")] public uint Height { get; }

                    [JsonProperty("seed")] public uint Seed { get; }

                    [JsonProperty("lastWipe")] public string LastWipe { get; }

                    public ServerMap(
                        string name,
                        uint size,
                        uint seed,
                        DateTime lastWipeDate
                    )
                    {
                        Name     = name;
                        Width    = Height = size;
                        Seed     = seed;
                        LastWipe = lastWipeDate.ToString("O").TrimEnd('Z');
                    }
                }

                public struct ServerPlayers
                {
                    [JsonProperty("online")] public int Online { get; }

                    [JsonProperty("max")] public int Max { get; }

                    public ServerPlayers(int online, int max)
                    {
                        Online = online;
                        Max    = max;
                    }
                }
            }
        }

#endregion

        private bool CanReceiveItem(BasePlayer player, Api.InventoryEntry inventoryEntry)
        {
            if ( inventoryEntry.Type == Api.InventoryEntry.EntryType.RANK ||
                 inventoryEntry.Type == Api.InventoryEntry.EntryType.RESEARCH ||
                 inventoryEntry.Type == Api.InventoryEntry.EntryType.CUSTOM )
            {
                return true;
            }

            int availableSpace =
            (player.inventory.containerMain.capacity -
             player.inventory.containerMain.itemList.Count) +
            (player.inventory.containerBelt.capacity -
             player.inventory.containerBelt.itemList.Count);

            switch (inventoryEntry.Type)
            {
                case Api.InventoryEntry.EntryType.ITEM:
                    return availableSpace > 0;
                case Api.InventoryEntry.EntryType.KIT:
                    return availableSpace >= inventoryEntry.Items.Count;
                default:
                    return false;
            }
        }

        private static class PlayerMessages
        {
            public const string CHAT_PREFIX          = "m.chatprefix";
            public const string NO_PERMISSION        = "m.nopermission";
            public const string INV_EMPTY            = "m.inv.empty";
            public const string ERROR_LOADING_ITEMS  = "m.error.itemload";
            public const string ERROR_RECEIVING_ITEM = "m.error.receiveitem";
            public const string ITEM_RECEIVED        = "m.itemreceived";
        }

        private class Ui : MonoBehaviour
        {
            private BasePlayer               _player;
            private State                    _state;
            private List<Api.InventoryEntry> _inventory;
            private int                      _currentPageIndex;

            private int PageCount => GetPageCount(_inventory.Count);

            private static int GetPageCount(int itemCount)
            {
                return itemCount / Builder.ITEMS_PER_PAGE +
                       (itemCount % Builder.ITEMS_PER_PAGE == 0 ? 0 : 1);
            }

#region Unity event functions

            private void Start()
            {
                _inventory = new List<Api.InventoryEntry>();
                _player    = GetComponent<BasePlayer>();
            }

            private void OnDestroy() => gMonetize_Close();

#endregion

#region Command message handlers

            private void gMonetize_Open()
            {
                State_LoadingItems();

                Instance._api.GetInventory(
                    _player.UserIDString,
                    items => {
                        _inventory.Clear();
                        _inventory.AddRange(items);

                        if ( _inventory.IsEmpty() )
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
                    errorCode => {
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
                if ( _state == State.Closed )
                {
                    return;
                }

                CuiHelper.DestroyUi(_player, Builder.Names.Main.CONTAINER);
                _state = State.Closed;
            }

            private void gMonetize_NextPage()
            {
                if ( _state == State.Closed )
                {
                    Instance.LogWarning(nameof(gMonetize_NextPage) + " called while UI was closed");
                }

                bool hasNextPage = _currentPageIndex < PageCount - 1;

                if ( !hasNextPage )
                    return;

                State_ItemPageDisplay();
                RemoveCurrentPageItems();
                _currentPageIndex++;
                DisplayCurrentItemPage();
            }

            private void gMonetize_PrevPage()
            {
                if ( _state == State.Closed )
                {
                    Instance.LogWarning(nameof(gMonetize_PrevPage) + " called while UI was closed");
                }

                bool hasPrevPage = _currentPageIndex > 0;

                if ( !hasPrevPage )
                    return;

                State_ItemPageDisplay();
                RemoveCurrentPageItems();
                _currentPageIndex--;
                DisplayCurrentItemPage();
            }

            private void gMonetize_RedeemItem(string id)
            {
                int entryIndex = _inventory.FindIndex(x => x.Id == id);

                if ( entryIndex == -1 )
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
                    () => {
                        Api.InventoryEntry entry = _inventory[entryIndex];
                        GiveRedeemedItems(entry);
                        RemoveCurrentPageItems();

                        _inventory.RemoveAt(entryIndex);

                        if ( _inventory.Count == 0 )
                        {
                            _currentPageIndex = 0;
                            State_NoItems();
                        }
                        else
                        {
                            if ( _currentPageIndex >= PageCount )
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
                        entry.Items
                             .Select(
                                 i => ItemManager.CreateByItemID(
                                     i.ItemId,
                                     i.Amount,
                                     i.Meta.SkinId ?? 0
                                 )
                             )
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
                Item item = rustItem.ToItem();

                if ( item == null )
                {
                    Instance.LogError(
                        "Failed to create Item object from Api.RustItem[{0}:{1}]",
                        rustItem.Id,
                        rustItem.ItemId
                    );
                    return;
                }

                _player.GiveItem(item);
            }

            private void RedeemResearchItem(Api.Research research)
            {
                int            itemId         = research.ResearchId;
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
                    Builder.GetMainContainerNotification(
                        $"Failed to load items: {errorCode}",
                        "1 0 0 1"
                    )
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

                CuiHelper.AddUi(
                    _player,
                    Builder.GetMainContainerNotification("Loading items...", "1 1 1 1")
                );
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

                CuiHelper.AddUi(
                    _player,
                    Builder.GetMainContainerNotification("Inventory is empty =(", "1 1 1 1")
                );
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
                if ( _state == State.ItemPageDisplay )
                {
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.PREV);
                    CuiHelper.DestroyUi(_player, Builder.Names.Main.Button.NEXT);
                }

                bool hasPrevPage = _currentPageIndex > 0;
                bool hasNextPage = _currentPageIndex < PageCount - 1;

                CuiHelper.AddUi(_player, Builder.GetItemListButtons(hasPrevPage, hasNextPage));

                IEnumerable<Api.InventoryEntry> currentPageItems = _inventory
                .Skip(Builder.ITEMS_PER_PAGE * _currentPageIndex)
                .Take(Builder.ITEMS_PER_PAGE);

                CuiHelper.AddUi(
                    _player,
                    currentPageItems.Select(
                                        (item, index) => {
                                            int indexOnPage = index % Builder.ITEMS_PER_PAGE;

                                            bool canReceive = /*CanReceiveItem(item)*/
                                            Instance.CanReceiveItem(_player, item);
                                            string text = !canReceive ? "CANNOT REDEEM" : "REDEEM";

                                            IEnumerable<CuiElement> card =
                                            Builder.GetCard(indexOnPage, item);
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
                if ( _state == State.Closed )
                {
                    CuiHelper.AddUi(_player, Builder.GetMainContainer());
                }
            }

            private void RemoveCurrentPageItems()
            {
                IEnumerable<Api.InventoryEntry> currentPageItems = _inventory
                .Skip(Builder.ITEMS_PER_PAGE * _currentPageIndex)
                .Take(Builder.ITEMS_PER_PAGE);

                currentPageItems.Select(item => item.Id)
                                .ToList()
                                .ForEach(
                                    id => CuiHelper.DestroyUi(
                                        _player,
                                        Builder.Names.ItemList.Card(id).Container
                                    )
                                );
            }

            private int GetAvailableSlots()
            {
                int totalSlots = _player.inventory.containerMain.capacity +
                                 _player.inventory.containerBelt.capacity;
                int claimedSlots = _player.inventory.containerMain.itemList.Count +
                                   _player.inventory.containerBelt.itemList.Count;

                return totalSlots - claimedSlots;
            }

            private bool IsAvailableForResearch(Api.Research research)
            {
                int itemId = research.ResearchId;

                ItemDefinition itemDefinition = ItemManager.FindItemDefinition(itemId);

                if ( itemDefinition == null )
                {
                    Instance.LogWarning(
                        "Not found ItemDefinition for itemid {0} in IsAvailableForResearch",
                        itemId
                    );
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
                public const  int   COLUMN_COUNT   = 7;
                public const  int   ROW_COUNT      = 3;
                public const  int   ITEMS_PER_PAGE = COLUMN_COUNT * ROW_COUNT;
                private const float COLUMN_GAP     = .005f;
                private const float ROW_GAP        = .01f;

                private const string DEFAULT_ICON_URL =
                "https://api.gmonetize.ru/static/v2/image/plugin/icons/rust_94773.png";

                public static string GetRedeemingButton(string id)
                {
                    Names.ItemList.ItemListCard ncard = Names.ItemList.Card(id);

                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = ncard.Container,
                                Name   = ncard.RedeemButton,
                                Components = {
                                    new CuiButtonComponent { Color = "0.4 0.4 0.4 0.5" },
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
                                Name   = ncard.RedeemButtonLabel,
                                Components = {
                                    new CuiTextComponent {
                                        Text = "REDEEMING...",
                                        Align =
                                        TextAnchor.MiddleCenter,
                                        Color =
                                        "0.6 0.6 0.6 0.8"
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
                                Name   = Names.Main.CONTAINER,
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
                                Name   = Names.Main.Button.CLOSE,
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
                                Name   = Names.Main.Label.CLOSE_TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = Colors.Build(
                                            0.8f,
                                            0.8f,
                                            0.8f,
                                            0.5f
                                        ),
                                        Align    = TextAnchor.MiddleCenter,
                                        Text     = "CLOSE",
                                        FontSize = 15
                                    },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent     = Names.Main.CONTAINER,
                                Name       = Names.Main.CONTAINER + ":needscursor",
                                Components = { new CuiNeedsCursorComponent() }
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

                public static string GetItemListButtons(bool hasPrevPage, bool hasNextPage)
                {
                    const string COLOR_DISABLED = "0.4 0.4 0.4 0.5";
                    const string COLOR_ENABLED  = "0.7 0.7 0.7 0.35";
                    const string COLOR_TEXT     = "0.8 0.8 0.8 0.4";

                    string prevButtonColor = hasPrevPage ? COLOR_ENABLED : COLOR_DISABLED;
                    string nextButtonColor = hasNextPage ? COLOR_ENABLED : COLOR_DISABLED;

                    return CuiHelper.ToJson(
                        new List<CuiElement> {
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Button.PREV,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = prevButtonColor,
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
                                Name   = Names.Main.Button.PREV + ":text",
                                Components = {
                                    new CuiTextComponent {
                                        Text = "PREVIOUS",
                                        Align =
                                        TextAnchor.MiddleCenter,
                                        Color    = COLOR_TEXT,
                                        FontSize = 12
                                    },
                                    GetTransform()
                                }
                            },
                            new CuiElement {
                                Parent = Names.Main.CONTAINER,
                                Name   = Names.Main.Button.NEXT,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = nextButtonColor,
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
                                Name   = Names.Main.Button.NEXT + ":text",
                                Components = {
                                    new CuiTextComponent {
                                        Text = "NEXT",
                                        Align =
                                        TextAnchor.MiddleCenter,
                                        Color    = COLOR_TEXT,
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
                                Name =
                                Names.ItemList.CONTAINER,
                                Components = {
                                    new CuiImageComponent { Color = "0.4 0.4 0.4 0.5" },
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

                    CuiElement button = new CuiElement {
                        Parent = Names.ItemList.Card(cardId).Container,
                        Name   = Names.ItemList.Card(cardId).RedeemButton,
                        Components = {
                            new CuiButtonComponent {
                                Command = isDisabled ? null : command,
                                Color   = buttonColor
                            },
                            buttonTransform
                        }
                    };

                    CuiElement buttonLabel = new CuiElement {
                        Parent = Names.ItemList.Card(cardId).RedeemButton,
                        Name   = Names.ItemList.Card(cardId).RedeemButtonLabel,
                        Components = {
                            new CuiTextComponent {
                                Text  = text,
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

                public static IEnumerable<CuiElement> GetCard(
                    int indexOnPage,
                    Api.InventoryEntry inventoryEntry
                )
                {
                    Names.ItemList.ItemListCard ncard = Names.ItemList.Card(inventoryEntry.Id);

                    CuiElementContainer container = new CuiElementContainer {
                        new CuiElement {
                            Parent = Names.ItemList.CONTAINER,
                            Name   = ncard.Container,
                            Components = {
                                new CuiImageComponent { Color = "0.4 0.4 0.4 0.6" },
                                GetGridTransform(indexOnPage)
                            }
                        },
                        new CuiElement {
                            Parent = ncard.Container,
                            Name   = ncard.InnerContainer,
                            Components = {
                                new CuiImageComponent { Color = "0 0 0 0" },
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
                            Name   = ncard.NameLabel + ":container",
                            Components = {
                                new CuiImageComponent { Color = "0.7 0.7 0.7 0.25" },
                                GetTransform(yMin: 0.91f, yMax: 1f)
                            }
                        },
                        new CuiElement {
                            Parent = ncard.NameLabel + ":container",
                            Name   = ncard.NameLabel,
                            Components = {
                                new CuiTextComponent {
                                    Text = string.IsNullOrEmpty(inventoryEntry.Name)
                                           ? "ITEM"
                                           : inventoryEntry.Name,
                                    Align = TextAnchor.MiddleCenter,
                                    Color = "0.8 0.8 0.8 0.4"
                                },
                                GetTransform()
                            }
                        }
                    };
                    CuiElement iconEl = new CuiElement {
                        Parent     = ncard.InnerContainer,
                        Name       = ncard.Icon,
                        Components = { GetTransform() }
                    };

                    LogDebug(
                        "Iconid for good {0}: {1}",
                        inventoryEntry.Name,
                        inventoryEntry.IconId
                    );
                    if ( string.IsNullOrEmpty(inventoryEntry.IconId) )
                    {
                        switch (inventoryEntry.Type)
                        {
                            case Api.InventoryEntry.EntryType.ITEM:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiImageComponent {
                                        ItemId = inventoryEntry.Item.ItemId,
                                        Color  = "1 1 1 0.8"
                                    }
                                );
                                break;

                            case Api.InventoryEntry.EntryType.KIT:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiImageComponent {
                                        ItemId = inventoryEntry.Items[0].ItemId,
                                        Color  = "1 1 1 0.8"
                                    }
                                );
                                break;

                            default:
                                iconEl.Components.Insert(
                                    0,
                                    new CuiRawImageComponent {
                                        Url   = DEFAULT_ICON_URL,
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
                            new CuiRawImageComponent {
                                Url = Instance._api.GetIconUrl(inventoryEntry.IconId)
                            }
                        );
                    }

                    container.Add(iconEl);

                    return container;
                }

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

                public static class Names
                {
                    public static class MainContainer
                    {
                        public const string SELF = "gmonetize.mainContainer";

                        public static class HeaderContainer
                        {
                            public const string SELF = MainContainer.SELF + ".headerContainer";

                            public static class CloseButton
                            {
                                public const string SELF = HeaderContainer.SELF + ".closeButton";
                                public const string TEXT = SELF + ":text";
                            }

                            public static class PaginationButtonsContainer
                            {
                                public const string SELF =
                                HeaderContainer.SELF + ".paginationButtonsContainer";

                                public static class Prev
                                {
                                    public const string SELF =
                                    PaginationButtonsContainer.SELF + ".prev";

                                    public const string TEXT = SELF + ":text";
                                }

                                public static class Next
                                {
                                    public const string SELF =
                                    PaginationButtonsContainer.SELF + ".next";

                                    public const string TEXT = SELF + ":text";
                                }
                            }
                        }

                        public static class ItemListContainer
                        {
                            public const string SELF = MainContainer.SELF + ".itemListContainer";

                            /*
                             * Note on this implementation:
                             * I considered caching of ItemCards in an ID=>ItemCard map,
                             * but this would require use of some (probably sophisticated) cleaning algorithm,
                             * since card id's are all UUIDS, which are practically unique,
                             * which would overcomplexify the code. So structs it is.
                             */

                            public static ItemCard Card(string id) => new ItemCard(id);

                            public struct ItemCard
                            {
                                public string Self { get; }
                                public ItemCardHeaderContainer Header { get; }
                                public ItemCardCenterContainer Center { get; }
                                public ItemCardFooterContainer Footer { get; }

                                public ItemCard(string id)
                                {
                                    // ReSharper disable once ArrangeStaticMemberQualifier // for clarity
                                    Self   = ItemListContainer.SELF + $".itemCard[{id}]";
                                    Header = default(ItemCardHeaderContainer);
                                    Center = default(ItemCardCenterContainer);
                                    Footer = default(ItemCardFooterContainer);

                                    Header = new ItemCardHeaderContainer(this);
                                    Center = new ItemCardCenterContainer(this);
                                    Footer = new ItemCardFooterContainer(this);
                                }

                                public struct ItemCardHeaderContainer
                                {
                                    public string Self { get; }

                                    public string ItemType { get; }
                                    public string ItemName { get; }

                                    public ItemCardHeaderContainer(ItemCard card)
                                    {
                                        Self     = card.Self + ".headerContainer";
                                        ItemType = Self + ".itemType";
                                        ItemName = Self + ".itemName";
                                    }
                                }

                                public struct ItemCardCenterContainer
                                {
                                    public string Self { get; }
                                    public string Image { get; }
                                    public string ConditionBar { get; }
                                    public string Amount { get; }

                                    public ItemCardCenterContainer(ItemCard card)
                                    {
                                        Self         = card.Self + ".centerContainer";
                                        Image        = Self + ".image";
                                        ConditionBar = Self + ".conditionBar";
                                        Amount       = Self + ".amount";
                                    }
                                }

                                public struct ItemCardFooterContainer
                                {
                                    public string Self { get; }
                                    public ItemCardButton Button { get; }

                                    public ItemCardFooterContainer(ItemCard card)
                                    {
                                        Self   = card.Self + ".bottomContainer";
                                        Button = default(ItemCardButton);
                                        Button = new ItemCardButton(this);
                                    }

                                    public struct ItemCardButton
                                    {
                                        public string Self { get; }
                                        public string Text { get; }

                                        public ItemCardButton(
                                            ItemCardFooterContainer footerContainer
                                        )
                                        {
                                            Self = footerContainer.Self + ".button";
                                            Text = Self + ":text";
                                        }
                                    }
                                }
                            }
                        }

                        public static class NotificationContainer
                        {
                            public const string SELF =
                            MainContainer.SELF + ".notificationContainer";

                            public static class HeaderContainer
                            {
                                public const string SELF =
                                NotificationContainer.SELF + ".headerContainer";

                                public const string TITLE = SELF + ".title";
                            }

                            public static class MessageContainer
                            {
                                public const string SELF =
                                NotificationContainer.SELF + ".messageContainer";

                                public const string MESSAGE = SELF + ".message";

                                public static class Button
                                {
                                    public const string SELF = MessageContainer.SELF + ".button";
                                    public const string TEXT = SELF + ":text";
                                }
                            }
                        }
                    }
                }

                private static class Materials
                {
                    public const string BLUR = "assets/content/ui/uibackgroundblur.mat";
                }

                public struct RustColor
                {
                    /*x00 -> x99 goes from darkest to lightest*/

                    public static readonly RustColor White   = new RustColor(1f);
                    public static readonly RustColor Black   = new RustColor(0f);
                    public static readonly RustColor Transp  = new RustColor(0, 0f);
                    public static readonly RustColor Bg00    = new RustColor(0.3f);
                    public static readonly RustColor Bg01    = new RustColor(0.4f);
                    public static readonly RustColor Bg02    = new RustColor(0.5f);
                    public static readonly RustColor Bg03    = new RustColor(0.6f);
                    public static readonly RustColor Fg00    = new RustColor(0.7f);
                    public static readonly RustColor Fg01    = new RustColor(0.8f);
                    public static readonly RustColor Fg02    = new RustColor(0.85f);
                    public static readonly RustColor Fg03    = new RustColor(0.9f);
                    public static readonly RustColor Success = new RustColor(0.2f, 0.8f, 0.3f);
                    public static readonly RustColor Warn    = new RustColor(0.7f, 0.6f, 0.2f);
                    public static readonly RustColor Error   = new RustColor(0.8f, 0.3f, 0.2f);

                    public readonly float Red,
                                          Green,
                                          Blue,
                                          Alpha;

                    private string _serialized;

                    public RustColor(
                        float red,
                        float green,
                        float blue,
                        float alpha = 1f
                    )
                    {
                        NormalizeRange(ref red);
                        NormalizeRange(ref green);
                        NormalizeRange(ref blue);
                        NormalizeRange(ref alpha);
                        Red         = red;
                        Green       = green;
                        Blue        = blue;
                        Alpha       = alpha;
                        _serialized = null;
                    }

                    public RustColor(float cChannels, float alpha = 1f) : this(
                        cChannels,
                        cChannels,
                        cChannels,
                        alpha
                    ) { }

                    public RustColor(
                        byte red,
                        byte green,
                        byte blue,
                        float alpha = 1f
                    ) : this(
                        NormalizeByte(red),
                        NormalizeByte(green),
                        NormalizeByte(blue),
                        alpha
                    ) { }

                    public RustColor(byte cChannels, float alpha = 1f) : this(
                        cChannels,
                        cChannels,
                        cChannels,
                        alpha
                    ) { }

                    public static implicit operator string(RustColor rc) => rc.ToString();

                    public static float NormalizeByte(byte b) => b / (float) byte.MaxValue;

                    private static void NormalizeRange(ref float value)
                    {
                        value = Math.Max(Math.Min(value, 1f), 0);
                    }

                    public override string ToString() =>
                    _serialized ?? (_serialized = $"{Red} {Green} {Blue} {Alpha}");

                    [Pure]
                    public RustColor With(
                        float? r = null,
                        float? g = null,
                        float? b = null,
                        float? a = null
                    )
                    {
                        return new RustColor(
                            r.GetValueOrDefault(Red),
                            g.GetValueOrDefault(Green),
                            b.GetValueOrDefault(Blue),
                            a.GetValueOrDefault(Alpha)
                        );
                    }

                    [Pure]
                    public RustColor With(
                        byte? r = null,
                        byte? g = null,
                        byte? b = null,
                        float? a = null
                    )
                    {
                        float fR,
                              fG,
                              fB;

                        fR = r.HasValue ? NormalizeByte(r.Value) : Red;
                        fG = g.HasValue ? NormalizeByte(g.Value) : Green;
                        fB = b.HasValue ? NormalizeByte(b.Value) : Green;

                        return new RustColor(
                            fR,
                            fG,
                            fB,
                            a.GetValueOrDefault(Alpha)
                        );
                    }

                    [Pure]
                    public RustColor WithAlpha(float alpha) => new RustColor(
                        Red,
                        Green,
                        Blue,
                        alpha
                    );

                    public static class ComponentColors
                    {
                        public static readonly RustColor PanelBase = Bg01.WithAlpha(0.4f);
                        public static readonly RustColor PanelDark = Bg02.WithAlpha(0.4f);

                        public static readonly RustColor ButtonError = Error.WithAlpha(0.3f);

                        public static readonly RustColor TextWhite = Fg01.WithAlpha(0.8f);

                        public static readonly RustColor ButtonDefault  = Bg02;
                        public static readonly RustColor ButtonDisabled = Bg00;
                    }
                }

                public static class ComponentBuilder
                {
                    private static readonly
                    Dictionary<ValueTuple<float, float, float, float>, CuiRectTransformComponent>
                    s_RectTransformCache =
                    new Dictionary<ValueTuple<float, float, float, float>,
                        CuiRectTransformComponent>();

                    /// <summary>
                    /// Builds main container along with it's header and close button
                    /// </summary>
                    /// <returns></returns>
                    public static IEnumerable<CuiElement> MainContainer()
                    {
                        return new[] {
                            /*main container*/
                            new CuiElement {
                                Parent = "Hud",
                                Name   = Names.MainContainer.SELF,
                                Components = {
                                    new CuiImageComponent {
                                        Color    = RustColor.ComponentColors.PanelBase,
                                        Material = Materials.BLUR
                                    },
                                    GetTransform()
                                }
                            },
                            /*header container*/
                            new CuiElement {
                                Parent = Names.MainContainer.SELF,
                                Name   = Names.MainContainer.HeaderContainer.SELF,
                                Components = {
                                    new CuiImageComponent { Color = RustColor.Transp },
                                    GetTransform(yMin: 0.9f)
                                }
                            },
                            /*close button*/
                            new CuiElement {
                                Parent = Names.MainContainer.HeaderContainer.SELF,
                                Name   = Names.MainContainer.HeaderContainer.CloseButton.SELF,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = RustColor.ComponentColors.ButtonError,
                                        Command = CMD_CLOSE
                                    },
                                    GetTransform()
                                }
                            },
                            /*close button text*/
                            new CuiElement {
                                Parent = Names.MainContainer.HeaderContainer.CloseButton.SELF,
                                Name   = Names.MainContainer.HeaderContainer.CloseButton.TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Text  = "CLOSE",
                                        Color = RustColor.ComponentColors.TextWhite
                                    },
                                    GetTransform()
                                }
                            },
                            /*pagination buttons container*/
                            new CuiElement {
                                Parent = Names.MainContainer.HeaderContainer.SELF,
                                Name = Names.MainContainer.HeaderContainer
                                            .PaginationButtonsContainer
                                            .SELF,
                                Components = { }
                            }
                        };
                    }

                    public static IEnumerable<CuiElement> PaginationButtons(
                        bool hasPrev,
                        bool hasNext
                    )
                    {
                        RustColor prevBtnColor =
                                  hasPrev
                                  ? RustColor.ComponentColors.ButtonDefault
                                  : RustColor.ComponentColors.ButtonDisabled,
                                  nextBtnColor =
                                  hasNext
                                  ? RustColor.ComponentColors.ButtonDefault
                                  : RustColor.ComponentColors.ButtonDisabled;

                        return new[] {
                            /*prev pagination btn*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.SELF,
                                Name = Names.MainContainer.HeaderContainer
                                            .PaginationButtonsContainer.Prev.SELF,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = prevBtnColor,
                                        Command = CMD_PREVP
                                    },
                                    GetTransform(xMax: 0.48f)
                                }
                            },
                            /*prev pagination btn text*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev
                                     .SELF,
                                Name =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev
                                     .TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color    = RustColor.ComponentColors.TextWhite,
                                        Text     = "PREVIOUS",
                                        Align    = TextAnchor.MiddleCenter,
                                        FontSize = 16
                                    },
                                    GetTransform()
                                }
                            },
                            /*next pagination btn*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.SELF,
                                Name = Names.MainContainer.HeaderContainer
                                            .PaginationButtonsContainer.Next.SELF,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = nextBtnColor,
                                        Command = CMD_NEXTP
                                    },
                                    GetTransform(xMin: 0.52f)
                                }
                            },
                            /*next pagination btn text*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next
                                     .SELF,
                                Name =
                                Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next
                                     .TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color    = RustColor.ComponentColors.TextWhite,
                                        Text     = "NEXT",
                                        Align    = TextAnchor.MiddleCenter,
                                        FontSize = 18
                                    },
                                    GetTransform()
                                }
                            }
                        };
                    }

                    public static IEnumerable<CuiElement> Notification(string text)
                    {
                        return new[] {
                            /*notification container*/
                            new CuiElement {
                                Parent = Names.MainContainer.SELF,
                                Name   = Names.MainContainer.NotificationContainer.SELF,
                                Components = {
                                    new CuiImageComponent {
                                        Color = RustColor.ComponentColors.PanelBase,
                                    },
                                    GetTransform(
                                        xMin: 0.2f,
                                        xMax: 0.8f,
                                        yMin: 0.3f,
                                        yMax: 0.7f
                                    )
                                }
                            },
                            /*notification header container*/
                            new CuiElement {
                                Parent = Names.MainContainer.NotificationContainer.SELF,
                                Name = Names.MainContainer.NotificationContainer.HeaderContainer
                                            .SELF,
                                Components = {
                                    new CuiImageComponent { Color = RustColor.Transp },
                                    GetTransform(yMin: 0.9f)
                                }
                            },
                            /*notification title*/
                            new CuiElement {
                                Parent = Names.MainContainer.NotificationContainer.HeaderContainer
                                              .SELF,
                                Name = Names.MainContainer.NotificationContainer.HeaderContainer
                                            .TITLE,
                                Components = {
                                    new CuiTextComponent { Text = "NOTIFICATION" },
                                    GetTransform()
                                }
                            },
                            /*notification message container*/
                            new CuiElement {
                                Parent = Names.MainContainer.NotificationContainer.SELF,
                                Name = Names.MainContainer.NotificationContainer.MessageContainer
                                            .SELF,
                                Components = {
                                    new CuiImageComponent {
                                        Color = RustColor.ComponentColors.PanelDark
                                    },
                                    GetTransform(yMax: 0.9f)
                                }
                            },
                            /*notification message text*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.NotificationContainer.MessageContainer.SELF,
                                Name =
                                Names.MainContainer.NotificationContainer.MessageContainer.MESSAGE,
                                Components = {
                                    new CuiTextComponent {
                                        Color = RustColor.ComponentColors.TextWhite,
                                        Text  = text
                                    },
                                    GetTransform(yMin: 0.3f)
                                }
                            },
                            /*notification message dismiss btn*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.NotificationContainer.MessageContainer.SELF,
                                Name =
                                Names.MainContainer.NotificationContainer.MessageContainer.Button
                                     .SELF,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = RustColor.ComponentColors.ButtonError,
                                        Command = "gmonetize.dismiss_notification"
                                    },
                                    GetTransform(yMax: 0.3f)
                                }
                            },
                            /*notification message dismiss btn text*/
                            new CuiElement {
                                Parent =
                                Names.MainContainer.NotificationContainer.MessageContainer.Button
                                     .SELF,
                                Name =
                                Names.MainContainer.NotificationContainer.MessageContainer.Button
                                     .TEXT,
                                Components = {
                                    new CuiTextComponent {
                                        Color = RustColor.ComponentColors.TextWhite,
                                        Text  = "DISMISS"
                                    },
                                    GetTransform()
                                }
                            }
                        };
                    }

                    public static List<CuiElement> ItemCard(
                        string id,
                        CuiRectTransformComponent transform,
                        InventoryEntryResponse inventoryEntry
                    )
                    {
                        Names.MainContainer.ItemListContainer.ItemCard nCard =
                        new Names.MainContainer.ItemListContainer.ItemCard(id);

                        List<CuiElement> componentList = Facepunch.Pool.GetList<CuiElement>();

                        /*root*/
                        componentList.Add(
                            new CuiElement {
                                Parent = Names.MainContainer.ItemListContainer.SELF,
                                Name   = nCard.Self,
                                Components = {
                                    new CuiImageComponent {
                                        Color = RustColor.ComponentColors.PanelBase,
                                    },
                                    transform
                                }
                            }
                        );
                        /*header container*/
                        componentList.Add(
                            new CuiElement {
                                Parent     = nCard.Self,
                                Name       = nCard.Header.Self,
                                Components = { }
                            }
                        );
                        /*listing name*/
                        componentList.Add(
                            new CuiElement {
                                Parent     = nCard.Header.Self,
                                Name       = nCard.Header.ItemName,
                                Components = { }
                            }
                        );

#if F_UI_LISTINGTYPE
                        /*listing type*/
                        componentList.Add(new CuiElement {
                            Parent = nCard.Header.Self,
                            Name = nCard.Header.ItemType,
                            Components = {  }
                        })
#endif
                        /*center container*/
                        componentList.Add(
                            new CuiElement {
                                Parent     = nCard.Self,
                                Name       = nCard.Center.Self,
                                Components = { }
                            }
                        );
                        /*item image*/
                        componentList.Add(
                            new CuiElement {
                                Parent = nCard.Center.Self,
                                Name   = nCard.Center.Image,
                            }
                        );
                        /*item amount*/
                        componentList.Add(
                            new CuiElement {
                                Parent = nCard.Center.Image,
                                Name   = nCard.Center.Amount
                            }
                        );
                        /*conditionBar*/
                        componentList.Add(
                            new CuiElement {
                                Parent     = nCard.Center.Image,
                                Name       = nCard.Center.ConditionBar,
                                Components = { }
                            }
                        );
                        /*footer container*/
                        componentList.Add(
                            new CuiElement {
                                Parent     = nCard.Self,
                                Name       = nCard.Footer.Self,
                                Components = { }
                            }
                        );

                        AddRedeemButton(
                            ref nCard,
                            componentList,
                            inventoryEntry.Id,
                            true // FIXME
                        );
#if F_UI_CONDITIONBAR
                        AddConditionBar(ref nCard, componentList, 1f );
#endif

                        return componentList;
                    }

                    private static void AddConditionBar(
                        ref Names.MainContainer.ItemListContainer.ItemCard nCard,
                        List<CuiElement> componentList,
                        float condition
                    )
                    {
                        componentList.Add(
                            new CuiElement {
                                Parent = nCard.Center.Image,
                                Name   = nCard.Center.ConditionBar,
                                Components = {
                                    new CuiImageComponent {
                                        Color = RustColor.Error.WithAlpha(0.1f)
                                    },
                                    GetTransform(yMax: Mathf.Clamp01(condition))
                                }
                            }
                        );
                    }

                    private static void AddRedeemButton(
                        ref Names.MainContainer.ItemListContainer.ItemCard nCard,
                        List<CuiElement> componentList,
                        string id,
                        bool isRedeemAvailable
                    )
                    {
                        RustColor btnColor;
                        string    btnText;

                        if ( isRedeemAvailable )
                        {
                            btnColor = RustColor.Success.WithAlpha(0.4f);
                            btnText  = "REDEEM";
                        }
                        else
                        {
                            btnColor = RustColor.ComponentColors.ButtonDisabled;
                            btnText  = "CANNOT REDEEM";
                        }

                        componentList.Add(
                            new CuiElement {
                                Parent = nCard.Footer.Self,
                                Name   = nCard.Footer.Button.Self,
                                Components = {
                                    new CuiButtonComponent {
                                        Color   = btnColor,
                                        Command = CMD_REDEEM + ' ' + id
                                    },
                                    GetTransform()
                                }
                            }
                        );

                        componentList.Add(
                            /*new CuiElement {
                                Parent = nCard.Footer.Button.Self,
                                Name   = nCard.Footer.Button.Text,
                                Components = {
                                    new CuiTextComponent {
                                        Color = RustColor.ComponentColors.TextWhite,
                                        Text  = btnText
                                    },
                                    GetTransform()
                                }
                            }*/
                            LabelBuilder.Create(nCard.Footer.Button.Self, nCard.Footer.Button.Text)
                                        .WithColor(RustColor.ComponentColors.TextWhite)
                                        .WithText(btnText)
                                        .FullSize()
                                        .ToCuiElement()
                        );
                    }

                    private static CuiRectTransformComponent GetGridTransform(
                        int cols,
                        int rows,
                        float colGap,
                        float rowGap,
                        int itemIndex
                    )
                    {
                        float totalColumnGap = colGap * (cols - 1);
                        float totalRowGap    = rowGap * (rows - 1);

                        float cardWidth  = (0.999f - totalColumnGap) / cols;
                        float cardHeight = (0.998f - totalRowGap) / rows;

                        int rowIndex    = itemIndex / cols;
                        int columnIndex = itemIndex % cols;

                        float columnGapSum = colGap * columnIndex;
                        float cardWidthSum = cardWidth * columnIndex;
                        float xPosition    = columnGapSum + cardWidthSum;

                        float rowGapSum     = rowGap * rowIndex;
                        float cardHeightSum = cardHeight * (rowIndex + 1);

                        float yPosition = 0.998f - (rowGapSum + cardHeightSum);

                        return GetTransform(
                            xPosition,
                            yPosition,
                            xPosition + cardWidth,
                            yPosition + cardHeight
                        );
                    }

                    private static CuiRectTransformComponent GetTransform(
                        float xMin = 0f,
                        float xMax = 1f,
                        float yMin = 0f,
                        float yMax = 1f
                    )
                    {
                        ValueTuple<float, float, float, float> key = ValueTuple.Create(
                            xMin,
                            yMin,
                            xMax,
                            yMax
                        );

                        CuiRectTransformComponent transform;

                        if ( !s_RectTransformCache.TryGetValue(key, out transform) )
                        {
                            transform = new CuiRectTransformComponent {
                                AnchorMin = $"{xMin} {yMin}",
                                AnchorMax = $"{xMax} {yMax}"
                            };

                            s_RectTransformCache[key] = transform;
                        }

                        return transform;
                    }

                    private struct LabelBuilder
                    {
                        public  string                    Parent;
                        public  string                    Name;
                        public  string                    Text;
                        public  RustColor                 TextColor;
                        public  TextAnchor                Align;
                        public  int                       FontSize;
                        private CuiRectTransformComponent Transform;

                        public LabelBuilder(
                            string parent,
                            string name,
                            string text,
                            RustColor textColor,
                            TextAnchor align,
                            int fontSize,
                            CuiRectTransformComponent transform
                        )
                        {
                            Name      = name;
                            Parent    = parent;
                            Text      = text;
                            TextColor = textColor;
                            Align     = align;
                            FontSize  = fontSize;
                            Transform = transform;
                        }

                        public static LabelBuilder Create(string parent, string name) =>
                        new LabelBuilder(
                            parent,
                            name,
                            string.Empty,
                            RustColor.White,
                            TextAnchor.MiddleCenter,
                            15,
                            GetTransform()
                        );

                        public LabelBuilder WithText(string text)
                        {
                            Text = text;
                            return this;
                        }

                        public LabelBuilder WithColor(RustColor color)
                        {
                            TextColor = color;
                            return this;
                        }

                        public LabelBuilder WithAlign(TextAnchor align)
                        {
                            Align = align;
                            return this;
                        }

                        public LabelBuilder Centered() => WithAlign(TextAnchor.MiddleCenter);
                        public LabelBuilder FromLeft() => WithAlign(TextAnchor.MiddleLeft);
                        public LabelBuilder FromRight() => WithAlign(TextAnchor.MiddleRight);

                        public LabelBuilder WithFontSize(int size)
                        {
                            FontSize = size;
                            return this;
                        }

                        public LabelBuilder WithTransform(CuiRectTransformComponent transform)
                        {
                            Transform = transform;
                            return this;
                        }

                        public LabelBuilder FullSize() => WithTransform(GetTransform());

                        public CuiElement ToCuiElement()
                        {
                            return new CuiElement {
                                Parent = Parent,
                                Name   = Name,
                                Components = {
                                    new CuiTextComponent {
                                        Color    = TextColor,
                                        Text     = Text,
                                        Align    = Align,
                                        FontSize = FontSize
                                    },
                                    Transform
                                }
                            };
                        }
                    }
                }
            }
        }

        private static class gAPI
        {
            private const string MAIN_API_PATH   = "/main/v3/plugin";
            private const string STATIC_API_PATH = "/static/v2";

            private static readonly JsonSerializerSettings s_SerializerSettings =
            new JsonSerializerSettings {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            private static gMonetize                  s_PluginInstance;
            private static Dictionary<string, string> s_RequestHeaders;

            public static bool IsReady => s_PluginInstance &&
                                          !string.IsNullOrEmpty(
                                              s_PluginInstance._configuration.ApiKey
                                          ) &&
                                          s_PluginInstance._configuration.ApiKey !=
                                          PluginConfiguration.GetDefault().ApiKey &&
                                          !string.IsNullOrEmpty(ApiBaseUrl) &&
                                          s_RequestHeaders != null;

            private static string ApiBaseUrl => Instance._configuration.ApiBaseUrl;
            private static WebRequests WebRequests => s_PluginInstance.webrequest;
            private static Dictionary<string, string> RequestHeaders => s_RequestHeaders;

            public static event Action<HeartbeatApiResult> OnHeartbeat;
            public static event Action<RedeemItemApiResult> OnRedeemItem;
            public static event Action<InventoryApiResult> OnReceiveInventory;

            public static void Init(gMonetize pluginInstance)
            {
                s_PluginInstance = pluginInstance;
                s_RequestHeaders = new Dictionary<string, string> {
                    { "Content-Type", "application/json" },
                    { "Authorization", "ApiKey " + s_PluginInstance._configuration.ApiKey }
                };
            }

            public static void SendHeartbeat(ServerHeartbeatRequest request)
            {
                string payloadJson = JsonConvert.SerializeObject(request);

                string url = GetHeartbeatUrl();

                WebRequests.Enqueue(
                    url,
                    payloadJson,
                    (code, body) => {
                        HeartbeatApiResult result = new HeartbeatApiResult(code, request);

                        if ( OnHeartbeat != null )
                        {
                            OnHeartbeat(result);
                        }
                    },
                    Instance,
                    RequestMethod.POST
                );
            }

            public static void GetInventory(
                string userId,
                ref IList inventory,
                Action handleSuccess = null,
                Action<int> handleFail = null
            ) { }

            public static void RedeemItem(
                string userId,
                string inventoryEntryId,
                Action handleSuccess,
                Action<int> handleFail
            ) { }

            private static string GetInventoryUrl(string userId)
            {
                return string.Concat(
                    ApiBaseUrl,
                    MAIN_API_PATH,
                    $"/customer/STEAM/{userId}/inventory"
                );
            }

            private static string GetRedeemUrl(string userId, string inventoryEntryId)
            {
                return string.Concat(
                    ApiBaseUrl,
                    MAIN_API_PATH,
                    $"/customer/STEAM/{userId}/inventory/{inventoryEntryId}/redeem"
                );
            }

            private static string GetHeartbeatUrl()
            {
                const string hbPath = "/server/ping";
                return string.Concat(ApiBaseUrl, MAIN_API_PATH, hbPath);
            }

            private static string GetIconUrl(string iconId)
            {
                const string imagePath = "/image/";
                return string.Concat(
                    ApiBaseUrl,
                    STATIC_API_PATH,
                    imagePath,
                    iconId
                );
            }

            private static bool IsSuccessStatusCode(int statusCode) =>
            statusCode >= 200 && statusCode < 300;
        }

        public abstract class ApiResult
        {
            public int StatusCode { get; }
            public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;

            public ApiResult(int statusCode)
            {
                StatusCode = statusCode;
            }
        }

        private class HeartbeatApiResult : ApiResult
        {
            public ServerHeartbeatRequest Request { get; }

            public HeartbeatApiResult(int statusCode, ServerHeartbeatRequest request) : base(
                statusCode
            )
            {
                Request = request;
            }
        }

        private class RedeemItemApiResult : ApiResult
        {
            public string UserId { get; }
            public string InventoryEntryId { get; }

            public RedeemItemApiResult(
                int statusCode,
                string userId,
                string inventoryEntryId
            ) : base(statusCode)
            {
                UserId           = userId;
                InventoryEntryId = inventoryEntryId;
            }
        }

        private class InventoryApiResult : ApiResult
        {
            public string UserId { get; }
            public List<object> Inventory { get; }

            public InventoryApiResult(int statusCode, string userId, List<object> inventory) : base(
                statusCode
            )
            {
                UserId    = userId;
                Inventory = inventory;
            }
        }

        private class ServerHeartbeatRequest
        {
            [JsonProperty("motd")] public string Description { get; }

            [JsonProperty("map")] public ServerMapRequest Map { get; }

            [JsonProperty("players")] public ServerPlayersRequest Players { get; }

            public ServerHeartbeatRequest(
                string description,
                ServerMapRequest map,
                ServerPlayersRequest players
            )
            {
                Description = description;
                Map         = map;
                Players     = players;
            }

            public class ServerMapRequest
            {
                [JsonProperty("name")] public string Name { get; }

                [JsonProperty("width")] public uint Width { get; }

                [JsonProperty("height")] public uint Height { get; }

                [JsonProperty("seed")] public uint Seed { get; }

                [JsonProperty("lastWipe")] public string LastWipe { get; }

                public ServerMapRequest(
                    string name,
                    uint size,
                    uint seed,
                    DateTime lastWipeDate
                )
                {
                    Name     = name;
                    Width    = Height = size;
                    Seed     = seed;
                    LastWipe = lastWipeDate.ToString("O").TrimEnd('Z');
                }
            }

            public class ServerPlayersRequest
            {
                [JsonProperty("online")] public int Online { get; }

                [JsonProperty("max")] public int Max { get; }

                public ServerPlayersRequest(int online, int max)
                {
                    Online = online;
                    Max    = max;
                }
            }
        }

        private class InventoryEntryResponse
        {
            public string Id { get; set; }
            public InventoryEntryType Type { get; set; }
            public string Name { get; set; }
            public string IconId { get; set; }
            public TimeSpan? WipeBlockDuration { get; set; }

            public RustItemResponse Item { get; set; }
            public List<RustItemResponse> Items { get; set; }
            public PermissionResponse Permission { get; set; }
            public GroupResponse Group { get; set; }
            public List<RoulettePrizeResponse> Prizes { get; set; }
        }

        private class RustItemResponse
        {
            public string ItemId { get; set; }
            public uint Amount { get; set; }
            public RustItemMetaResponse Meta { get; set; }

            public class RustItemMetaResponse
            {
                public ulong? SkinId { get; set; }
                public float Condition { get; set; }
            }
        }

        private class PermissionResponse
        {
            public string Value { get; set; }
            public TimeSpan Duration { get; set; }
        }

        private class GroupResponse
        {
            public string GroupName { get; set; }
            public TimeSpan Duration { get; set; }
        }

        private class RoulettePrizeResponse { }

        private enum InventoryEntryType
        {
            ITEM,
            KIT,
            PERMISSION,
            RANK,
            RESEARCH,
            ROULETTE
        }

        private enum GoodObjectType
        {
            ITEM, RANK, COMMAND, RESEARCH, PERMISSION
        }
    }
}
