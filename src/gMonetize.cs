#define UNITY_ASSERTIONS
// #define DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Network;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Pool = Facepunch.Pool;
using Server = ConVar.Server;

// ReSharper disable StringLiteralTypo
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Local

namespace Oxide.Plugins
{
    // ReSharper disable once ClassNeverInstantiated.Global
    [Info("gMonetize", "2CHEVSKII", "1.2.1")]
    public class gMonetize : CovalencePlugin
    {
        private const string PERM_USE = "gmonetize.use";

        private const string CMD_OPEN         = "gmonetize.open";
        private const string CMD_CLOSE        = "gmonetize.close";
        private const string CMD_NEXTP        = "gmonetize.nextpage";
        private const string CMD_PREVP        = "gmonetize.prevpage";
        private const string CMD_RETRY_LOAD   = "gmonetize.retry_load";
        private const string CMD_REDEEM       = "gmonetize.redeemitem";
        private const string CMD_RETRY_REDEEM = "gmonetize.retry_redeem";

        private static gMonetize                           Instance;
        private        PluginConfiguration                 _configuration;
        private        Timer                               _heartbeatTimer;
        private        Timer                               _checkTimedRanksTimer;
        private        Dictionary<string, List<TimedRank>> _timedRanks;

        #region Log helpers

        [Conditional("DEBUG")]
        private static void LogDebug(string message) => Interface.Oxide.LogDebug(message);

        private static void LogMessage(string format, params object[] args)
        {
            string message = string.Format(format, args);

            LogDebug(message);
            Instance?.LogToFile(
                "log",
                message,
                Instance,
                true,
                true
            );
        }

        #endregion

        #region Oxide hook handlers

        private void Init()
        {
            Instance = this;
            permission.RegisterPermission(PERM_USE, this);
            SetupCommands();

            foreach (string chatCommand in _configuration.ChatCommands)
                covalence.RegisterCommand(chatCommand, this, HandleCommand);
        }

        private void OnServerInitialized()
        {
            SetupServerTags();

            gAPI.Init(this);
            SetupAPICallbacks();
            StartSendingHeartbeats();

            LoadTimedRanks();
            StartCheckingTimedRanks();

            foreach (IPlayer player in players.Connected)
                OnUserConnected(player);
        }

        private void Unload()
        {
            StopCheckingTimedRanks();
            SaveTimedRanks();

            StopSendingHeartbeats();
            CleanupAPICallbacks();
            CleanupServerTags();

            foreach (IPlayer player in players.Connected)
                OnUserDisconnected(player);
        }

        private void OnUserConnected(IPlayer player) =>
            ((BasePlayer)player.Object).gameObject.AddComponent<Ui>();

        private void OnUserDisconnected(IPlayer player) =>
            UnityEngine.Object.Destroy(((BasePlayer)player.Object).GetComponent<Ui>());

        #endregion

        #region Setup/Teardown

        private void SetupCommands()
        {
            foreach (string commandName in from field in typeof(gMonetize).GetFields(
                                               BindingFlags.Static | BindingFlags.NonPublic
                                           )
                                           where field.Name.StartsWith("CMD_")
                                           select field.GetRawConstantValue() as string)
            {
                covalence.RegisterCommand(commandName, this, HandleCommand);
            }
        }

        private void StartCheckingTimedRanks()
        {
            _checkTimedRanksTimer = timer.Every(10, CheckExpiredTimedRanks);
        }

        private void StopCheckingTimedRanks()
        {
            _checkTimedRanksTimer.Destroy();
        }

        private void SetupAPICallbacks()
        {
            gAPI.OnHeartbeat += HandleOnHeartbeat;
            gAPI.OnReceiveInventory += HandleOnReceiveInventory;
            gAPI.OnRedeemItem += HandleOnRedeemItem;
        }

        private void CleanupAPICallbacks()
        {
            gAPI.OnHeartbeat -= HandleOnHeartbeat;
        }

        private void StartSendingHeartbeats() => (_heartbeatTimer = timer.Every(
            60f,
            () =>
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
        )).Callback();

        private void StopSendingHeartbeats() => _heartbeatTimer.Destroy();


        #region Server tags

        private void SetupServerTags()
        {
            if (!Server.tags.Contains("gmonetize"))
            {
                Server.tags = string.Join(
                    ",",
                    Server.tags.Split(new[] {","}, StringSplitOptions.RemoveEmptyEntries).Concat(new[] {"gmonetize"})
                );

                LogMessage("Added gmonetize tag to server tags");
                LogMessage("Server tags are now: {0}", Server.tags);
            }
        }

        private void CleanupServerTags()
        {
            if (Server.tags.Contains("gmonetize"))
            {
                Server.tags = string.Join(
                    ",",
                    Server.tags.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries).Except(new[] {"gmonetize"})
                );

                LogMessage("Removed gmonetize tags from server tags");
                LogMessage("Server tags are now: {0}", Server.tags);
            }
        }

        #endregion

        #endregion

        #region API callback handlers

        private void HandleOnHeartbeat(HeartbeatApiResult result)
        {
            if (!result.IsSuccess)
                LogMessage("HandleOnHeartbeat: Server heartbeat request failed with code {0}", result.StatusCode);
            else LogMessage("HandleOnHeartbeat: Server heartbeat was sent successfully");
        }

        private void HandleOnReceiveInventory(InventoryApiResult result)
        {
            LogMessage("HandleOnReceiveInventory: Inventory received for player {0}", result.UserId);

            IPlayer player = players.FindPlayerById(result.UserId);

            if (player == null)
            {
                LogMessage("HandleOnReceiveInventory: Failed to find player with id {0}", result.UserId);
                return;
            }

            if (!player.IsConnected)
            {
                LogMessage("HandleOnReceiveInventory: Player with id {0} is no longer connected", result.UserId);
                return;
            }

            BasePlayer basePlayer = (BasePlayer)player.Object;
            if (result.IsSuccess)
            {
                basePlayer.SendMessage(
                    "gMonetize_InventoryLoaded",
                    result.Inventory,
                    SendMessageOptions.RequireReceiver
                );
            }
            else
            {
                LogMessage(
                    "HandleOnReceiveInventory: Failed to receive inventory for player {0}, request failed with code {1}",
                    result.UserId,
                    result.StatusCode
                );
                basePlayer.SendMessage("gMonetize_InventoryLoadError", SendMessageOptions.RequireReceiver);
            }
        }

        private void HandleOnRedeemItem(RedeemItemApiResult result)
        {
            IPlayer player = players.FindPlayerById(result.UserId);

            if (player == null)
            {
                LogMessage("HandleOnRedeemItem: Failed to find player with userID {0}", result.UserId);
                return;
            }

            if (!player.IsConnected)
            {
                LogMessage("HandleOnRedeemItem: Player {0} is no longer connected to the server", player.Id);
            }

            BasePlayer basePlayer = (BasePlayer)player.Object;

            if (!result.IsSuccess)
            {
                LogMessage(
                    "HandleOnRedeemItem: RedeemItem({0}, {1}) failed: {2}",
                    result.InventoryEntryId,
                    result.UserId,
                    result.StatusCode
                );
                basePlayer.SendMessage("gMonetize_RedeemItemFailed", result.InventoryEntryId);
                return;
            }

            LogMessage(
                "HandleOnRedeemItem: RedeemItem({0}, {1}) is successful",
                result.InventoryEntryId,
                result.UserId
            );
            basePlayer.SendMessage("gMonetize_RedeemItemOk", result.InventoryEntryId);
        }

        #endregion

        #region Command handler

