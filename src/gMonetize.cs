using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Epic.OnlineServices.KWS;

using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info( "gMonetize", "2CHEVSKII", "0.1.0" )]
    public class gMonetize : CovalencePlugin
    {
        private const string API_BASE_URL = "https://localhost:4000";

        private PluginSettings _settings;

        /*
         * Lifecycle callbacks:
         * On server went online
         * On server went offline
         * Alive-tick
         *
         */

        private bool HasToken => !string.IsNullOrEmpty( _settings.Token );

#region Hooks

        void OnServerInitialized()
        {
            if ( !HasToken )
            {
                LogWarning( "Configuration:Token is empty, plugin will not be functional" );

                return;
            }

            /*Send callback on server online*/
            string onlineRequestUrl = GetRequestUrl( "/on-server-online" );
            webrequest.Enqueue(
                onlineRequestUrl,
                string.Empty,
                (code, body) => { Log( "OnServerOnline response: [{0}]\n{1}", code, body ); },
                this,
                RequestMethod.POST,
                CreateHeaders()
            );

            timer.Every(
                60,
                () => {
                    /*send alive callbacks*/
                }
            );
        }

        /*void Unload()
        {
            if ( !HasToken )
                return;

            /*send offline callback#1#
            string offlineRequestUrl = GetRequestUrl( "/on-server-offline" );

            ManualResetEventSlim resetEvent = new ManualResetEventSlim( false );
            webrequest.Enqueue(
                offlineRequestUrl,
                string.Empty,
                (code, body) => { resetEvent.Reset(); },
                this,
                RequestMethod.POST,
                CreateHeaders()
            );

            Log( "Waiting for callback to finish" );
            resetEvent.Wait();
            Log( "Callback finished" );
        }*/

#endregion

#region Request utilities

        private string GetRequestUrl(string path, Dictionary<string, object> query = null)
        {
            string queryString = query != null ? CreateQueryString( query ) : string.Empty;

            string url = API_BASE_URL + path + queryString;

            return url;
        }

        private string CreateQueryString(Dictionary<string, object> query)
        {
            if ( query.Count == 0 )
                return string.Empty;

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append( '?' );

            stringBuilder.Append( FormatQueryParameter( query.First() ) );

            foreach ( KeyValuePair<string, object> kvp in query.Skip( 1 ) )
            {
                stringBuilder.Append( '&' );
                stringBuilder.Append( FormatQueryParameter( kvp ) );
            }

            return stringBuilder.ToString();
        }

        private string FormatQueryParameter(KeyValuePair<string, object> parameter)
        {
            string name  = parameter.Key;
            object value = parameter.Value;

            if ( value is IEnumerable )
            {
                return string.Join(
                    "&",
                    ((IEnumerable) value).Cast<object>().Select( x => $"{name}={x}" )
                );
            }

            string strValue = value.ToString();

            return $"{name}={strValue}";
        }

        private Dictionary<string, string> CreateHeaders(
            params KeyValuePair<string, string>[] additionalHeaders
        )
        {
            var dict = new Dictionary<string, string>( additionalHeaders );
            dict.Add( "Authorization", "Bearer " + _settings.Token );

            return dict;
        }

        private void SendGetRequest(string fullRequestPath, Action<int, string> callback)
        {
            webrequest.Enqueue( fullRequestPath, string.Empty, callback, this );
        }

        private void SendPostRequest(
            string fullRequestPath,
            string body,
            Action<int, string> callback
        )
        {
            webrequest.Enqueue( fullRequestPath, body, callback, this );
        }

        private void SendPostRequest(string fullRequestPath, Action<int, string> callback) =>
        SendPostRequest( fullRequestPath, string.Empty, callback );

#endregion

#region Configuration

        protected override void LoadDefaultConfig()
        {
            _settings = PluginSettings.Default;
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject( _settings );

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                _settings = Config.ReadObject<PluginSettings>();
            }
            catch (Exception e)
            {
                LogError( "Failed to load configuration: {0}", e.Message );

                LoadDefaultConfig();
            }
        }

        private class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings();

            public string Token { get; set; }
        }

#endregion

#region Request players cart data

        void LoadCart(IPlayer player, Action<object> callback)
        {
            var steamId = player.Id;

            const string defaultRequestPath = "/items/{steamId}";

            string requestPath = defaultRequestPath.Replace( "{steamId}", steamId );

            webrequest.Enqueue(
                API_BASE_URL + requestPath,
                string.Empty,
                (code, json) => { },
                this
            );
        }

