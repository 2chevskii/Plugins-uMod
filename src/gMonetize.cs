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
    [Info("gMonetize", "2CHEVSKII", "0.1.0")]
    public class gMonetize : CovalencePlugin
    {
        readonly Dictionary<string, List<QueuedCommandItem>> _queuedCommands;
        readonly Dictionary<string, List<PurchasedItem>>     _cartData;

        string ApiBaseUrl { get; }
        string ApiToken { get; }
        float ApiRequestTimeout { get; }
        Dictionary<string, string> ApiHeaders { get; }


        [Conditional("DEBUG")]
        void LogDebug(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[GMONETIZE DEBUG]: " + format, args);
        }

        [Command("open")]
        void CmdOpenHandler(IPlayer player)
        {
            UiBuilder.CreateUi(player.Object as BasePlayer);
        }

        [Command("gmonetize.ui.cmd:close")]
        void CmdCloseHandler(IPlayer player)
        {
            UiBuilder.RemoveUi(player.Object as BasePlayer);
        }

        void OnPlayerConnected(BasePlayer player)
        {
            UpdateQueuedCommands(player, () =>
            {
                List<QueuedCommandItem> commands;

                if (!_queuedCommands.TryGetValue(player.UserIDString, out commands))
                {
                    return;
                }

                foreach (QueuedCommandItem command in commands.Where(c =>
                             c.Type == QueuedCommandItem.QueuedCommandType.ON_CONNECT ||
                             c.Type == QueuedCommandItem.QueuedCommandType.IMMEDIATE).ToArray())
                {
                    PostApiRequest<object, object>("/callbacks/claim-purchase",
                        new { id = command.Id, userId = player.UserIDString }, dataCallback:
                        _ =>
                        {
                            commands.Remove(command);
                            foreach (string commandData in command.Commands)
                            {
                                server.Command(commandData.Replace("${steamid}", player.UserIDString)
                                                          .Replace("${name}", player.displayName));
                            }
                        });
                }
            });
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            List<QueuedCommandItem> commands;

            if (!_queuedCommands.TryGetValue(player.UserIDString, out commands))
            {
                return;
            }

            foreach (QueuedCommandItem command in commands.Where(c =>
                         c.Type == QueuedCommandItem.QueuedCommandType.ON_RESPAWN ||
                         c.Type == QueuedCommandItem.QueuedCommandType.IMMEDIATE).ToArray())
            {
                PostApiRequest<object, object>("/callbacks/claim-purchase",
                    new { id = command.Id, userId = player.UserIDString }, dataCallback:
                    _ =>
                    {
                        commands.Remove(command);
                        foreach (string commandData in command.Commands)
                        {
                            server.Command(commandData.Replace("${steamid}", player.UserIDString)
                                                      .Replace("${name}", player.displayName));
                        }
                    });
            }
        }

        void OnServerInitialized()
        {
            timer.Every(30f, () =>
            {
                UpdateQueuedCommands();
                UpdatePlayerCarts();
            });
        }

        void UpdateQueuedCommands()
        {
            PostApiRequest<Dictionary<string, List<QueuedCommandItem>>, object>("/commands",
                BasePlayer.activePlayerList.Select(x => x.UserIDString), dataCallback:
                commands =>
                {
                    foreach (KeyValuePair<string, List<QueuedCommandItem>> kv in commands)
                    {
                        _queuedCommands[kv.Key] = kv.Value;
                    }
                });
        }

        void UpdateQueuedCommands(BasePlayer player, Action callback)
        {
            /*update commands for specific player*/

            PostApiRequest<Dictionary<string, List<QueuedCommandItem>>, object>("/commands",
                new[] { player.UserIDString },
                dataCallback: commands =>
                {
                    foreach (KeyValuePair<string, List<QueuedCommandItem>> kv in commands)
                    {
                        _queuedCommands[kv.Key] = kv.Value;
                    }

                    if (callback != null)
                        callback();
                });
        }

        void UpdatePlayerCarts()
        {
            PostApiRequest<Dictionary<string, List<PurchasedItem>>, IEnumerable<string>>("/items",
                BasePlayer.activePlayerList.Select(x => x.UserIDString), dataCallback:
                items => { });
        }

        #region Request helpers

        void PostApiRequest<TData>(string        path,                Dictionary<string, object> queryParameters = null,
                                   Action<TData> dataCallback = null, Action<int>                errorCallback   = null)
            => PostApiRequest<TData, object>(path, null, queryParameters, dataCallback, errorCallback);

        void PostApiRequest<TData, TBody>(string path, TBody body, Dictionary<string, object> queryParameters = null,
                                          Action<TData> dataCallback = null, Action<int> errorCallback = null)
        {
            string requestUrl = BuildRequestUrl(path, queryParameters);

            webrequest.Enqueue(requestUrl, JsonConvert.SerializeObject(body), delegate(int code, string response)
                {
                    if (code >= 200 && code < 300)
                    {
                        dataCallback?.Invoke(JsonConvert.DeserializeObject<TData>(response));
                    }
                    else
                    {
                        errorCallback?.Invoke(code);
                    }
                }, this, RequestMethod.POST, ApiHeaders,
                ApiRequestTimeout);
        }

        void GetApiRequest<TData>(string        path, Dictionary<string, object> queryParameters = null,
                                  Action<TData> dataCallback  = null,
                                  Action<int>   errorCallback = null)
        {
            string requestUrl = BuildRequestUrl(path, queryParameters);

            webrequest.Enqueue(requestUrl, null, delegate(int code, string response)
                {
                    if (code >= 200 && code < 300)
                    {
                        dataCallback?.Invoke(JsonConvert.DeserializeObject<TData>(response));
                    }
                    else
                    {
                        errorCallback?.Invoke(code);
                    }
                }, this, headers: ApiHeaders,
                timeout: ApiRequestTimeout);
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

            foreach (var pair in parameters)
            {
                string paramName = pair.Key;
                object paramValue = pair.Value;

                IEnumerable paramValueEnumerable = paramValue as IEnumerable;

                string paramValueString;

                if (paramValueEnumerable != null)
                {
                    List<string> paramValues = new List<string>();

                    foreach (var obj in paramValueEnumerable)
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

        abstract class PurchasedItem
        {
            public int Id { get; set; }
        }

        class PurchasedCommandItem : PurchasedItem
        {
            public string[] Commands { get; set; }
        }

        class QueuedCommandItem : PurchasedCommandItem
        {
            public QueuedCommandType Type { get; set; }

            public enum QueuedCommandType
            {
                IMMEDIATE,
                ON_CONNECT,
                ON_RESPAWN
            }
        }

        class PurchasedGameItem : PurchasedItem
        {
            public int ItemId { get; set; }
            public float Condition { get; set; }
            public ulong SkinId { get; set; }
        }

        public static class UiBuilder
        {
            const string PANEL_MATERIAL = "assets/content/ui/uibackgroundblur.mat";
            const string FONT_NAME      = "robotocondensed-regular.ttf";

            const string PANEL_MAIN_BG                   = "gmonetize.ui.main.bg";
            const string BUTTON_MAIN_CLOSE               = "gmonetize.ui.main.button_close";
            const string PANEL_TABSWITCHER_CONTAINER     = "gmonetize.ui.tabswitcher.container";
            const string PANEL_TABSWITCHER_DIVIDER       = "gmonetize.ui.tabswitcher.divider";
            const string LABEL_TABSWITCHER_ITEMS         = "gmonetize.ui.tabswitcher.label_items";
            const string LABEL_TABSWITCHER_SUBSCRIPTIONS = "gmonetize.ui.tabswitcher.label_subscriptions";
            const string PANEL_ITEMLIST_CONTAINER        = "gmonetize.ui.itemlist.container";
            const string PANEL_ITEM_CONTAINER            = "gmonetize.ui.itemlist.item[{itemid}].container";
            const string PANEL_ITEM_ICON_CONTAINER       = "gmonetize.ui.itemlist.item[{itemid}].icon.container";
            const string IMAGE_ITEM_ICON_ICON            = "gmonetize.ui.itemlist.item[{itemid}].icon.icon";
            const string LABEL_ITEM_ICON_AMOUNT          = "gmonetize.ui.itemlist.item[{itemid}].icon.label_amount";
            const string IMAGE_ITEM_ICON_CONDITION       = "gmonetize.ui.itemlist.item[{itemid}].icon.condition";
            const string BUTTON_ITEM_CLAIM               = "gmonetize.ui.itemlist.item[{itemid}].button_claim";
            const string LABEL_ITEM_BUTTON_CLAIM         = "gmonetize.ui.itemlist.item[{itemid}].button_claim.label";

            static readonly string s_uiCacheMainBg;

            static UiBuilder()
            {
                /*build and cache static elements*/
                s_uiCacheMainBg = CuiHelper.ToJson(new CuiElementContainer
                                                   {
                                                       new CuiElement
                                                       {
                                                           Name = PANEL_MAIN_BG,
                                                           Parent = "Hud",
                                                           Components =
                                                           {
                                                               new CuiImageComponent
                                                               {
                                                                   Material = PANEL_MATERIAL,
                                                                   Color = "0.4 0.4 0.4 0.6"
                                                               },
                                                               new CuiRectTransformComponent
                                                               {
                                                                   AnchorMin = "0.05 0.1",
                                                                   AnchorMax = "0.95 0.95"
                                                               }
                                                           }
                                                       },
                                                       new CuiElement
                                                       {
                                                           Name = BUTTON_MAIN_CLOSE,
                                                           Parent = PANEL_MAIN_BG,
                                                           Components =
                                                           {
                                                               new CuiButtonComponent
                                                               {
                                                                   Color = "0.6 0.2 0.2 0.8",
                                                                   Command = "gmonetize.ui.cmd:close"
                                                               },
                                                               new CuiRectTransformComponent
                                                               {
                                                                   AnchorMin = "0.95 0.95",
                                                                   AnchorMax = "1.0 1.0"
                                                               }
                                                           }
                                                       },
                                                       new CuiElement
                                                       {
                                                           Name = PANEL_TABSWITCHER_CONTAINER,
                                                           Parent = PANEL_MAIN_BG,
                                                           Components =
                                                           {
                                                               new CuiImageComponent
                                                               {
                                                                   Color = "0 0 0 0.4"
                                                               },
                                                               new CuiRectTransformComponent
                                                               {
                                                                   AnchorMin = "0.4 0.95",
                                                                   AnchorMax = "0.6 1.0"
                                                               }
                                                           }
                                                       },
                                                       new CuiElement
                                                       {
                                                           Name = PANEL_TABSWITCHER_DIVIDER,
                                                           Parent = PANEL_TABSWITCHER_CONTAINER,
                                                           Components =
                                                           {
                                                               new CuiImageComponent { Color = "0.6 0.6 0.6 0.6" },
                                                               new CuiRectTransformComponent
                                                               {
                                                                   AnchorMin = "0.498 0",
                                                                   AnchorMax = "0.502 1"
                                                               }
                                                           }
                                                       },
                                                   });
            }

            public static string RenderTabLabels(Tab tab)
            {
                string itemsLabelColor, subscriptionsLabelColor;

                itemsLabelColor = tab == Tab.Items ? "0.8 0.8 0.8 0.6" : "0.6 0.6 0.6 0.6";
                subscriptionsLabelColor = tab == Tab.Subscriptions ? "0.8 0.8 0.8 0.6" : "0.6 0.6 0.6 0.6";

                return CuiHelper.ToJson(new CuiElementContainer
                                        {
                                            new CuiElement
                                            {
                                                Name = LABEL_TABSWITCHER_ITEMS,
                                                Parent = PANEL_TABSWITCHER_CONTAINER,
                                                Components =
                                                {
                                                    new CuiTextComponent
                                                    {
                                                        Text = "ITEMS",
                                                        Color = itemsLabelColor,
                                                        Align = TextAnchor.MiddleCenter,
                                                        FontSize = 16,
                                                        Font = FONT_NAME
                                                    },
                                                    new CuiRectTransformComponent
                                                    {
                                                        AnchorMin = "0 0",
                                                        AnchorMax = "0.498 1"
                                                    }
                                                }
                                            },
                                            new CuiElement
                                            {
                                                Name = LABEL_TABSWITCHER_SUBSCRIPTIONS,
                                                Parent = PANEL_TABSWITCHER_CONTAINER,
                                                Components =
                                                {
                                                    new CuiTextComponent
                                                    {
                                                        Text = "SUBSCRIPTIONS",
                                                        Color = subscriptionsLabelColor,
                                                        Align = TextAnchor.MiddleCenter,
                                                        FontSize = 16,
                                                        Font = FONT_NAME
                                                    },
                                                    new CuiRectTransformComponent
                                                    {
                                                        AnchorMin = "0.502 0",
                                                        AnchorMax = "1 1"
                                                    }
                                                }
                                            }
                                        });
            }

            static CuiElement RenderGridContainer()
            {
                return new CuiElement
                       {
                           Name = PANEL_ITEMLIST_CONTAINER,
                           Parent = PANEL_MAIN_BG,
                           Components =
                           {
                               new CuiImageComponent
                               {
                                   Color = "0 0 0 0.25"
                               },
                               new CuiRectTransformComponent
                               {
                                   AnchorMin = "0.02 0.03",
                                   AnchorMax = "0.98 0.92"
                               }
                           }
                       };
            }

            public static IEnumerable<CuiElement> RenderItemCard(int uiItemId, ItemData itemData,
                                                                 int totalItemCount)
            {
                // var rect = GetCardGridRect(GetFlexPosition(uiItemId,totalItemCount, cardwidth,cardheight, 0.05f));

                var rect = GetGridPosition(uiItemId, 8, 4, 0.005f);

                Interface.Oxide.LogInfo(JsonConvert.SerializeObject(rect, Formatting.Indented));

                var list = new List<CuiElement>
                           {
                               new CuiElement
                               {
                                   Name = PANEL_ITEM_CONTAINER.Replace("{itemid}", uiItemId.ToString()),
                                   Parent = PANEL_ITEMLIST_CONTAINER,
                                   Components =
                                   {
                                       new CuiImageComponent
                                       {
                                           Material = PANEL_MATERIAL,
                                           Color = "0.4 0.4 0.4 0.5"
                                       },
                                       rect
                                   }
                               },
                               new CuiElement
                               {
                                   Name = PANEL_ITEM_ICON_CONTAINER.Replace("{itemid}", uiItemId.ToString()),
                                   Parent = PANEL_ITEM_CONTAINER.Replace("{itemid}", uiItemId.ToString()),
                                   Components =
                                   {
                                       new CuiImageComponent
                                       {
                                           Color = "0 0 0 0.2"
                                       },
                                       new CuiRectTransformComponent
                                       {
                                           AnchorMin = "0.1 0.3",
                                           AnchorMax = "0.9 0.9"
                                       }
                                   }
                               },
                               new CuiElement
                               {
                                   Name = BUTTON_ITEM_CLAIM.Replace("{itemid}", uiItemId.ToString()),
                                   Parent = PANEL_ITEM_CONTAINER.Replace("{itemid}", uiItemId.ToString()),
                                   Components =
                                   {
                                       new CuiButtonComponent
                                       {
                                           Color = "0.2 0.6 0.2 0.6",
                                           Command = "gmonetize.ui.claimitem:" + uiItemId
                                       },
                                       new CuiRectTransformComponent
                                       {
                                           AnchorMin = "0.1 0.1",
                                           AnchorMax = "0.9 0.27"
                                       }
                                   }
                               },
                               new CuiElement
                               {
                                   Name = LABEL_ITEM_BUTTON_CLAIM.Replace("{itemid}", uiItemId.ToString()),
                                   Parent = BUTTON_ITEM_CLAIM.Replace("{itemid}", uiItemId.ToString()),
                                   Components =
                                   {
                                       new CuiTextComponent
                                       {
                                           Text = "CLAIM",
                                           Color = "0.8 0.8 0.8 0.6",
                                           Font = FONT_NAME,
                                           FontSize = 16,
                                           Align = TextAnchor.MiddleCenter
                                       },
                                       new CuiRectTransformComponent
                                       {
                                           AnchorMin = "0 0",
                                           AnchorMax = "1 1"
                                       }
                                   }
                               }
                           };

                list.AddRange(RenderItemIcon(uiItemId, itemData));

                return list;
            }

            static CuiRectTransformComponent GetGridPosition(int itemId, int columns, int rows, float gap)
            {
                float itemWidth = (1.0f - ((columns - 1) * gap)) / columns;
                float itemHeight = (1.0f - ((rows - 1) * gap)) / rows;

                int colNumber = itemId % columns;
                int rowNumber = Mathf.FloorToInt((float)itemId / columns);

                float itemX = itemWidth * colNumber + gap * colNumber;
                float itemY = 1 - (itemHeight * rowNumber + gap * (rowNumber - 1)) - itemHeight - (gap * rowNumber);

                return new CuiRectTransformComponent
                       {
                           AnchorMin = $"{itemX} {itemY}",
                           AnchorMax = $"{itemX + itemWidth} {itemY + itemHeight}"
                       };
            }

            public static IEnumerable<CuiElement> RenderItemIcon(int uiItemId, ItemData itemData)
            {
                const string placeholder = "{itemid}";
                string parent = PANEL_ITEM_ICON_CONTAINER.Replace(placeholder, uiItemId.ToString());

                if (itemData.Type != ItemData.ItemType.Item)
                {
                    throw new NotImplementedException();
                }

                var list = new List<CuiElement>
                           {
                               new CuiElement
                               {
                                   Name = IMAGE_ITEM_ICON_ICON.Replace(placeholder, uiItemId.ToString()),
                                   Parent = parent,
                                   Components =
                                   {
                                       new CuiImageComponent
                                       {
                                           ItemId = itemData.ItemId,
                                           SkinId = itemData.SkindId
                                       },
                                       new CuiRectTransformComponent
                                       {
                                           AnchorMin = "0 0",
                                           AnchorMax = "1 1"
                                       }
                                   }
                               },
                           };

                if (itemData.Amount > 1)
                {
                    list.Add(new CuiElement
                             {
                                 Name = LABEL_ITEM_ICON_AMOUNT.Replace(placeholder, uiItemId.ToString()),
                                 Parent = parent,
                                 Components =
                                 {
                                     new CuiTextComponent
                                     {
                                         Text = 'x' + itemData.Amount.ToString(),
                                         Font = FONT_NAME,
                                         Color = "0.8 0.8 0.8 1"
                                     },
                                     new CuiRectTransformComponent
                                     {
                                         AnchorMin = "0 0",
                                         AnchorMax = "1 1"
                                     }
                                 }
                             });
                }

                if (itemData.Condition != 0f && itemData.Condition != 1.0f)
                {
                    list.Add(new CuiElement
                             {
                                 Name = IMAGE_ITEM_ICON_CONDITION.Replace(placeholder, uiItemId.ToString()),
                                 Parent = parent,
                                 Components =
                                 {
                                     new CuiImageComponent
                                     {
                                         Color = "1 0 0 0.3"
                                     },
                                     new CuiRectTransformComponent
                                     {
                                         AnchorMin = "0 0",
                                         AnchorMax = "1 " + itemData.Condition
                                     }
                                 }
                             });
                }

                return list;
            }


            public static void CreateUi(BasePlayer player)
            {
                CuiHelper.AddUi(player, s_uiCacheMainBg);
                CuiHelper.AddUi(player, RenderTabLabels(Tab.Items));
                CuiHelper.AddUi(player, CuiHelper.ToJson(new List<CuiElement> { RenderGridContainer() }));
                var items = player.inventory.AllItems();

                int pos = 0;

                foreach (var item in items)
                {
                    var itemdata = new ItemData
                                   {
                                       ItemId = item.info.itemid,
                                       Condition = item.conditionNormalized,
                                       Amount = item.amount,
                                       Type = ItemData.ItemType.Item,
                                       SkindId = item.skin,
                                   };

                    var card = RenderItemCard(pos++, itemdata, items.Length);

                    var j = CuiHelper.ToJson(card.ToList());

                    // Interface.Oxide.LogInfo(j);

                    CuiHelper.AddUi(player, j);
                }
            }

            public static void RemoveUi(BasePlayer player)
            {
                CuiHelper.DestroyUi(player, PANEL_MAIN_BG);
            }

            public enum Tab
            {
                Items,
                Subscriptions
            }
        }

        public struct ItemData
        {
            public ItemType Type { get; set; }
            public string PermissionName { get; set; }
            public string[] CommandNames { get; set; }
            public int ItemId { get; set; }
            public ulong SkindId { get; set; }
            public int Amount { get; set; }
            public float Condition { get; set; }

            public enum ItemType
            {
                Item,
                Command,
                Subscription
            }
        }
    }
}
