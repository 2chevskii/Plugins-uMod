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
        Dictionary<string, List<PlayerCartItem>> _playerCarts;

        static gMonetize s_Instance;

        string ApiBaseUrl { get; } = "https://gmonetize.ru/api/v2/customer";
        string ApiToken { get; } = "changeme";
        float ApiRequestTimeout { get; } = 5f;

        Dictionary<string, string> ApiHeaders { get; } = new Dictionary<string, string> {
            { "Authorization", "ApiKey changeme" }
        };


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

        #region Command handlers

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

        #endregion

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

        struct PlayerCartItem
        {
            public string Id { get; set; }
            public string IconId { get; set; }
            public CartItem[] Items { get; set; }
            public CartCommand[] Commands { get; set; }
        }

        struct CartItem
        {
            public string ItemId { get; set; }
            public uint Amount { get; set; }
        }

        struct CartCommand
        {
            public string Value { get; set; }
        }

        class UiController : MonoBehaviour
        {
            const int ITEMS_PER_PAGE = 32;

            BasePlayer           _player;
            bool                 _isOpen;
            int                  _currentPage;
            List<PlayerCartItem> _items;

            /*tabs are unused with current backend*/
            // UiBuilder.Tab        _activeTab;

            int PageCount => Mathf.CeilToInt((float)_items.Count / ITEMS_PER_PAGE);

            public void gMonetize_OpenShop()
            {
                if (_isOpen)
                {
                    s_Instance.LogWarning(
                        "Got an OpenUi command, but the UI is already open, refreshing"
                    );
                    RefreshUi();
                    return;
                }

                ShowMainUi();
                ShowLoader();

                s_Instance.SendFetchItemsRequest(
                    _player,
                    items =>
                    {
                        if (_isOpen)
                        {
                            _items.Clear();
                            _items.AddRange(items);
                            RemoveLoader();
                            _currentPage = 0;
                            ShowItemPage();
                        }
                        else
                        {
                            s_Instance.LogDebug(
                                "UI was closed earlier than item load was finished"
                            );
                        }
                    },
                    error =>
                    {
                        RemoveLoader();
                        ShowLoadError();
                    }
                );
            }

            public void gMonetize_RefreshUi()
            {
                if (!_isOpen)
                    throw new InvalidOperationException("RefreshUi called, but _isOpen=false");

                CuiHelper.DestroyUi(_player, UiBuilder.Names.ItemList.Container);
                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());
                ShowItemPage();
            }

            public void gMonetize_NextPage()
            {
                CuiHelper.DestroyUi(_player, UiBuilder.Names.ItemList.Container);
                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());

                if (PageCount == 0)
                {
                    ShowNoItemsLabel();
                }
                else if(_currentPage == PageCount-1)
                {
                    s_Instance.LogDebug("NextPage called, but already on last page");
                    RefreshUi();
                }
                else
                {
                    _currentPage++;
                    ShowItemsPage();
                }
            }

            public void gMonetize_PrevPage()
            {
                CuiHelper.DestroyUi(_player, UiBuilder.Names.ItemList.Container);
                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());

                if (PageCount == 0)
                {
                    ShowNoItemsLabel();
                } else if (_currentPage == 0)
                {
                    RefreshUi();
                }
                else
                {
                    _currentPage--;
                    ShowItemsPage();
                }
            }

            public void gMonetize_CloseUi()
            {

            }

            public void gMonetize_ClaimItem(string itemId) { }

            void ShowMainUi()
            {
                if (_isOpen)
                    return;

                CuiHelper.AddUi(_player, UiBuilder.RenderMainUi());
                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());
            }

            void ShowItemPage()
            {
                CuiHelper.AddUi(_player, UiBuilder.RenderItemListContainer());

                if (!_items.Any())
                {
                    s_Instance.LogDebug("No items found for user {0}", _player.UserIDString);
                    /*render no items text and force set current page to 0*/
                    _currentPage = 0;
                    CuiHelper.AddUi(
                        _player,
                        UiBuilder.RenderStatusLabel("No items :/", false)
                    );
                    return;
                }

                int pageCount = Mathf.CeilToInt((float)_items.Count / ITEMS_PER_PAGE);

                if (_currentPage >= pageCount)
                {
                    int targetPageIndex = pageCount - 1;
                    s_Instance.LogDebug(
                        "Page count is {0}, but current page index is {1}. Correcting to {2} (user: {3})",
                        pageCount,
                        _currentPage,
                        targetPageIndex,
                        _player.UserIDString
                    );
                }

                int itemsToSkipCount = ITEMS_PER_PAGE * _currentPage;
                var itemsToRender = _items.Skip(itemsToSkipCount).Take(ITEMS_PER_PAGE).ToArray();

                for (var i = 0; i < itemsToRender.Length; i++)
                {
                    var item = itemsToRender[i];

                    var card = UiBuilder.RenderItemCard(i, item);
                    CuiHelper.AddUi(_player, UiBuilder.ConvertToJson(card));
                }
            }

            void ShowLoader()
            {
                CuiHelper.AddUi(_player, UiBuilder.RenderStatusLabel("Loading items...", false));
            }

            void RemoveLoader()
            {
                CuiHelper.DestroyUi(_player, UiBuilder.Names.ItemList.LabelStatus);
            }

            void ShowLoadError()
            {
                CuiHelper.AddUi(
                    _player,
                    UiBuilder.RenderStatusLabel("Failed to load items :(", true)
                );
            }

            #region Unity messages

            void Start()
            {
                _player = GetComponent<BasePlayer>();
                _activeTab = UiBuilder.Tab.Items;
            }

            void OnDestroy()
            {
                /*remove ui if exists*/
            }

            #endregion
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

            /*public static string RenderTabLabels(Tab activeTab)
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
            }*/

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

            public static string RenderStatusLabel(string status, bool isError)
            {
                return JsonUtility.ToJson(
                                      new[] {
                                          new CuiElement {
                                              Name = Names.ItemList.LabelStatus,
                                              Parent = Names.ItemList.Container,
                                              Components = {
                                                  new CuiTextComponent {
                                                      Text = status,
                                                      Align = TextAnchor.MiddleCenter,
                                                      FontSize = 18,
                                                      Font = Resources.Fonts.RobotoCondensedRegular,
                                                      Color = !isError
                                                          ? Resources.Colors.TextBackground
                                                          : Resources.Colors.RedBase
                                                  }
                                              }
                                          }
                                      }
                                  )
                                  .Replace("\n\n", "\n");
            }

            /*public static string RenderCartIsEmptyText()
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
            }*/

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
                    public const string Container   = "gmonetize.ui::itemlist/container";
                    public const string LabelStatus = "gmonetize.ui::itemlist/label_status";

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

            /*public enum Tab
            {
                Items,
                Subscriptions
            }*/
        }
    }
}