#endregion

        [Command( "test" )]
        void CmdTest(IPlayer player)
        {
            Log( "Test command" );
            CuiElementContainer elements = Ui.RenderStaticElements();

            CuiElementContainer pag = Ui.RenderPaginationLabels( 42, 112 );
            CuiElementContainer sel = Ui.RenderTabSelectionButtons( Ui.Tab.Subscriptions );

            var bp = (BasePlayer) player.Object;

            CuiHelper.AddUi( bp, elements );
            CuiHelper.AddUi( bp, pag );
            CuiHelper.AddUi( bp, sel );

            Log( "Test command end" );
        }

        Item CreateItemFromItemData(ItemData itemData)
        {
            var itemDefinition = ItemManager.FindItemDefinition( itemData.ItemId );

            var item = ItemManager.Create( itemDefinition, itemData.Amount, itemData.SkinId );

            if ( item.hasCondition )
            {
                item.conditionNormalized = itemData.Condition;
            }

            return item;
        }

        Item[] CreateItemsFromItemDataArray(ItemData[] data)
        {
            Item[] array = new Item[data.Length];

            for ( var i = 0; i < data.Length; i++ )
            {
                var item = CreateItemFromItemData( data[i] );
                array[i] = item;
            }

            return array;
        }

        bool PlayerReceiveItems(BasePlayer player, ItemData[] data)
        {
            if ( GetAvailableInventorySlots( player ) < data.Length )
            {
                return false;
            }

            var items = CreateItemsFromItemDataArray( data );

            for ( var i = 0; i < items.Length; i++ )
            {
                var item = items[i];

                player.GiveItem( item, BaseEntity.GiveItemReason.PickedUp );
            }

            return true;
        }

        int GetAvailableInventorySlots(BasePlayer player)
        {
            var mainSlots = player.inventory.containerMain.capacity -
                            player.inventory.containerMain.itemList.Count;

            var beltSlots = player.inventory.containerBelt.capacity -
                            player.inventory.containerBelt.itemList.Count;

            return mainSlots + beltSlots;
        }

        public struct ItemData
        {
            public int   ItemId;
            public int   Amount;
            public float Condition;
            public ulong SkinId;
        }

        public struct CartData
        {
            public ItemData[] Items;
        }

        public static class Ui
        {
            private const string MATERIAL_BLUR = "assets/content/ui/uibackgroundblur.mat";

            static readonly CuiRectTransformComponent s_rectTransformZeroOne =
            new CuiRectTransformComponent {
                AnchorMin = "0 0",
                AnchorMax = "1 1"
            };

            static CuiElement RenderMainContainer()
            {
                return new CuiElement {
                    Name = "gmonetize.main.container",
                    Parent = "Hud",
                    Components = {
                        new CuiImageComponent {
                            Material = MATERIAL_BLUR,
                            Color = "0.4 0.4 0.4 0.7"
                        },
                        new CuiRectTransformComponent {
                            AnchorMin = "0.5 0.5",
                            AnchorMax = "0.5 0.5",
                            OffsetMin = "-850 -450",
                            OffsetMax = "850 450"
                        }
                    }
                };
            }

            static IEnumerable<CuiElement> RenderTabSwitcher(Tab activeTab)
            {
                const string colorActive  = "1 1 1 0.7";
                const string colorDefault = "1 1 1 0.4";

                string itemsColor,
                       subsColor;

                if ( activeTab == Tab.Items )
                {
                    itemsColor = colorActive;
                    subsColor = colorDefault;
                }
                else
                {
                    itemsColor = colorDefault;
                    subsColor = colorActive;
                }

                CuiImageComponent emptyImage = new CuiImageComponent {
                    Color = "0 0 0 0"
                };

                return new[] {
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.container",
                        Parent = "gmonetize.main.container",
                        Components = {
                            new CuiImageComponent {
                                Color = "0.2 0.2 0.2 0.4"
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.4 0.9",
                                AnchorMax = "0.6 1.0"
                            }
                        }
                    },
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.divider",
                        Parent = "gmonetize.tabswitcher.container",
                        Components = {
                            new CuiImageComponent {
                                Color = "1 1 1 0.4"
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0.5 0",
                                AnchorMax = "0.5 1",
                                OffsetMin = "-2 0",
                                OffsetMax = "2 0"
                            }
                        }
                    },
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.items.container",
                        Parent = "gmonetize.tabswitcher.container",
                        Components = {
                            emptyImage,
                            new CuiRectTransformComponent {
                                AnchorMin = "0.5 0",
                                AnchorMax = "1 1"
                            }
                        }
                    },
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.subscriptions.container",
                        Parent = "gmonetize.tabswitcher.container",
                        Components = {
                            emptyImage,
                            new CuiRectTransformComponent {
                                AnchorMin = "0 0",
                                AnchorMax = "0.5 1"
                            }
                        }
                    },
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.items.label",
                        Parent = "gmonetize.tabswitcher.items.container",
                        Components = {
                            new CuiTextComponent {
                                Color = itemsColor, // get active color
                                Text = "ITEMS"
                            },
                            s_rectTransformZeroOne
                        }
                    },
                    new CuiElement {
                        Name = "gmonetize.tabswitcher.subscriptions.label",
                        Parent = "gmonetize.tabswitcher.subscriptions.container",
                        Components = {
                            new CuiTextComponent {
                                Color = subsColor, //get active color,
                                Text = "SUBSCRIPTIONS"
                            },
                            s_rectTransformZeroOne
                        }
                    }
                };
            }

            static Vector2 GetGridOffset(int gridPosition)
            {
                const int rowLength = 6;
                const int colLength = 4;

                const int width  = 1650;
                const int height = 700;

                int rowNumber = gridPosition / rowLength;
                int colNumber = gridPosition % rowLength;

                float wStep = width / (float) rowLength;
                float hStep = height / (float) colLength;

                float wOffset = colNumber * wStep;
                float hOffset = rowNumber * hStep;

                return new Vector2( wOffset, hOffset );
            }

            static CuiRectTransformComponent Vector2ToRectTransform(
                Vector2 vector,
                float width,
                float height
            )
            {
                float halfWidth  = width / 2f;
                float halfHeight = height / 2f;

                return new CuiRectTransformComponent {
                    AnchorMin = $"{vector.x} {vector.y}",
                    AnchorMax = $"{vector.x} {vector.y}",
                    OffsetMin = $"-{halfWidth} -{halfHeight}",
                    OffsetMax = $"{halfWidth} {halfHeight}"
                };
            }

            static IEnumerable<CuiElement> RenderItemCard(ItemData itemData, int gridPosition)
            {
                var transform = Vector2ToRectTransform( GetGridOffset( gridPosition ), 160, 200 );

                var baseName = $"gmonetize.itemlist.item({gridPosition})";

                return new[] {
                    new CuiElement {
                        Name = baseName + ".container",
                        Parent = "gmonetize.itemlist.container",
                        Components = {
                            new CuiImageComponent {
                                Color = "0 0 0 0"
                            },
                            transform
                        }
                    },
                    new CuiElement {
                        Name = baseName + ".button_claim.container",
                        Parent = baseName + ".container",
                        Components = {
                            new CuiImageComponent {
                                Color = "0.4 0.8 0.2 0.8",

                            }
                        }
                    },
                    new CuiElement {
                        Name = baseName + ".desc.container",
                        Parent = baseName + ".container",
                        Components = {
                            new CuiImageComponent {
                                Color = ""
                            },
                            new CuiRectTransformComponent {

                            }
                        }
                    },
                    new CuiElement {
                        Name = baseName + ".desc.image",
                        Parent = baseName + ".desc.container",
                        Components = {
                            new CuiImageComponent {},
                            new CuiRectTransformComponent {
                                AnchorMin = "0 0",
                                AnchorMax = "1 0.45"
                            }
                        }
                    },
                    new CuiElement {
                        Name = baseName + ".desc.label.quantity",
                        Parent = baseName + ".desc.container",
                        Components = {
                            new CuiTextComponent {
                                Text = "x" + itemData.Amount,
                                Color = "1 1 1 0.5",
                                Align = TextAnchor.UpperLeft
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1"
                            }
                        }
                    },
                    new CuiElement {
                        Name = baseName + ".desc.label.description",
                        Parent = baseName + ".desc.container",
                        Components = {
                            new CuiTextComponent {
                                Text = "", // find desc text
                            },
                            new CuiRectTransformComponent {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1"
                            }
                        }
                    }
                };
            }

            enum Tab { Items, Subscriptions }
        }
    }
}