        private bool HandleCommand(IPlayer player, string command, string[] args)
        {
            if (player.IsServer)
            {
                player.Message("This command cannot be executed in server console");
                return true;
            }

            BasePlayer basePlayer = (BasePlayer)player.Object;

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
                    basePlayer.SendMessage("gMonetize_Close");
                    break;
                case CMD_NEXTP:
                    basePlayer.SendMessage("gMonetize_NextPage");
                    break;
                case CMD_PREVP:
                    basePlayer.SendMessage("gMonetize_PrevPage");
                    break;
                case CMD_REDEEM:
                    basePlayer.SendMessage("gMonetize_RedeemingItem", int.Parse(args[1]));
                    gAPI.RedeemItem(player.Id, args[0]);
                    break;
                case CMD_RETRY_LOAD:
                    basePlayer.SendMessage("gMonetize_InventoryLoading");
                    gAPI.GetInventory(player.Id);
                    break;
                case CMD_RETRY_REDEEM:
                    basePlayer.SendMessage("gMonetize_RedeemingItem", args[1]);
                    gAPI.RedeemItem(player.Id, args[0]);
                    break;
                default:
                    if (command == CMD_OPEN || _configuration.ChatCommands.Contains(command))
                    {
                        basePlayer.SendMessage("gMonetize_Open");
                        basePlayer.SendMessage("gMonetize_InventoryLoading");
                        gAPI.GetInventory(player.Id);
                    }

                    break;
            }

