#define DEBUG

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info(
        "gMonetize",
        "2CHEVSKII",
        "0.1.0"
    )]
    public class gMonetize : CovalencePlugin
    {
        Dictionary<string, List<PlayerCartItem>>     _playerCarts;
        Dictionary<string, List<PlayerCartItem>>     _delayedCommands;
        Dictionary<string, List<PlayerSubscription>> _subscriptionsPersistence;

        static gMonetize s_Instance;

        string ApiBaseUrl { get; }
        string ApiToken { get; }
        float ApiRequestTimeout { get; }
        Dictionary<string, string> ApiHeaders { get; }


        [Conditional("DEBUG")]
        void LogDebug(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[GMONETIZE DEBUG]: " + format, args);
        }


        void AddUiController(BasePlayer player)
        {
            player.gameObject.AddComponent<UiController>();
        }

        void RemoveUiController(BasePlayer player)
        {
            UnityEngine.Object.Destroy(player.GetComponent<UiController>());
        }

        void OnServerInitialized()
        {
            s_Instance = this;

            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                AddUiController(player);
            }

            covalence.RegisterCommand(
                "shop",
                this,
                HandleOpenShopCommand
            );
            covalence.RegisterCommand(
                "gmonetize.openshop",
                this,
                HandleOpenShopCommand
            );
            covalence.RegisterCommand(
                "gmonetize.closeui",
                this,
                HandleCloseShopCommand
            );
            covalence.RegisterCommand(
                "gmonetize.switchtab",
                this,
                HandleSwitchTabCommand
            );
            covalence.RegisterCommand(
                "gmonetize.claimitem",
                this,
                HandleClaimItemCommand
            );
            covalence.RegisterCommand(
                "gmonetize.nextpage",
                this,
                HandleNextPageCommand
            );
            covalence.RegisterCommand(
                "gmonetize.prevpage",
                this,
                HandlePrevPageCommand
            );

            permission.RegisterPermission("gmonetize.useshop", this);

            /*start timers*/
            /*send api callback*/
        }

        void Unload()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                RemoveUiController(player);
            }

            if (s_Instance == this)
            {
                s_Instance = null;
            }
        }

        void OnPlayerInitialized(BasePlayer player)
        {
            AddUiController(player);
        }

        bool HandleOpenShopCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;

            basePlayer.gameObject.SendMessage(
                nameof(UiController.gMonetize_OpenShop)
            );

            return true;
        }

        bool HandleCloseShopCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;
            basePlayer.gameObject.SendMessage(
                nameof(UiController.gMonetize_CloseShop)
            );

            return true;
        }

        bool HandleSwitchTabCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;

            UiBuilder.Tab targetTab =
                args[0] == "items" ? UiBuilder.Tab.Items : UiBuilder.Tab.Subscriptions;

            basePlayer.gameObject.SendMessage(
                nameof(UiController.gMonetize_SwitchTab),
                targetTab
            );

            return true;
        }

        bool HandleClaimItemCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;

            var id = int.Parse(args[0]);

            basePlayer.gameObject.SendMessage(nameof(UiController.gMonetize_ClaimItem), id);

            throw new NotImplementedException();

            return true;
        }

        bool HandleNextPageCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;

            basePlayer.gameObject.SendMessage(nameof(UiController.gMonetize_NextPage));

            return true;
        }

        bool HandlePrevPageCommand(IPlayer player, string command, string[] args)
        {
            var basePlayer = (BasePlayer)player.Object;

            basePlayer.gameObject.SendMessage(nameof(UiController.gMonetize_PrevPage));

            return true;
        }

        void UpdatePlayerSubscriptionData(IEnumerable<string> playerIdList)
        {
            PostApiRequest<IEnumerable<string>,
                Dictionary<string, IEnumerable<PlayerSubscription>>>(
                "/subscriptions",
                playerIdList,
                dataCallback: SynchronizePlayerSubscriptions
            );
        }


        void InitPlayerCart(string userId)
        {
            if (!_playerCarts.ContainsKey(userId))
            {
                _playerCarts.Add(userId, new List<PlayerCartItem>());
            }
        }

        void SynchronizePlayerCarts(Dictionary<string, List<PlayerCartItem>> map)
        {
            _playerCarts.Clear();

            Dictionary<string, List<PlayerCartItem>> blueprintsToActivate =
                new Dictionary<string, List<PlayerCartItem>>();

            foreach (KeyValuePair<string, List<PlayerCartItem>> pair in map)
            {
                var userId = pair.Key;
                var items = pair.Value;

                InitPlayerCart(userId);

                foreach (PlayerCartItem item in items)
                {
                    switch (item.Type)
                    {
                        case PlayerCartItem.ItemType.Command:
                            switch (item.CommandExecutionType)
                            {
                                case PlayerCartItem.CardCommandExecutionType.MANUAL:
                                    _playerCarts[userId].Add(item);
                                    break;
                                default:

                                    if (!_delayedCommands.ContainsKey(userId))
                                    {
                                        _delayedCommands.Add(
                                            userId,
                                            new List<PlayerCartItem> { item }
                                        );
                                    }
                                    else
                                    {
                                        _delayedCommands[userId].Add(item);
                                    }

                                    break;
                            }

                            break;
                        case PlayerCartItem.ItemType.Item:
                            _playerCarts[userId].Add(item);
                            break;
                        case PlayerCartItem.ItemType.Blueprint:
                            if (item.BlueprintExecutionType ==
                                PlayerCartItem.CartBlueprintExecutionType.IMMEDIATE)
                            {
                                if (!blueprintsToActivate.ContainsKey(userId))
                                {
                                    blueprintsToActivate.Add(
                                        userId,
                                        new List<PlayerCartItem> { item }
                                    );
                                    continue;
                                }

                                blueprintsToActivate[userId].Add(item);

                                continue;
                            }

                            _playerCarts[userId].Add(item);

                            break;
                    }
                }
            }

            RunBlueprintActivations(blueprintsToActivate);
            RunImmediateCommands();
        }

        void RunImmediateCommands()
        {
            foreach (KeyValuePair<string, List<PlayerCartItem>> keyValuePair in
                     _delayedCommands)
            {
                var userId = keyValuePair.Key;
                var commandItems = keyValuePair.Value;
                var immediateCommands = commandItems.Where(
                    x => x.CommandExecutionType == PlayerCartItem.CardCommandExecutionType.IMMEDIATE
                );

                foreach (PlayerCartItem commandItem in immediateCommands)
                {
                    RunClaimedCallback(
                        userId,
                        commandItem,
                        () =>
                        {
                            commandItems.Remove(commandItem);
                            ExecuteCommandItem(userId, commandItem);
                        }
                    );
                }
            }
        }

        void ExecuteCommandItem(string userId, PlayerCartItem item)
        {
            var player = players.FindPlayerById(userId);

            foreach (string command in item.Commands)
            {
                string prepared = command.Replace("${steamid}", userId)
                                         .Replace("${name}", player.Name);

                server.Command(prepared);
            }
        }

        void RunBlueprintActivations(Dictionary<string, List<PlayerCartItem>> blueprintsToActivate)
        {
            foreach (KeyValuePair<string, List<PlayerCartItem>> keyValuePair in
                     blueprintsToActivate)
            {
                string userId = keyValuePair.Key;
                List<PlayerCartItem> blueprints = keyValuePair.Value;

                var basePlayer =
                    BasePlayer.activePlayerList.FirstOrDefault(x => x.UserIDString == userId) ??
                    BasePlayer.sleepingPlayerList.FirstOrDefault(x => x.UserIDString == userId);

                if (basePlayer == null)
                {
                    /*cannot learn blueprints if player object is not found */
                    LogWarning(
                        "Found blueprints for player {0} in the cart data, but could not find the player",
                        userId
                    );
                    continue;
                }

                foreach (PlayerCartItem blueprint in blueprints)
                {
                    var itemDef = ItemManager.FindItemDefinition(blueprint.ItemId);

                    if (basePlayer.blueprints.HasUnlocked(itemDef))
                    {
                        LogWarning(
                            "Found blueprint {0} for player {1} in the cart data, but player has this item unlocked already",
                            blueprint.ItemId,
                            userId
                        );

                        continue;
                    }

                    RunClaimedCallback(
                        userId,
                        blueprint,
                        () =>
                        {
                            basePlayer.blueprints.Unlock(itemDef);
                            LogDebug(
                                "Unlocked blueprint {0} for player {1}",
                                blueprint.ItemId,
                                userId
                            );
                        }
                    );
                }
            }
        }

        void RunClaimedCallback(
            string         userId,
            PlayerCartItem item,
            Action         successCallback,
            Action         errorCallback = null
        ) { }

        void SynchronizePlayerSubscriptions(
            Dictionary<string, IEnumerable<PlayerSubscription>> subscriptions
        )
        {
            foreach (KeyValuePair<string, IEnumerable<PlayerSubscription>> keyValuePair in
                     subscriptions)
            {
                string userId = keyValuePair.Key;
                PlayerSubscription[] fetchedSubscriptions = keyValuePair.Value.ToArray();
                List<PlayerSubscription> savedSubscriptions;

                if (!_subscriptionsPersistence.TryGetValue(userId, out savedSubscriptions))
                {
                    savedSubscriptions = new List<PlayerSubscription>();
                }

                PlayerSubscription[] newSubscriptions = fetchedSubscriptions
                                                        .Where(
                                                            x => savedSubscriptions.All(
                                                                y => y.Id != x.Id
                                                            )
                                                        )
                                                        .ToArray();
                PlayerSubscription[] expiredSubscriptions = savedSubscriptions
                                                            .Where(
                                                                x => fetchedSubscriptions.All(
                                                                    y => y.Id != x.Id
                                                                )
                                                            )
                                                            .ToArray();

                IPlayer player = players.FindPlayerById(userId);

                foreach (PlayerSubscription expiredSubscription in expiredSubscriptions)
                {
                    if (expiredSubscription.Type == PlayerSubscription.SubscriptionType.Permission)
                    {
                        player.RevokePermission(expiredSubscription.PermissionName);
                    }
                    else
                    {
                        player.RemoveFromGroup(expiredSubscription.PermissionName);
                    }

                    LogDebug(
                        "Player {0} subscription has expired: {1}/{2}",
                        userId,
                        expiredSubscription.Type.ToString(),
                        expiredSubscription.PermissionName
                    );
                    savedSubscriptions.Remove(expiredSubscription);
                }

                foreach (PlayerSubscription newSubscription in newSubscriptions)
                {
                    if (newSubscription.Type == PlayerSubscription.SubscriptionType.Permission)
                    {
                        player.GrantPermission(newSubscription.PermissionName);
                    }
                    else
                    {
                        player.AddToGroup(newSubscription.PermissionName);
                    }

                    LogDebug(
                        "Player {0} has activated new subscription: {1}/{2}",
                        userId,
                        newSubscription.Type.ToString(),
                        newSubscription.PermissionName
                    );
                    savedSubscriptions.Add(newSubscription);
                }

                _subscriptionsPersistence[userId] = savedSubscriptions;
            }

            SaveSubscriptionPersistence();
        }

        void SaveSubscriptionPersistence()
        {
            throw new NotImplementedException();
        }

        #region Request helpers

        void PostApiRequest<TData>(
            string                     path,
            Dictionary<string, object> queryParameters = null,
            Action<TData>              dataCallback    = null,
            Action<int>                errorCallback   = null
        )
            => PostApiRequest<TData, object>(
                path,
                null,
                queryParameters,
                dataCallback,
                errorCallback
            );

        void PostApiRequest<TBody, TData>(
            string                     path,
            TBody                      body,
            Dictionary<string, object> queryParameters = null,
            Action<TData>              dataCallback    = null,
            Action<int>                errorCallback   = null
        )
        {
            string requestUrl = BuildRequestUrl(path, queryParameters);

            webrequest.Enqueue(
                requestUrl,
                JsonConvert.SerializeObject(body),
                delegate(int code, string response)
                {
                    if (code >= 200 && code < 300)
                    {
                        dataCallback?.Invoke(JsonConvert.DeserializeObject<TData>(response));
                    }
                    else
                    {
                        errorCallback?.Invoke(code);
                    }
                },
                this,
                RequestMethod.POST,
                ApiHeaders,
                ApiRequestTimeout
            );
        }

        void GetApiRequest<TData>(
            string                     path,
            Dictionary<string, object> queryParameters = null,
            Action<TData>              dataCallback    = null,
            Action<int>                errorCallback   = null
        )
        {
            string requestUrl = BuildRequestUrl(path, queryParameters);

            webrequest.Enqueue(
                requestUrl,
                null,
                delegate(int code, string response)
                {
                    if (code >= 200 && code < 300)
                    {
                        dataCallback?.Invoke(JsonConvert.DeserializeObject<TData>(response));
                    }
                    else
                    {
                        errorCallback?.Invoke(code);
                    }
                },
                this,
                headers: ApiHeaders,
                timeout: ApiRequestTimeout
            );
        }

        string BuildRequestUrl(string path, Dictionary<string, object> queryParams)
        {
            string queryString = string.Empty;

            if (queryParams != null)
            {
                queryString = ComposeQueryString(queryParams);
            }

            return ApiBaseUrl + path + queryString;
        }

        string ComposeQueryString(Dictionary<string, object> parameters)
        {
            StringBuilder stringBuilder = new StringBuilder();

            foreach (KeyValuePair<string, object> pair in parameters)
            {
                string paramName = pair.Key;
                object paramValue = pair.Value;

                IEnumerable paramValueEnumerable = paramValue as IEnumerable;

                string paramValueString;

                if (paramValueEnumerable != null)
                {
                    List<string> paramValues = new List<string>();

                    foreach (object obj in paramValueEnumerable)
                    {
                        paramValues.Add(paramName + "=" + obj);
                    }

                    paramValueString = string.Join("&", paramValues);
                }
                else
                {
                    paramValueString = paramName + "=" + paramValue;
                }

                if (stringBuilder.Length != 0)
                {
                    stringBuilder.Append('&');
                }

                stringBuilder.Append(paramValueString);
            }

            if (stringBuilder.Length == 0)
            {
                return string.Empty;
            }

            return stringBuilder.Insert(0, '?').ToString();
        }

        #endregion

        struct PlayerSubscription
        {
            public int Id { get; set; }
            public SubscriptionType Type { get; set; }
            public string DisplayName { get; set; }
            public string PermissionName { get; set; }
            public string IconUrl { get; set; }
            public DateTime ExpiresAt { get; set; }

            public enum SubscriptionType
            {
                Permission,
                Group
            }
        }

        struct PlayerCartItem
        {
            public int Id { get; set; }
            public ItemType Type { get; set; }
            public CardCommandExecutionType CommandExecutionType { get; set; }
            public CartBlueprintExecutionType BlueprintExecutionType { get; set; }
            public string[] Commands { get; set; }
            public int ItemId { get; set; }
            public float Condition { get; set; }
            public ulong SkinId { get; set; }
            public string IconUrl { get; set; }
            public int Amount { get; set; }

            public enum CartBlueprintExecutionType
            {
                IMMEDIATE,
                MANUAL
            }

            public enum CardCommandExecutionType
            {
                IMMEDIATE,
                ON_CONNECT,
                ON_RESPAWN,
                MANUAL
            }

            public enum ItemType
            {
                Command,
                Item,
                Blueprint
            }
        }

        class UiController : MonoBehaviour
        {
            const int ITEMS_PER_PAGE = 32;

            BasePlayer    _player;
            bool          _isOpen;
            UiBuilder.Tab _activeTab;
            int           _currentPage;

            public void gMonetize_OpenShop()
            {
                if (_isOpen)
                {
                    s_Instance.LogWarning("Got an OpenUi command, but the UI is already open");
                    return;
                }

                CuiHelper.AddUi(_player, UiBuilder.RenderMainUi());
                CuiHelper.AddUi(_player, UiBuilder.RenderTabLabels(_activeTab));

                if (_activeTab == UiBuilder.Tab.Items)
                {
                    CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());
                    _currentPage = 0;
                    ShowItemPage(0);
                }
                else
                {
                    throw new NotImplementedException();
                }
            }

            public void gMonetize_CloseShop()
            {
                if (!_isOpen)
                {
                    s_Instance.LogWarning("Got a CloseUi command, but the UI is not open");
                    return;
                }

                CuiHelper.DestroyUi(_player, UiBuilder.Names.Main.Container);
            }

            public void gMonetize_SwitchTab(UiBuilder.Tab tab)
            {
                if (_activeTab == tab)
                    return;

                if (tab == UiBuilder.Tab.Items)
                {
                    List<PlayerCartItem> items;
                    if (!s_Instance._playerCarts.TryGetValue(_player.UserIDString, out items))
                    {
                        items = new List<PlayerCartItem>();
                    }
                }
                else
                {
                    List<PlayerSubscription> subscriptions;
                    if (!s_Instance._subscriptionsPersistence.TryGetValue(
                            _player.UserIDString,
                            out subscriptions
                        ))
                    {
                        subscriptions = new List<PlayerSubscription>();
                    }
                }

                _activeTab = tab;
            }

            public void gMonetize_NextPage() { }

            public void gMonetize_PrevPage() { }

            public void gMonetize_ClaimItem()
            {
                if (_activeTab != UiBuilder.Tab.Items)
                    s_Instance.LogWarning(
                        "Received the OnItemClaim message, but the active tab was {0}",
                        _activeTab.ToString()
                    );

                /*re-render item grid*/
                throw new NotImplementedException();
            }

            void Start()
            {
                _player = GetComponent<BasePlayer>();
                _activeTab = UiBuilder.Tab.Items;
            }

            void ShowItemPage(int pageId)
            {
                List<PlayerCartItem> items;
                if (!s_Instance._playerCarts.TryGetValue(_player.UserIDString, out items))
                {
                    items = new List<PlayerCartItem>();
                }

                int pageCount = Mathf.CeilToInt((float)items.Count / ITEMS_PER_PAGE);

                if (pageId >= pageCount)
                {
                    s_Instance.LogWarning(
                        "Tried to render item page {0}, but there are only {1} pages, will render the last page instead",
                        pageId + 1,
                        pageCount
                    );
                    pageId = pageCount - 1;
                }

                var itemsToRender =
                    items.Skip(pageId * ITEMS_PER_PAGE).Take(ITEMS_PER_PAGE).ToArray();

                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());

                if (itemsToRender.Length == 0)
                {
                    /*render cart is empty text*/
                    CuiHelper.AddUi(_player, UiBuilder.RenderCartIsEmptyText());
                }
                else
                {
                    /*render item list*/
                    var renderedItems = new IEnumerable<CuiElement>[itemsToRender.Length];

                    for (var i = 0; i < itemsToRender.Length; i++)
                    {
                        var item = itemsToRender[i];

                        renderedItems[i] = UiBuilder.RenderItemCard(i, item);
                    }

                    CuiHelper.AddUi(
                        _player,
                        UiBuilder.ConvertToJson(renderedItems.SelectMany(x => x))
                    );
                }
            }
        }

        static class UiBuilder
        {
            readonly static CuiElement[] s_SingleElementArray = new CuiElement[1];

            static string
                s_MainUiCache; // contains main container, close button, tabswitcher container & divider

            static string s_ItemListContainerCache;
            static string s_ItemListIsEmptyLabelCache;


            #region Json Utilities

            public static string ConvertToJson(IEnumerable<CuiElement> elements)
            {
                return JsonUtility.ToJson(elements).Replace("\n\n", "\n");
            }

            public static string ConvertToJson(CuiElement element)
            {
                s_SingleElementArray[0] = element;
                return ConvertToJson(s_SingleElementArray);
            }

            #endregion

            public static string RenderMainUi()
            {
                if (s_MainUiCache == null)
                {
                    s_MainUiCache = ConvertToJson(
                        new CuiElementContainer {
                            new CuiElement {
                                Name = Names.Main.Container,
                                Parent = "Hud",
                                Components = {
                                    new CuiImageComponent {
                                        Material = Resources.Materials.UiBackgroundBlur,
                                        Color = Resources.Colors.BackgroundBase
                                    },
                                    new CuiRectTransformComponent {
                                        AnchorMin = "0.05 0.1", AnchorMax = "0.95 0.95"
                                    }
                                }
                            },
                            new CuiElement {
                                Name = Names.Main.ButtonClose,
                                Parent = Names.Main.Container,
                                Components = {
                                    new CuiButtonComponent {
                                        Color = Resources.Colors.RedBase,
                                        Command = "gmonetize.ui.cmd:close"
                                    },
                                    new CuiRectTransformComponent {
                                        AnchorMin = "0.95 0.95", AnchorMax = "1.0 1.0"
                                    }
                                }
                            },
                            new CuiElement {
                                Name = Names.TabSwitcher.Container,
                                Parent = Names.Main.Container,
                                Components = {
                                    new CuiImageComponent {
                                        Color = Resources.Colors.BackgroundSemiTransparent
                                    },
                                    new CuiRectTransformComponent {
                                        AnchorMin = "0.4 0.95", AnchorMax = "0.6 1.0"
                                    }
                                }
                            },
                            new CuiElement {
                                Name = Names.TabSwitcher.Divider,
                                Parent = Names.TabSwitcher.Container,
                                Components = {
                                    new CuiImageComponent {
                                        Color = Resources.Colors.TextSemiTransparent
                                    },
                                    new CuiRectTransformComponent {
                                        AnchorMin = "0.498 0", AnchorMax = "0.502 1"
                                    }
                                }
                            },
                        }
                    );
                }

                return s_MainUiCache;
            }

            public static string RenderTabLabels(Tab activeTab)
            {
                string itemsLabelColor, subscriptionsLabelColor;

                if (activeTab == Tab.Items)
                {
                    itemsLabelColor = Resources.Colors.TextBase;
                    subscriptionsLabelColor = Resources.Colors.TextBackground;
                }
                else
                {
                    itemsLabelColor = Resources.Colors.TextBackground;
                    subscriptionsLabelColor = Resources.Colors.TextBase;
                }

                return ConvertToJson(
                    new CuiElementContainer {
                        new CuiElement {
                            Name = Names.TabSwitcher.LabelItems,
                            Parent = Names.TabSwitcher.Container,
                            Components = {
                                new CuiTextComponent {
                                    Text = "ITEMS",
                                    Color = itemsLabelColor,
                                    Align = TextAnchor.MiddleCenter,
                                    FontSize = 16,
                                    Font = Resources.Fonts.RobotoCondensedRegular
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0", AnchorMax = "0.498 1"
                                }
                            }
                        },
                        new CuiElement {
                            Name = Names.TabSwitcher.LabelSubscriptions,
                            Parent = Names.TabSwitcher.Container,
                            Components = {
                                new CuiTextComponent {
                                    Text = "SUBSCRIPTIONS",
                                    Color = subscriptionsLabelColor,
                                    Align = TextAnchor.MiddleCenter,
                                    FontSize = 16,
                                    Font = Resources.Fonts.RobotoCondensedRegular
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0.502 0", AnchorMax = "1 1"
                                }
                            }
                        }
                    }
                );
            }

            public static string RenderItemListContainer()
            {
                if (s_ItemListContainerCache == null)
                {
                    s_ItemListContainerCache = ConvertToJson(
                        new CuiElement {
                            Name = Names.ItemList.Container,
                            Parent = Names.Main.Container,
                            Components = {
                                new CuiImageComponent {
                                    Color = Resources.Colors.BackgroundSemiTransparent
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0.02 0.03", AnchorMax = "0.98 0.92"
                                }
                            }
                        }
                    );
                }

                return s_ItemListContainerCache;
            }

            public static IEnumerable<CuiElement> RenderItemCard(
                int            uiItemId,
                PlayerCartItem itemData
            )
            {
                CuiRectTransformComponent rect = GetGridPosition(
                    uiItemId,
                    8,
                    4,
                    0.005f
                );

                List<CuiElement> list = new List<CuiElement> {
                    new CuiElement {
                        Name = Names.ItemList.Item.Container(uiItemId),
                        Parent = Names.ItemList.Container,
                        Components = {
                            new CuiImageComponent {
                                Material = Resources.Materials.UiBackgroundBlur,
                                Color = "0.4 0.4 0.4 0.5"
                            },
                            rect
                        }
                    },
                    new CuiElement {
                        Name = Names.ItemList.Item.Icon.Container(uiItemId),
                        Parent = Names.ItemList.Item.Container(uiItemId),
                        Components = {
                            new CuiImageComponent {
                                Color = Resources.Colors.BackgroundSemiTransparent
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.1 0.3", AnchorMax = "0.9 0.9"
                            }
                        }
                    },
                    new CuiElement {
                        Name = Names.ItemList.Item.ButtonClaim.Container(uiItemId),
                        Parent = Names.ItemList.Item.Container(uiItemId),
                        Components = {
                            new CuiButtonComponent {
                                Color = "0.2 0.6 0.2 0.6",
                                Command = "gmonetize.ui.claimitem:" + itemData.Id
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.1 0.1", AnchorMax = "0.9 0.27"
                            }
                        }
                    },
                    new CuiElement {
                        Name = Names.ItemList.Item.ButtonClaim.Text(uiItemId),
                        Parent = Names.ItemList.Item.ButtonClaim.Container(uiItemId),
                        Components = {
                            new CuiTextComponent {
                                Text = "CLAIM",
                                Color = "0.8 0.8 0.8 0.6",
                                Font = Resources.Fonts.RobotoCondensedRegular,
                                FontSize = 16,
                                Align = TextAnchor.MiddleCenter
                            },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }
                    }
                };

                list.AddRange(RenderItemIcon(uiItemId, itemData));

                return list;
            }

            public static IEnumerable<CuiElement> RenderItemIcon(
                int            uiItemId,
                PlayerCartItem itemData
            )
            {
                if (itemData.Type != PlayerCartItem.ItemType.Item)
                {
                    throw new NotImplementedException();
                }

                List<CuiElement> list = new List<CuiElement> {
                    new CuiElement {
                        Name = Names.ItemList.Item.Icon.Image(uiItemId),
                        Parent = Names.ItemList.Item.Icon.Container(uiItemId),
                        Components = {
                            new CuiImageComponent {
                                ItemId = itemData.ItemId, SkinId = itemData.SkinId
                            },
                            new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                        }
                    },
                };

                if (itemData.Amount > 1)
                {
                    list.Add(
                        new CuiElement {
                            Name = Names.ItemList.Item.Icon.LabelAmount(uiItemId),
                            Parent = Names.ItemList.Item.Icon.Container(uiItemId),
                            Components = {
                                new CuiTextComponent {
                                    Text = 'x' + itemData.Amount.ToString(),
                                    Font = Resources.Fonts.RobotoCondensedRegular,
                                    Color = "0.8 0.8 0.8 1"
                                },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0", AnchorMax = "1 1"
                                }
                            }
                        }
                    );
                }

                if (itemData.Condition != 0f && itemData.Condition != 1.0f)
                {
                    list.Add(
                        new CuiElement {
                            Name =
                                Names.ItemList.Item.Icon.BarCondition(uiItemId),
                            Parent = Names.ItemList.Item.Icon.Container(uiItemId),
                            Components = {
                                new CuiImageComponent { Color = "1 0 0 0.3" },
                                new CuiRectTransformComponent {
                                    AnchorMin = "0 0", AnchorMax = "1 " + itemData.Condition
                                }
                            }
                        }
                    );
                }

                return list;
            }

            public static string RenderCartIsEmptyText()
            {
                if (s_ItemListIsEmptyLabelCache == null)
                {
                    s_ItemListIsEmptyLabelCache = JsonUtility
                                                  .ToJson(
                                                      new[] {
                                                          new CuiElement {
                                                              Name = Names.ItemList.LabelIsEmpty,
                                                              Parent = Names.ItemList.Container,
                                                              Components = {
                                                                  new CuiTextComponent {
                                                                      Text = "Your cart is empty",
                                                                      Align = TextAnchor
                                                                          .MiddleCenter,
                                                                      FontSize = 18,
                                                                      Font = Resources.Fonts
                                                                          .RobotoCondensedRegular,
                                                                      Color = Resources.Colors
                                                                          .TextBackground
                                                                  }
                                                              }
                                                          }
                                                      }
                                                  )
                                                  .Replace("\n\n", "\n");
                }

                return s_ItemListIsEmptyLabelCache;
            }

            static CuiRectTransformComponent GetGridPosition(
                int   itemId,
                int   columns,
                int   rows,
                float gap
            )
            {
                float itemWidth = (1.0f - ((columns - 1) * gap)) / columns;
                float itemHeight = (1.0f - ((rows - 1) * gap)) / rows;

                int colNumber = itemId % columns;
                int rowNumber = Mathf.FloorToInt((float)itemId / columns);

                float itemX = itemWidth * colNumber + gap * colNumber;
                float itemY = 1 - (itemHeight * rowNumber + gap * (rowNumber - 1)) - itemHeight -
                              (gap * rowNumber);

                return new CuiRectTransformComponent {
                    AnchorMin = $"{itemX} {itemY}",
                    AnchorMax = $"{itemX + itemWidth} {itemY + itemHeight}"
                };
            }

            public static class Names
            {
                public static class Main
                {
                    public const string Container   = "gmonetize.ui::main/container";
                    public const string ButtonClose = "gmonetize.ui::main/button_close";
                }

                public static class TabSwitcher
                {
                    public const string Container  = "gmonetize.ui::tabswitcher/container";
                    public const string Divider    = "gmonetize.ui::tabswitcher/divider";
                    public const string LabelItems = "gmonetize.ui::tabswitcher/label_items";

                    public const string LabelSubscriptions =
                        "gmonetize.ui::tabswitcher/label_subscriptions";
                }

                public static class ItemList
                {
                    public const string Container    = "gmonetize.ui::itemlist/container";
                    public const string LabelIsEmpty = "gmonetize.ui::itemlist/label_isempty";

                    public static class Item
                    {
                        public static string Container(int id)
                        {
                            return "gmonetize.ui::itemlist/item/" + id + "/container";
                        }

                        public static class ButtonClaim
                        {
                            public static string Container(int id)
                            {
                                return "gmonetize.ui::itemlist/item" + id +
                                       "button_claim/container";
                            }

                            public static string Text(int id)
                            {
                                return "gmonetize.ui::itemlist/item" + id + "button_claim/text";
                            }
                        }

                        public static class Icon
                        {
                            public static string Container(int id)
                            {
                                return "gmonetize.ui::itemlist/item/" + id + "/icon/container";
                            }

                            public static string Image(int id)
                            {
                                return "gmonetize.ui::itemlist/item/" + id + "/icon/image";
                            }

                            public static string LabelAmount(int id)
                            {
                                return "gmonetize.ui::itemlist/item/" + id + "/icon/label_amount";
                            }

                            public static string BarCondition(int id)
                            {
                                return "gmonetize.ui::itemlist/item/" + id + "/icon/bar_condition";
                            }
                        }
                    }
                }
            }

            static class Resources
            {
                public static class Materials
                {
                    public const string UiBackgroundBlur = "assets/content/ui/uibackgroundblur.mat";
                }

                public static class Fonts
                {
                    public const string RobotoCondensedRegular = "robotocondensed-regular.ttf";
                }

                public static class Colors
                {
                    public const string TextBase            = "0.8 0.8 0.8 0.6";
                    public const string TextBackground      = "0.6 0.6 0.6 0.6";
                    public const string TextSemiTransparent = "0.6 0.6 0.6 0.6";

                    public const string BackgroundBase            = "0.4 0.4 0.4 0.6";
                    public const string BackgroundSemiTransparent = "0 0 0 0.25";
                    public const string GreenSemiTransparent      = "";
                    public const string RedBase                   = "0.6 0.2 0.2 0.8";
                    public const string RedSemiTransparent        = "";
                }
            }

            public enum Tab
            {
                Items,
                Subscriptions
            }
        }
    }
}