            return true;
        }

        private bool CheckPermission(IPlayer player) => player.HasPermission(PERM_USE);

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

                LogMessage("ApiKey: {0}", _configuration.ApiKey);
                LogMessage("Chat commands: {0}", string.Join(", ", _configuration.ChatCommands));
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

        #region Timed ranks

        private void LoadTimedRanks()
        {
            _timedRanks =
                Interface.Oxide.DataFileSystem
                         .ReadObject<Dictionary<string, List<TimedRank>>>("gmonetize.timedranks") ??
                new Dictionary<string, List<TimedRank>>();
        }

        private void SaveTimedRanks()
        {
            Interface.Oxide.DataFileSystem.WriteObject("gmonetize.timedranks", _timedRanks);
        }

        private void CheckExpiredTimedRanks()
        {
            foreach (KeyValuePair<string, List<TimedRank>> kv in _timedRanks.ToArray())
            {
                string userId = kv.Key;
                List<TimedRank> list = kv.Value;

                IPlayer player = players.FindPlayerById(userId);

                if (player == null)
                {
                    LogMessage("CheckExpiredTimedRanks(): Could not find player {0}", userId);
                    continue;
                }

                for (int i = list.Count - 1; i >= 0; i--)
                {
                    TimedRank rank = list[i];

                    bool hasExpired = rank.TimeLeft() <= TimeSpan.Zero;

                    if (!hasExpired)
                    {
                        continue;
                    }

                    if (rank.Type == TimedRank.RankType.Permission)
                    {
                        LogMessage("TimedRank expired: Removing permission {0} from player {1}", rank.Name, player.Id);
                        rank.Unset(player);
                    }
                    else
                    {
                        LogMessage("TimedRank expired: Removing player {0} from group {1}", player.Id, rank.Name);
                        rank.Unset(player);
                    }

                    list.RemoveAt(i);
                }

                if (list.Count == 0)
                {
                    _timedRanks.Remove(kv.Key);
                }
            }
        }

        #endregion

        #region CanReceiveItem helpers

        /*private bool CanRedeemItem(
            BasePlayer player,
            gAPI.InventoryEntry inventoryEntry,
            CannotRedeemItemReason? reason
        )
        {
            reason = null;
            switch (inventoryEntry.Type)
            {
                case gAPI.InventoryEntry.InventoryEntryType.ITEM:
                    ItemDefinition itemDef = inventoryEntry.Item.FindItemDefinition();
                    if (!itemDef)
                    {
                        reason = CannotRedeemItemReason.NoItemDefinition;
                        return false;
                    }

                    int availableSlots = GetAvailableInventorySlots(player);

                    int requiredSlots = Mathf.CeilToInt(inventoryEntry.Item.Amount / (float)itemDef.stackable);

                    return requiredSlots <= availableSlots;
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.KIT:
                    break;
            }

            return true; // FIXME
        }*/

        private int GetAvailableInventorySlots(BasePlayer player)
        {
            return GetAvailableItemContainerSlots(player.inventory.containerMain) +
                   GetAvailableItemContainerSlots(player.inventory.containerBelt);
        }

        private int GetAvailableItemContainerSlots(ItemContainer container)
        {
            return container.capacity - container.itemList.Count;
        }

        private enum CannotRedeemItemReason
        {
            NoItemDefinition,
            InventoryFull,
            ResearchComplete
        }

        private enum RedeemButtonState
        {
            InvalidItem,
            Redeeming,
            InventoryFull,
            ResearchComplete,
            Error,
            Available
        }

        #endregion

        #region Redeem handlers

        private void RedeemInventoryEntry(BasePlayer player, gAPI.InventoryEntry entry)
        {
            switch (entry.type)
            {
                case gAPI.InventoryEntry.InventoryEntryType.ITEM:
                    RedeemItem(player, entry.item);
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.KIT:
                    RedeemKitOrCustom(player, entry.contents);
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.RESEARCH:
                    RedeemResearch(player, entry.research);
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.RANK:
                    RedeemRankOrPermission(player, rankDto: entry.rank);
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.PERMISSION:
                    RedeemRankOrPermission(player, entry.permission);
                    break;
                case gAPI.InventoryEntry.InventoryEntryType.CUSTOM:
                    RedeemKitOrCustom(player, entry.contents);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void RedeemItem(BasePlayer player, gAPI.ItemDto itemDto)
        {
            ItemDefinition itemDef = itemDto.FindItemDefinition();

            if (!itemDef)
            {
                LogMessage("ItemDefinition({0}) not found", itemDto.itemId);
                return;
            }

            Item item = ItemManager.Create(itemDef, (int)itemDto.amount, itemDto.meta.skinId ?? 0ul);

            LogMessage("Giving item {0} to player {1}", itemDto.itemId, player.UserIDString);
            player.GiveItem(item);
        }

        private void RedeemResearch(BasePlayer player, gAPI.ResearchDto researchDto)
        {
            ItemDefinition itemDef = researchDto.FindItemDefinition();

            if (!itemDef)
            {
                LogMessage(
                    "ItemDefinition({0}) was not found while attempting to receive item by player {1}",
                    researchDto.researchId,
                    player.UserIDString
                );
                return;
            }

            if (player.blueprints.IsUnlocked(itemDef))
            {
                LogMessage("Player {0} already has unlocked research {1}", player.UserIDString, researchDto.researchId);
            }
            else
            {
                LogMessage("Unlocking item {0} for player {1}", researchDto.researchId, player.UserIDString);
                player.blueprints.Unlock(itemDef);
            }
        }

        private void RedeemRankOrPermission(
            BasePlayer player,
            gAPI.PermissionDto permissionDto = null,
            gAPI.RankDto rankDto = null
        )
        {
            TimedRank timedRank;
            if (permissionDto != null)
            {
                Debug.Assert(rankDto == null);
                timedRank = TimedRank.CreateFrom(permissionDto);
            }
            else
            {
                Debug.Assert(permissionDto == null);
                Debug.Assert(rankDto != null);

                timedRank = TimedRank.CreateFrom(rankDto);
            }

            List<TimedRank> ranksList;
            if (!_timedRanks.TryGetValue(player.UserIDString, out ranksList))
            {
                ranksList = new List<TimedRank>();
                _timedRanks.Add(player.UserIDString, ranksList);
            }

            TimedRank exRank = ranksList.Find(r => r.Name == timedRank.Name && r.Type == timedRank.Type);

            if (exRank == null)
            {
                LogMessage(
                    "Creating new TimedRank for player {0}: {1}/{2}",
                    player.UserIDString,
                    timedRank.Name,
                    timedRank.Duration
                );
                timedRank.Set(player.IPlayer);
                ranksList.Add(timedRank);
                return;
            }

            LogMessage(
                "Updating existing rank for player {0}: Adding {1} to duration of {2}",
                player.UserIDString,
                timedRank.Duration,
                exRank.Duration
            );
            exRank.Duration = exRank.Duration.Add(timedRank.Duration);
        }

        private void RedeemKitOrCustom(BasePlayer player, gAPI.GoodObjectImpl[] contents)
        {
            foreach (gAPI.GoodObjectImpl goodObjectImpl in contents)
            {
                switch (goodObjectImpl.type)
                {
                    case gAPI.GoodObjectImpl.GoodObjectType.ITEM:
                        RedeemItem(player, goodObjectImpl.ToItem());
                        break;
                    case gAPI.GoodObjectImpl.GoodObjectType.RANK:
                        RedeemRankOrPermission(player, rankDto: goodObjectImpl.ToRank());
                        break;
                    case gAPI.GoodObjectImpl.GoodObjectType.COMMAND:
                        RedeemCommand(player, goodObjectImpl.ToCommand());
                        break;
                    case gAPI.GoodObjectImpl.GoodObjectType.RESEARCH:
                        RedeemResearch(player, goodObjectImpl.ToResearch());
                        break;
                    case gAPI.GoodObjectImpl.GoodObjectType.PERMISSION:
                        RedeemRankOrPermission(player, goodObjectImpl.ToPermission());
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void RedeemCommand(BasePlayer player, gAPI.CommandDto commandDto)
        {
            string effectiveCommand = commandDto.value
                                                .Replace(
                                                    "${userId}",
                                                    player.UserIDString,
                                                    StringComparison.OrdinalIgnoreCase
                                                )
                                                .Replace(
                                                    "${userName}",
                                                    player.displayName,
                                                    StringComparison.OrdinalIgnoreCase
                                                );

            server.Command(effectiveCommand);
        }

        #endregion

        #region Public API

        private void gMonetize_GetCustomerBalance(string userId, Action<long> onBalanceReceived, Action<int> onError)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentNullException(nameof(userId), "Customer ID cannot be empty");
            }

            if (onBalanceReceived == null)
            {
                throw new ArgumentNullException(nameof(onBalanceReceived), "Balance received callback cannot be null");
            }

            gAPI.GetCustomerBalance(userId, onBalanceReceived, onError);
        }

        private void gMonetize_SetCustomerBalance(
            string userId,
            long value,
            Action onOk,
            Action<int> onError
        )
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                throw new ArgumentNullException(nameof(userId), "Customer ID cannot be empty");
            }

            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Balance value cannot be negative");
            }

            gAPI.SetCustomerBalance(
                userId,
                value,
                onOk,
                onError
            );
        }

        #endregion

        private class Ui : MonoBehaviour
        {
            private const string RPC_DESTROYUI  = "DestroyUI";
            private const string RPC_ADDUI      = "AddUI";
            private const int    COLUMN_COUNT   = 8;
            private const int    ROW_COUNT      = 3;
            private const int    CARDS_PER_PAGE = COLUMN_COUNT * ROW_COUNT;
            private const float  COLUMN_GAP     = .005f;
            private const float  ROW_GAP        = .01f;

            private BasePlayer                _player;
            private SendInfo                  _playerSendInfo;
            private List<gAPI.InventoryEntry> _inventory;

            private bool _mainWindowDrawn;

            private bool _notificationDrawn;
            private bool _itemListDrawn;

            private int _pageIndex;
            private int _renderedItemCards;

            private gAPI.InventoryEntry[] _currentPageItems;
            private bool                  _pageChanged;

            private bool IsInventoryEmpty => _inventory.Count == 0;

            private int PageCount => IsInventoryEmpty
                ? 0
                : _inventory.Count / CARDS_PER_PAGE + (_inventory.Count % CARDS_PER_PAGE == 0 ? 0 : 1);

            private gAPI.InventoryEntry[] CurrentPageItems
            {
                get
                {
                    if (_pageChanged)
                    {
                        _currentPageItems = _inventory.Skip(_pageIndex * CARDS_PER_PAGE).Take(CARDS_PER_PAGE).ToArray();
                        _pageChanged = false;
                    }

                    return _currentPageItems;
                }
            }

            #region Unity event functions

            private void Start()
            {
                _player = GetComponent<BasePlayer>();
                _playerSendInfo = new SendInfo(_player.Connection) {
                    priority = Priority.Normal,
                    method = SendMethod.Reliable,
                    channel = 0
                };
            }

            private void OnDestroy() => gMonetize_Close();

            #endregion

            #region Custom event handlers

            private void gMonetize_Open()
            {
                if (_mainWindowDrawn)
                {
                    return;
                }

                IEnumerable<CuiElement> ui = ComponentBuilder.MainContainer();

                SendAddUI(ui);

                _mainWindowDrawn = true;
            }

            private void gMonetize_Close()
            {
                if (!_mainWindowDrawn)
                {
                    return;
                }

                SendDestroyUI(Names.MainContainer.SELF);

                _notificationDrawn = false;
                _itemListDrawn = false;
                _mainWindowDrawn = false;
                _renderedItemCards = 0;
            }

            private void gMonetize_InventoryLoading()
            {
                HideItemList();
                HideNotification();

                List<CuiElement> componentList = Pool.GetList<CuiElement>();

                ComponentBuilder.Notification(componentList, NotificationType.LoadingItems);
                SendAddUI(componentList);
                Pool.FreeList(ref componentList);

                _notificationDrawn = true;
            }

            private void gMonetize_InventoryLoadError()
            {
                HideItemList();
                HideNotification();

                List<CuiElement> list = Pool.GetList<CuiElement>();
                ComponentBuilder.Notification(list, NotificationType.ItemsLoadError);
                SendAddUI(list);
                Pool.FreeList(ref list);

                _notificationDrawn = true;
            }

            private void gMonetize_InventoryLoaded(List<gAPI.InventoryEntry> items)
            {
                _inventory = items;

                HideNotification();

                _pageIndex = 0;

                OnInventoryUpdated();
                RenderItemListContainer();

                if (_inventory.Count != 0)
                {
                    RenderItemPage();
                }
            }

            private void gMonetize_RedeemingItem(int index)
            {
                Names.MainContainer.ItemListContainer.ItemCard
                    nCard = Names.MainContainer.ItemListContainer.Card(index);
                string button = nCard.Footer.Button.Self;

                gAPI.InventoryEntry entry = CurrentPageItems[index];

                SendDestroyUI(button);
                List<CuiElement> list = Pool.GetList<CuiElement>();
                ComponentBuilder.RedeemButton(
                    nCard,
                    index,
                    entry.id,
                    RedeemButtonState.Redeeming,
                    list
                );
                SendAddUI(list);
                Pool.FreeList(ref list);
            }

            private void gMonetize_NextPage()
            {
                bool isLastPage = _pageIndex == PageCount - 1;

                if (isLastPage)
                {
                    return;
                }

                _pageIndex++;
                _pageChanged = true;

                RenderPaginationButtons();
                RenderItemPage();
            }

            private void gMonetize_PrevPage()
            {
                bool isFirstPage = _pageIndex == 0;

                if (isFirstPage)
                {
                    return;
                }

                _pageIndex--;
                _pageChanged = true;

                RenderPaginationButtons();
                RenderItemPage();
            }

            private void gMonetize_RedeemItemOk(string entryId)
            {
                int entryIndex = _inventory.FindIndex(e => e.id == entryId);

                gAPI.InventoryEntry entry = _inventory[entryIndex];

                Instance.RedeemInventoryEntry(_player, entry);

                _inventory.RemoveAt(entryIndex);

                OnInventoryUpdated();
                RenderItemListContainer();

                // TODO: This is counterintuitive, needs rework
                if (_inventory.Count != 0)
                {
                    RenderItemPage();
                }
            }

            #endregion

            private void HideNotification()
            {
                if (!_notificationDrawn)
                    return;

                SendDestroyUI(Names.MainContainer.NotificationContainer.SELF);
                _notificationDrawn = false;
            }

            private void HideItemList()
            {
                if (!_itemListDrawn)
                    return;

                SendDestroyUI(Names.MainContainer.ItemListContainer.SELF);
                _itemListDrawn = false;
            }

            private void OnInventoryUpdated()
            {
                if (!_mainWindowDrawn)
                    return;

                if (_pageIndex >= PageCount)
                    _pageIndex = PageCount - 1;

                _pageChanged = true;

                if (_inventory.Count == 0)
                {
                    RenderNoItemsNotification();
                }
                else
                {
                    RenderPaginationButtons();
                }
            }

            private void RenderPaginationButtons()
            {
                List<CuiElement> componentList = Pool.GetList<CuiElement>();

                SendDestroyUI(Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev.SELF);
                SendDestroyUI(Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next.SELF);

                bool enablePrev = _pageIndex != 0;
                bool enableNext = _pageIndex != PageCount - 1;

                ComponentBuilder.PaginationButtons(enablePrev, enableNext, componentList);
                SendAddUI(componentList);

                Pool.FreeList(ref componentList);
            }

            private void RenderNoItemsNotification()
            {
                HideItemList();
                HideNotification();

                List<CuiElement> list = Pool.GetList<CuiElement>();
                ComponentBuilder.Notification(list, NotificationType.InventoryEmpty);
                SendAddUI(list);
                Pool.FreeList(ref list);

                _notificationDrawn = true;
            }

            private void RenderItemListContainer()
            {
                if (_itemListDrawn)
                {
                    return;
                }

                SendAddUI(ComponentBuilder.ItemListContainer());
                _itemListDrawn = true;
            }

            private void HideItemCards()
            {
                List<string> destroyList = Pool.GetList<string>();

                for (int i = 0; i < _renderedItemCards; i++)
                {
                    destroyList.Add(Names.MainContainer.ItemListContainer.Card(i).Self);
                }

                SendDestroyUI(destroyList);
                Pool.FreeList(ref destroyList);
            }

            private void RenderItemPage()
            {
                Debug.Log("RenderItemPage()");

                // this method should not be called
                // if inventory is empty
                // RenderNoItemsNotification() should be used instead
                Debug.Assert(_inventory.Count != 0);

                HideItemCards();

                List<CuiElement> componentList = Pool.GetList<CuiElement>();

                gAPI.InventoryEntry[] items = CurrentPageItems;

                for (int i = 0; i < items.Length; i++)
                {
                    ComponentBuilder.ItemCard(
                        i,
                        items[i],
                        RedeemButtonState.Available,
                        componentList
                    );
                }

                _renderedItemCards = items.Length;

                SendAddUI(componentList);

                Pool.FreeList(ref componentList);
            }

            private enum NotificationType
            {
                LoadingItems,
                ItemsLoadError,
                InventoryEmpty
            }

            #region UI RPC helpers

            private string SerializeUI(IEnumerable<CuiElement> elements)
            {
                /*FIXMEPLS*/
                List<CuiElement> list = Pool.GetList<CuiElement>();
                list.AddRange(elements);

                string json = CuiHelper.ToJson(list);
                Pool.FreeList(ref list);
                return json;
            }

            private void SendAddUI(CuiElement element)
            {
                List<CuiElement> tempList = Pool.GetList<CuiElement>();
                tempList.Add(element);
                SendAddUI(tempList);
                Pool.FreeList(ref tempList);
            }

            private void SendAddUI(IEnumerable<CuiElement> elements)
            {
                SendAddUI(SerializeUI(elements));
            }

            private void SendAddUI(string uiJson)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_ADDUI,
                    uiJson
                );
            }

            private void SendDestroyUI(string elementName)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    elementName
                );
            }

            private void SendDestroyUI(string en0, string en1)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    en0
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    en1
                );
            }

            private void SendDestroyUI(string en0, string en1, string en2)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    en0
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    en1
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _playerSendInfo,
                    null,
                    RPC_DESTROYUI,
                    en2
                );
            }

            private void SendDestroyUI(List<string> elementNames)
            {
                // ReSharper disable once ForCanBeConvertedToForeach
                for (int i = 0; i < elementNames.Count; i++)
                {
                    SendDestroyUI(elementNames[i]);
                }
            }

            #endregion

            #region Name helper

            [SuppressMessage("ReSharper", "MemberHidesStaticFromOuterClass")]
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
                            public const string SELF = HeaderContainer.SELF + ".paginationButtonsContainer";

                            public static class Prev
                            {
                                public const string SELF = PaginationButtonsContainer.SELF + ".prev";

                                public const string TEXT = SELF + ":text";
                            }

                            public static class Next
                            {
                                public const string SELF = PaginationButtonsContainer.SELF + ".next";

                                public const string TEXT = SELF + ":text";
                            }
                        }
                    }

                    public static class ItemListContainer
                    {
                        public const string SELF = MainContainer.SELF + ".itemListContainer";

                        // ReSharper disable once CommentTypo
                        /*
                         * Note on this implementation:
                         * I considered caching of ItemCards in an ID=>ItemCard map,
                         * but this would require use of some (probably sophisticated) cleaning algorithm,
                         * since card id's are all UUIDS, which are practically unique,
                         * which would overcomplexify the code. So structs it is.
                         */

                        public static ItemCard Card(int index) => new ItemCard(index);

                        public struct ItemCard
                        {
                            public string Self { get; }
                            public ItemCardHeaderContainer Header { get; }
                            public ItemCardCenterContainer Center { get; }
                            public ItemCardFooterContainer Footer { get; }

                            public ItemCard(int index)
                            {
                                // ReSharper disable once ArrangeStaticMemberQualifier // for clarity
                                Self = SELF + $".itemCard[{index}]";

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

                                // ReSharper disable once UnusedAutoPropertyAccessor.Local
                                public string ItemType { get; }
                                public string ItemName { get; }

                                public ItemCardHeaderContainer(ItemCard card)
                                {
                                    Self = card.Self + ".headerContainer";
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
                                    Self = card.Self + ".centerContainer";
                                    Image = Self + ".image";
                                    ConditionBar = Self + ".conditionBar";
                                    Amount = Self + ".amount";
                                }
                            }

                            public struct ItemCardFooterContainer
                            {
                                public string Self { get; }
                                public ItemCardButton Button { get; }

                                public ItemCardFooterContainer(ItemCard card)
                                {
                                    Self = card.Self + ".bottomContainer";
                                    Button = default(ItemCardButton);
                                    Button = new ItemCardButton(this);
                                }

                                public struct ItemCardButton
                                {
                                    public string Self { get; }
                                    public string Text { get; }

                                    public ItemCardButton(ItemCardFooterContainer footerContainer)
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
                        public const string SELF = MainContainer.SELF + ".notificationContainer";

                        public static class HeaderContainer
                        {
                            public const string SELF = NotificationContainer.SELF + ".headerContainer";

                            public const string TITLE = SELF + ".title";
                        }

                        public static class MessageContainer
                        {
                            public const string SELF = NotificationContainer.SELF + ".messageContainer";

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

            #endregion

            private static class Materials
            {
                public const string BLUR = "assets/content/ui/uibackgroundblur.mat";
            }

            [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
            public struct RustColor
            {
                /*x00 -> x99 goes from darkest to lightest*/

                public static readonly RustColor White = new RustColor(1f);
                public static readonly RustColor Black = new RustColor(0f);

                // ReSharper disable once IdentifierTypo
                public static readonly RustColor Transp  = new RustColor(0, 0f);
                public static readonly RustColor Bg00    = new RustColor(0.1f);
                public static readonly RustColor Bg01    = new RustColor(0.3f);
                public static readonly RustColor Bg02    = new RustColor(0.5f);
                public static readonly RustColor Bg03    = new RustColor(0.6f);
                public static readonly RustColor Fg00    = new RustColor(0.7f);
                public static readonly RustColor Fg01    = new RustColor(0.8f);
                public static readonly RustColor Fg02    = new RustColor(0.85f);
                public static readonly RustColor Fg03    = new RustColor(0.9f);
                public static readonly RustColor Success = new RustColor(0.28f, 0.4f, 0.2f);
                public static readonly RustColor Warn    = new RustColor(0.7f, 0.6f, 0.2f);
                public static readonly RustColor Error   = new RustColor(0.6f, 0.2f, 0.1f);

                public readonly float Red, Green, Blue, Alpha;

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
                    Red = red;
                    Green = green;
                    Blue = blue;
                    Alpha = alpha;
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

                public static float NormalizeByte(byte b) => b / (float)byte.MaxValue;

                private static void NormalizeRange(ref float value)
                {
                    value = Math.Max(Math.Min(value, 1f), 0);
                }

                public override string ToString() =>
                    _serialized ?? (_serialized = $"{Red} {Green} {Blue} {Alpha}");

                [System.Diagnostics.Contracts.Pure]
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

                [System.Diagnostics.Contracts.Pure]
                public RustColor With(
                    byte? r = null,
                    byte? g = null,
                    byte? b = null,
                    float? a = null
                )
                {
                    float fR = r.HasValue ? NormalizeByte(r.Value) : Red;
                    float fG = g.HasValue ? NormalizeByte(g.Value) : Green;
                    float fB = b.HasValue ? NormalizeByte(b.Value) : Green;

                    return new RustColor(
                        fR,
                        fG,
                        fB,
                        a.GetValueOrDefault(Alpha)
                    );
                }

                [System.Diagnostics.Contracts.Pure]
                public RustColor WithAlpha(float alpha) => new RustColor(
                    Red,
                    Green,
                    Blue,
                    alpha
                );

                public static class ComponentColors
                {
                    public static readonly RustColor PanelBase = Bg00.WithAlpha(0.4f);
                    public static readonly RustColor PanelFg   = Bg01.WithAlpha(0.5f);


                    public static readonly RustColor TextWhite    = Fg01.WithAlpha(0.6f);
                    public static readonly RustColor TextSemiDark = Fg00.WithAlpha(0.4f);

                    public static readonly RustColor TextDefault  = Fg00.WithAlpha(0.4f);
                    public static readonly RustColor TextDisabled = Fg00.WithAlpha(0.2f);

                    public static readonly RustColor ButtonDefault  = Bg03.WithAlpha(0.3f);
                    public static readonly RustColor ButtonDisabled = Bg03.WithAlpha(0.2f);
                    public static readonly RustColor ButtonSuccess  = Success.WithAlpha(0.7f);
                    public static readonly RustColor ButtonError    = Error.WithAlpha(0.7f);
                }
            }

            private static class ComponentBuilder
            {
                private const string DEFAULT_ICON_URL =
                    "https://api.gmonetize.ru/static/v2/image/plugin/icons/rust_94773.png";

                private static readonly Dictionary<ValueTuple<float, float, float, float>, CuiRectTransformComponent>
                    s_RectTransformCache =
                        new Dictionary<ValueTuple<float, float, float, float>, CuiRectTransformComponent>();


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
                            Name = Names.MainContainer.SELF,
                            Components = {
                                new CuiImageComponent {
                                    Color = RustColor.ComponentColors.PanelBase,
                                    Material = Materials.BLUR
                                },
                                new CuiNeedsCursorComponent(),
                                GetTransform(
                                    0.0135f,
                                    0.9865f,
                                    0.139f,
                                    0.95f
                                )
                            }
                        },
                        /*header container*/
                        new CuiElement {
                            Parent = Names.MainContainer.SELF,
                            Name = Names.MainContainer.HeaderContainer.SELF,
                            Components = {
                                new CuiImageComponent {Color = RustColor.ComponentColors.PanelFg},
                                GetTransform(yMin: 0.95f)
                            }
                        },
                        /*close button*/
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.SELF,
                            Name = Names.MainContainer.HeaderContainer.CloseButton.SELF,
                            Components = {
                                new CuiButtonComponent {
                                    Color = RustColor.ComponentColors.ButtonError,
                                    Command = CMD_CLOSE
                                },
                                GetTransform(
                                    0.94f,
                                    0.9965f,
                                    0.098f,
                                    0.88f
                                )
                            }
                        },
                        /*close button text*/
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.CloseButton.SELF,
                            Name = Names.MainContainer.HeaderContainer.CloseButton.TEXT,
                            Components = {
                                new CuiTextComponent {
                                    Text = "CLOSE",
                                    Color = RustColor.ComponentColors.TextSemiDark,
                                    Align = TextAnchor.MiddleCenter
                                },
                                GetTransform()
                            }
                        },
                        /*pagination buttons container*/
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.SELF,
                            Name = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.SELF,
                            Components = {
                                new CuiImageComponent {
                                    Color = RustColor.Transp,
                                },
                                GetTransform(
                                    0.0035f,
                                    0.1f,
                                    0.098f,
                                    0.84f
                                ) // w=0,0565
                            }
                        }
                    };
                }

                public static void PaginationButtons(bool prev, bool next, List<CuiElement> componentList)
                {
                    RustColor colorEnabled = RustColor.ComponentColors.ButtonDefault;
                    RustColor colorDisabled = RustColor.ComponentColors.ButtonDisabled;

                    RustColor textColorEnabled = RustColor.ComponentColors.TextDefault;
                    RustColor textColorDisabled = RustColor.ComponentColors.TextDisabled;

                    RustColor prevBtnColor = prev ? colorEnabled : colorDisabled;
                    RustColor prevBtnTextColor = prev ? textColorEnabled : textColorDisabled;
                    string prevBtnCmd = prev ? CMD_PREVP : null;

                    RustColor nextBtnColor = next ? colorEnabled : colorDisabled;
                    RustColor nextBtnTextColor = next ? textColorEnabled : textColorDisabled;
                    string nextBtnCmd = next ? CMD_NEXTP : null;

                    /*prev btn*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.SELF,
                            Name = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev.SELF,
                            Components = {
                                new CuiButtonComponent {
                                    Color = prevBtnColor,
                                    Command = prevBtnCmd
                                },
                                GetTransform(xMax: 0.48f)
                            }
                        }
                    );

                    /*prev btn text*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev.SELF,
                            Name = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Prev.TEXT,
                            Components = {
                                new CuiTextComponent {
                                    Color = prevBtnTextColor,
                                    Text = "PREVIOUS",
                                    Align = TextAnchor.MiddleCenter,
                                    FontSize = 12
                                },
                                GetTransform()
                            }
                        }
                    );

                    /*next btn*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.SELF,
                            Name = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next.SELF,
                            Components = {
                                new CuiButtonComponent {
                                    Color = nextBtnColor,
                                    Command = nextBtnCmd
                                },
                                GetTransform(xMin: 0.52f)
                            }
                        }
                    );

                    /*next btn text*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next.SELF,
                            Name = Names.MainContainer.HeaderContainer.PaginationButtonsContainer.Next.TEXT,
                            Components = {
                                new CuiTextComponent {
                                    Color = nextBtnTextColor,
                                    Text = "NEXT",
                                    Align = TextAnchor.MiddleCenter,
                                    FontSize = 12
                                },
                                GetTransform()
                            }
                        }
                    );
                }

                public static void Notification(List<CuiElement> componentList, NotificationType type)
                {
                    /*notification container*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.SELF,
                            Components = {
                                new CuiImageComponent {
                                    Color = RustColor.ComponentColors.PanelFg,
                                },
                                GetTransform(
                                    xMin: 0.35f,
                                    xMax: 0.65f,
                                    yMin: 0.3f,
                                    yMax: 0.6f
                                )
                            }
                        }
                    );

                    /*notification header container*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.HeaderContainer.SELF,
                            Components = {
                                new CuiImageComponent {Color = RustColor.Bg01.WithAlpha(0.5f)},
                                GetTransform(yMin: 0.9f, xMax: 0.998f, yMax: 0.998f)
                            }
                        }
                    );

                    /*notification title*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.HeaderContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.HeaderContainer.TITLE,
                            Components = {
                                new CuiTextComponent {
                                    Color = RustColor.ComponentColors.TextWhite.WithAlpha(0.4f),
                                    Text = "NOTIFICATION",
                                    Align = TextAnchor.MiddleCenter
                                },
                                GetTransform()
                            }
                        }
                    );

                    /*notification message container*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.MessageContainer.SELF,
                            Components = {
                                new CuiImageComponent {Color = RustColor.Transp},
                                GetTransform(yMax: 0.9f)
                            }
                        }
                    );

                    float notificationMessageYMin = type == NotificationType.ItemsLoadError ? 0.3f : 0f;

                    string text;

                    switch (type)
                    {
                        case NotificationType.LoadingItems:
                            text = "LOADING ITEMS...";
                            break;
                        case NotificationType.ItemsLoadError:
                            text = "FAILED TO LOAD ITEMS";
                            break;
                        case NotificationType.InventoryEmpty:
                            text = "INVENTORY IS EMPTY";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(type), type, null);
                    }

                    /*notification message text*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.MessageContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.MessageContainer.MESSAGE,
                            Components = {
                                new CuiTextComponent {
                                    Color = RustColor.ComponentColors.TextSemiDark,
                                    Text = text,
                                    Align = TextAnchor.MiddleCenter
                                },
                                GetTransform(yMin: notificationMessageYMin)
                            }
                        }
                    );

                    AddNotificationButton(componentList, type);
                }

                private static void AddNotificationButton(List<CuiElement> componentList, NotificationType type)
                {
                    if (type != NotificationType.ItemsLoadError)
                        return;

                    string text = "RETRY";
                    string cmd = CMD_RETRY_LOAD;

                    /*notification message dismiss btn*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.MessageContainer.SELF,
                            Name = Names.MainContainer.NotificationContainer.MessageContainer.Button.SELF,
                            Components = {
                                new CuiButtonComponent {
                                    Color = RustColor.ComponentColors.ButtonError,
                                    Command = cmd
                                },
                                GetTransform(yMax: 0.3f)
                            }
                        }
                    );

                    /*notification message dismiss btn text*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.NotificationContainer.MessageContainer.Button.SELF,
                            Name = Names.MainContainer.NotificationContainer.MessageContainer.Button.TEXT,
                            Components = {
                                new CuiTextComponent {
                                    Color = RustColor.ComponentColors.TextWhite,
                                    Text = text
                                },
                                GetTransform()
                            }
                        }
                    );
                }

                public static CuiElement ItemListContainer()
                {
                    return new CuiElement {
                        Parent = Names.MainContainer.SELF,
                        Name = Names.MainContainer.ItemListContainer.SELF,
                        Components = {
                            new CuiImageComponent {
                                Color = RustColor.Transp,
                            },
                            GetTransform(
                                0.0035f,
                                0.9965f,
                                0.006f,
                                0.944f
                            )
                        }
                    };
                }

                public static void ItemCard(
                    int index,
                    gAPI.InventoryEntry inventoryEntry,
                    RedeemButtonState buttonState,
                    List<CuiElement> componentList
                )
                {
                    CuiRectTransformComponent gridTransform = GetGridTransform(
                        COLUMN_COUNT,
                        ROW_COUNT,
                        COLUMN_GAP,
                        ROW_GAP,
                        index
                    );

                    Debug.Log($"Item {index} grid transform: {gridTransform.AnchorMin} ; {gridTransform.AnchorMax}");

                    Names.MainContainer.ItemListContainer.ItemCard nCard =
                        Names.MainContainer.ItemListContainer.Card(index);

                    /*root*/
                    componentList.Add(
                        new CuiElement {
                            Parent = Names.MainContainer.ItemListContainer.SELF,
                            Name = nCard.Self,
                            Components = {
                                new CuiImageComponent {
                                    Color = RustColor.ComponentColors.PanelFg,
                                },
                                gridTransform
                            }
                        }
                    );
                    /*header container*/
                    componentList.Add(
                        new CuiElement {
                            Parent = nCard.Self,
                            Name = nCard.Header.Self,
                            Components = {
                                new CuiImageComponent {Color = RustColor.ComponentColors.PanelFg.WithAlpha(0.5f)},
                                GetTransform(yMin: 0.9f, yMax: 0.99f, xMax: 0.995f)
                            }
                        }
                    );
                    /*listing name*/
                    componentList.Add(
                        LabelBuilder.Create(nCard.Header.Self, nCard.Header.ItemName)
                                    .WithText(inventoryEntry.name)
                                    .WithFontSize(12)
                                    .Centered()
                                    .WithColor(RustColor.ComponentColors.TextWhite.WithAlpha(0.5f))
                                    .WithTransform(GetTransform())
                                    .ToCuiElement()
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
                            Parent = nCard.Self,
                            Name = nCard.Center.Self,
                            Components = {
                                new CuiImageComponent {Color = RustColor.Transp},
                                GetTransform(
                                    0f,
                                    1f,
                                    0.2f,
                                    0.9f
                                )
                            }
                        }
                    );
                    /*item image*/
                    /*componentList.Add(
                        new CuiElement {
                            Parent = nCard.Center.Self,
                            Name = nCard.Center.Image,
                            Components = {
                                new CuiImageComponent {ItemId = -253079493},
                                GetTransform()
                            }
                        }
                    );*/

                    CuiElement iconElement = new CuiElement {
                        Parent = nCard.Center.Self,
                        Name = nCard.Center.Image
                    };

                    componentList.Add(iconElement);

                    if (!string.IsNullOrEmpty(inventoryEntry.iconId))
                    {
                        iconElement.Components.Add(
                            new CuiRawImageComponent {Url = gAPI.GetIconUrl(inventoryEntry.iconId)}
                        );
                    }
                    else if (inventoryEntry.type == gAPI.InventoryEntry.InventoryEntryType.ITEM)
                    {
                        iconElement.Components.Add(new CuiImageComponent {ItemId = inventoryEntry.item.itemId});
                    }
                    else
                    {
                        iconElement.Components.Add(new CuiRawImageComponent {Url = DEFAULT_ICON_URL});
                    }

                    /*item amount*/
                    if (inventoryEntry.type == gAPI.InventoryEntry.InventoryEntryType.ITEM)
                    {
                        componentList.Add(
                            new CuiElement {
                                Parent = nCard.Center.Image,
                                Name = nCard.Center.Amount,
                                Components = {
                                    new CuiTextComponent {
                                        Text = inventoryEntry.item.amount.ToString(),
                                        Align = TextAnchor.MiddleCenter,
                                        Color = RustColor.ComponentColors.TextSemiDark.WithAlpha(0.5f)
                                    },
                                    GetTransform(xMax: 0.3f, yMax: 0.2f)
                                }
                            }
                        );
                    }

                    /*footer container*/
                    componentList.Add(
                        new CuiElement {
                            Parent = nCard.Self,
                            Name = nCard.Footer.Self,
                            Components = {
                                new CuiImageComponent {Color = RustColor.Transp},
                                GetTransform(yMax: 0.16f)
                            }
                        }
                    );

                    RedeemButton(
                        nCard,
                        index,
                        inventoryEntry.id,
                        buttonState,
                        componentList
                    );

#if F_UI_CONDITIONBAR
                        AddConditionBar(ref nCard, componentList, 1f );
#endif
                }

                public static void RedeemButton(
                    Names.MainContainer.ItemListContainer.ItemCard nCard,
                    int index,
                    string entryId,
                    RedeemButtonState state,
                    List<CuiElement> componentList
                )
                {
                    RustColor btnColor;
                    string btnText;

                    switch (state)
                    {
                        case RedeemButtonState.InvalidItem:
                            btnText = "REDEEM";
                            btnColor = RustColor.ComponentColors.ButtonSuccess;
                            break;
                        case RedeemButtonState.InventoryFull:
                            btnText = "INV. FULL";
                            btnColor = RustColor.ComponentColors.ButtonDisabled;
                            break;
                        case RedeemButtonState.ResearchComplete:
                            btnText = "RESEARCHED";
                            btnColor = RustColor.ComponentColors.ButtonDisabled;
                            break;
                        case RedeemButtonState.Error:
                            btnText = "ERROR";
                            btnColor = RustColor.ComponentColors.ButtonError;
                            break;
                        case RedeemButtonState.Available:
                            btnText = "REDEEM";
                            btnColor = RustColor.ComponentColors.ButtonSuccess;
                            break;
                        case RedeemButtonState.Redeeming:
                            btnText = "REDEEMING...";
                            btnColor = RustColor.ComponentColors.ButtonDisabled;
                            break;
                        default:
                            throw new ArgumentOutOfRangeException(nameof(state), state, null);
                    }

                    componentList.Add(
                        new CuiElement {
                            Parent = nCard.Footer.Self,
                            Name = nCard.Footer.Button.Self,
                            Components = {
                                new CuiButtonComponent {
                                    Color = btnColor,
                                    Command = string.Join(
                                        " ",
                                        CMD_REDEEM,
                                        entryId,
                                        index
                                    )
                                },
                                GetTransform(
                                    0.02f,
                                    0.97f,
                                    0.11f,
                                    0.9f
                                )
                            }
                        }
                    );

                    componentList.Add(
                        new CuiElement {
                            Parent = nCard.Footer.Button.Self,
                            Name = nCard.Footer.Button.Text,
                            Components = {
                                new CuiTextComponent {
                                    Color = RustColor.ComponentColors.TextSemiDark,
                                    Align = TextAnchor.MiddleCenter,
                                    Text = btnText
                                },
                                GetTransform()
                            }
                        }
                    );
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
                            Name = nCard.Center.ConditionBar,
                            Components = {
                                new CuiImageComponent {Color = RustColor.Error.WithAlpha(0.1f)},
                                GetTransform(yMax: Mathf.Clamp01(condition))
                            }
                        }
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
                    float totalRowGap = rowGap * (rows - 1);

                    float cardWidth = (0.999f - totalColumnGap) / cols;
                    float cardHeight = (0.997f - totalRowGap) / rows;

                    int rowIndex = itemIndex / cols;
                    int columnIndex = itemIndex % cols;

                    float columnGapSum = colGap * columnIndex;
                    float cardWidthSum = cardWidth * columnIndex;
                    float xPosition = columnGapSum + cardWidthSum;

                    float rowGapSum = rowGap * rowIndex;
                    float cardHeightSum = cardHeight * (rowIndex + 1);
                    float yPosition = 0.997f - (rowGapSum + cardHeightSum);

                    return GetTransform(
                        xPosition,
                        xPosition + cardWidth,
                        yPosition,
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

                    if (!s_RectTransformCache.TryGetValue(key, out transform))
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
                        Name = name;
                        Parent = parent;
                        Text = text;
                        TextColor = textColor;
                        Align = align;
                        FontSize = fontSize;
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
                            Name = Name,
                            Components = {
                                new CuiTextComponent {
                                    Color = TextColor,
                                    Text = Text,
                                    Align = Align,
                                    FontSize = FontSize
                                },
                                Transform
                            }
                        };
                    }
                }
            }
        }

        private static class gAPI
        {
            private const string MAIN_API_PATH   = "/main/v3/plugin";
            private const string STATIC_API_PATH = "/static/v2";

            private static gMonetize                  s_PluginInstance;
            private static Dictionary<string, string> s_RequestHeaders;

            public static bool IsReady => s_PluginInstance &&
                                          !string.IsNullOrEmpty(s_PluginInstance._configuration.ApiKey) &&
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
                    {"Content-Type", "application/json"},
                    {"Authorization", "ApiKey " + s_PluginInstance._configuration.ApiKey}
                };
            }

            public static void GetCustomerBalance(string userId, Action<long> balanceCb, Action<int> errCb)
            {
                string url = GetBalanceUrl(userId);

                WebRequests.Enqueue(
                    url,
                    null,
                    (code, body) =>
                    {
                        JObject obj = JObject.Parse(body);

                        if (code != 200)
                        {
                            errCb?.Invoke(code);
                        }
                        else
                        {
                            balanceCb(obj.Value<long>("value"));
                        }
                    },
                    s_PluginInstance,
                    headers: s_RequestHeaders
                );
            }

            public static void SetCustomerBalance(
                string userId,
                long value,
                Action okCb,
                Action<int> errCb
            )
            {
                string url = GetBalanceUrl(userId);

                string payload = JsonConvert.SerializeObject(new {value});

                WebRequests.Enqueue(
                    url,
                    payload,
                    (code, body) =>
                    {
                        if (code != 200)
                        {
                            errCb?.Invoke(code);
                        }
                        else if (okCb != null)
                        {
                            okCb();
                        }
                    },
                    s_PluginInstance,
                    RequestMethod.PATCH,
                    s_RequestHeaders
                );
            }

            public static void SendHeartbeat(ServerHeartbeatRequest request)
            {
                string payloadJson = JsonConvert.SerializeObject(request);

                string url = GetHeartbeatUrl();

                LogMessage("Sending server heartbeat to {0}:\n{1}", url, payloadJson);

                WebRequests.Enqueue(
                    url,
                    payloadJson,
                    (code, body) => OnHeartbeat?.Invoke(new HeartbeatApiResult(code, request)),
                    s_PluginInstance,
                    RequestMethod.POST,
                    s_RequestHeaders
                );
            }

            public static void GetInventory(string userId)
            {
                string url = GetInventoryUrl(userId);

                WebRequests.Enqueue(
                    url,
                    null,
                    (code, body) => OnReceiveInventory?.Invoke(
                        new InventoryApiResult(code, userId, JsonConvert.DeserializeObject<List<InventoryEntry>>(body))
                    ),
                    s_PluginInstance,
                    RequestMethod.GET,
                    s_RequestHeaders
                );
            }

            public static void RedeemItem(string userId, string inventoryEntryId)
            {
                string url = GetRedeemUrl(userId, inventoryEntryId);

                WebRequests.Enqueue(
                    url,
                    null,
                    (code, body) => OnRedeemItem?.Invoke(new RedeemItemApiResult(code, userId, inventoryEntryId)),
                    s_PluginInstance,
                    RequestMethod.POST,
                    s_RequestHeaders
                );
            }

            private static string GetInventoryUrl(string userId)
            {
                return string.Concat(ApiBaseUrl, MAIN_API_PATH, $"/customer/STEAM/{userId}/inventory");
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

            private static string GetBalanceUrl(string userId)
            {
                return string.Concat(ApiBaseUrl, MAIN_API_PATH, $"/customer/STEAM/{userId}/balance");
            }

            public static string GetIconUrl(string iconId)
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

            private class DurationTimeSpanJsonConverter : JsonConverter
            {
                public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
                {
                    throw new NotImplementedException();
                }

                public override object ReadJson(
                    JsonReader reader,
                    Type objectType,
                    object existingValue,
                    JsonSerializer serializer
                )
                {
                    string stringValue = reader.Value as string;

                    if (string.IsNullOrEmpty(stringValue))
                    {
                        LogMessage("TimeSpan stringValue is null");
                        return null;
                    }

                    TimeSpan ts = ParseDuration(stringValue);

                    LogMessage("Converting to timeSpan: {0}=>{1}", stringValue, ts);

                    return ts;
                }

                public override bool CanConvert(Type objectType)
                {
                    return objectType == typeof(TimeSpan?);
                }

                public static TimeSpan ParseDuration(string input)
                {
                    int tIndex = input.IndexOf('T');

                    int timePortionIndex = tIndex != -1 ? tIndex + 1 : 0;

                    int partStart = timePortionIndex;
                    int hours = 0, minutes = 0, seconds = 0;
                    for (int i = timePortionIndex; i < input.Length; i++)
                    {
                        char c = input[i];

                        string partBuf;

                        switch (c)
                        {
                            case 'H':
                                partBuf = input.Substring(partStart, i - partStart);
                                hours = int.Parse(partBuf);
                                partStart = i + 1;
                                break;
                            case 'M':
                                partBuf = input.Substring(partStart, i - partStart);
                                minutes = int.Parse(partBuf);
                                partStart = i + 1;
                                break;
                            case 'S':
                            case 'Z':
                                partBuf = input.Substring(partStart, i - partStart);
                                seconds = int.Parse(partBuf);
                                partStart = i + 1;
                                if (c == 'Z')
                                {
                                    i = input.Length;
                                }

                                break;

                            default:
                                if (i == input.Length - 1)
                                {
                                    partBuf = input.Substring(partStart, i - partStart + 1);
                                    seconds = int.Parse(partBuf);
                                }

                                break;
                        }
                    }

                    return new TimeSpan(
                        0,
                        hours,
                        minutes,
                        seconds
                    );
                }
            }

            #region DTOs

            public class InventoryEntry
            {
                public string id;

                [JsonConverter(typeof(StringEnumConverter))]
                public InventoryEntryType type;

                public string name;
                public string iconId;

                [JsonConverter(typeof(DurationTimeSpanJsonConverter))]
                public TimeSpan? wipeBlockDuration;

                public ItemDto          item;
                public ResearchDto      research;
                public RankDto          rank;
                public PermissionDto    permission;
                public GoodObjectImpl[] contents;

                public bool HasCustomIcon()
                {
                    return !string.IsNullOrEmpty(iconId);
                }

                public enum InventoryEntryType
                {
                    ITEM,
                    KIT,
                    RANK,
                    RESEARCH,
                    CUSTOM,
                    PERMISSION
                }
            }

            public class GoodObjectImpl
            {
                [JsonConverter(typeof(StringEnumConverter))]
                public GoodObjectType type;

                public int    researchId;
                public string groupName;
                public string value;

                [JsonConverter(typeof(DurationTimeSpanJsonConverter))]
                public TimeSpan duration;

                public int          itemId;
                public uint         amount;
                public RustItemMeta meta;

                public ItemDto ToItem()
                {
                    return new ItemDto {
                        itemId = itemId,
                        amount = amount,
                        meta = meta
                    };
                }

                public ResearchDto ToResearch()
                {
                    return new ResearchDto {researchId = researchId};
                }

                public PermissionDto ToPermission()
                {
                    return new PermissionDto {
                        value = value,
                        duration = duration
                    };
                }

                public RankDto ToRank()
                {
                    return new RankDto {
                        groupName = groupName,
                        duration = duration
                    };
                }

                public CommandDto ToCommand()
                {
                    return new CommandDto {value = value};
                }

                public enum GoodObjectType
                {
                    ITEM,
                    RANK,
                    COMMAND,
                    RESEARCH,
                    PERMISSION
                }
            }

            public class RustItemMeta
            {
                public ulong? skinId;
                public float  condition;
            }

            public class ItemDto
            {
                public int          itemId;
                public uint         amount;
                public RustItemMeta meta;

                public ItemDefinition FindItemDefinition()
                {
                    return ItemManager.FindItemDefinition(itemId);
                }
            }

            public class ResearchDto
            {
                public int researchId;

                public ItemDefinition FindItemDefinition()
                {
                    return ItemManager.FindItemDefinition(researchId);
                }
            }

            public class RankDto
            {
                public string groupName;

                [JsonConverter(typeof(DurationTimeSpanJsonConverter))]
                public TimeSpan duration;
            }

            public class PermissionDto
            {
                public string value;

                [JsonConverter(typeof(DurationTimeSpanJsonConverter))]
                public TimeSpan duration;
            }

            public class CommandDto
            {
                public string value;
            }

            #endregion
        }

        #region API RESULTS

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

            public HeartbeatApiResult(int statusCode, ServerHeartbeatRequest request) : base(statusCode)
            {
                Request = request;
            }
        }

        private class RedeemItemApiResult : ApiResult
        {
            public string UserId { get; }
            public string InventoryEntryId { get; }

            public RedeemItemApiResult(int statusCode, string userId, string inventoryEntryId) : base(statusCode)
            {
                UserId = userId;
                InventoryEntryId = inventoryEntryId;
            }
        }

        private class InventoryApiResult : ApiResult
        {
            public string UserId { get; }
            public List<gAPI.InventoryEntry> Inventory { get; }

            public InventoryApiResult(int statusCode, string userId, List<gAPI.InventoryEntry> inventory) : base(
                statusCode
            )
            {
                UserId = userId;
                Inventory = inventory;
            }
        }

        private class ServerHeartbeatRequest
        {
            [JsonProperty("motd")]
            public string Description { get; }

            [JsonProperty("map")]
            public ServerMapRequest Map { get; }

            [JsonProperty("players")]
            public ServerPlayersRequest Players { get; }

            public ServerHeartbeatRequest(string description, ServerMapRequest map, ServerPlayersRequest players)
            {
                Description = description;
                Map = map;
                Players = players;
            }

            public class ServerMapRequest
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

                public ServerMapRequest(
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

            public class ServerPlayersRequest
            {
                [JsonProperty("online")]
                public int Online { get; }

                [JsonProperty("max")]
                public int Max { get; }

                public ServerPlayersRequest(int online, int max)
                {
                    Online = online;
                    Max = max;
                }
            }
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

            public static PluginConfiguration GetDefault() => new PluginConfiguration {
                ApiKey = "Change me",
                ApiBaseUrl = "https://api.gmonetize.ru",
                ChatCommands = new[] {"shop"}
            };
        }

        #endregion

        private class TimedRank
        {
            public string Name { get; set; }
            public RankType Type { get; set; }
            public DateTime StartedAt { get; set; }
            public TimeSpan Duration { get; set; }

            public static TimedRank CreateFrom(gAPI.PermissionDto permissionDto)
            {
                return new TimedRank {
                    Name = permissionDto.value,
                    Type = RankType.Permission,
                    StartedAt = DateTime.UtcNow,
                    Duration = permissionDto.duration
                };
            }

            public static TimedRank CreateFrom(gAPI.RankDto rankDto)
            {
                return new TimedRank {
                    Name = rankDto.groupName,
                    Type = RankType.Group,
                    StartedAt = DateTime.UtcNow,
                    Duration = rankDto.duration
                };
            }

            public static TimedRank CreateFrom(gAPI.GoodObjectImpl goodObject)
            {
                switch (goodObject.type)
                {
                    case gAPI.GoodObjectImpl.GoodObjectType.PERMISSION:
                        return new TimedRank {
                            Name = goodObject.value,
                            Type = RankType.Permission,
                            StartedAt = DateTime.UtcNow,
                            Duration = goodObject.duration
                        };

                    case gAPI.GoodObjectImpl.GoodObjectType.RANK:
                        return new TimedRank {
                            Name = goodObject.groupName,
                            Type = RankType.Group,
                            StartedAt = DateTime.UtcNow,
                            Duration = goodObject.duration
                        };

                    default: throw new ArgumentOutOfRangeException(nameof(goodObject.type));
                }
            }

            public TimeSpan TimeLeft()
            {
                TimeSpan timePassed = DateTime.UtcNow - StartedAt;

                return Duration - timePassed;
            }

            public void Add(TimedRank other)
            {
                if (Name != other.Name)
                {
                    throw new ArgumentException(
                        $"Other rank Name does not match this rank name ({other.Name} != {Name})",
                        nameof(other.Name)
                    );
                }

                if (Type != other.Type)
                {
                    throw new ArgumentException(
                        $"Other rank Type does not match this rank type ({other.Type:G} != {Type:G})",
                        nameof(other.Type)
                    );
                }

                if (other.TimeLeft() <= TimeSpan.Zero)
                {
                    LogMessage("Adding rank with timeleft <= 0");
                    return;
                }

                Duration = Duration.Add(other.TimeLeft());
            }

            public void Set(IPlayer player)
            {
                switch (Type)
                {
                    case RankType.Permission:
                        LogMessage(
                            "Setting player {0} permission {1} for {2}",
                            player,
                            Name,
                            Duration.ToString("g")
                        );
                        player.GrantPermission(Name);
                        break;
                    case RankType.Group:
                        LogMessage(
                            "Adding player {0} to group {1} for {2}",
                            player,
                            Name,
                            Duration.ToString("g")
                        );
                        player.AddToGroup(Name);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Type));
                }
            }

            public void Unset(IPlayer player)
            {
                switch (Type)
                {
                    case RankType.Permission:
                        player.RevokePermission(Name);
                        break;
                    case RankType.Group:
                        player.RemoveFromGroup(Name);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(Type));
                }
            }

            public enum RankType
            {
                Permission,
                Group
            }
        }
    }
}
