// Requires: gMonetize

#define DEBUG

using System.Collections.Generic;
using System.Diagnostics;
using Facepunch;
using Network;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Game.Rust.Cui;
using UnityEngine;


namespace Oxide.Plugins
{
    [Info("gMonetize UI", "2CHEVSKII", "1.0.0")]
    public class gMonetizeUI : CovalencePlugin
    {
        [Conditional("DEBUG")]
        private static void LogDebug(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[gMonetize UI] " + format, args);
        }


        #region Oxide hooks

        private void OnServerInitialized()
        {
            foreach (var player in players.Connected)
            {
                OnUserConnected(player);
            }

            RunDebugRender();
        }

        private void Unload()
        {
            foreach (var player in players.Connected)
            {
                OnUserDisconnected(player);
            }
        }

        private void OnUserConnected(IPlayer player)
        {
            var basePlayer = player.Object as BasePlayer;

            if (!basePlayer)
            {
                LogWarning("Could not cast player {0} to a BasePlayer, skipping UI initialization",
                    player);
                return;
            }

            basePlayer.gameObject.AddComponent<Controller>();

            LogDebug("Initialized UI on player {0}", player);
        }

        private void OnUserDisconnected(IPlayer player)
        {
            var basePlayer = player.Object as BasePlayer;

            if (!basePlayer)
            {
                LogWarning("Could not cast player {0} to a BasePlayer, skipping de-initialization of the UI", player);
                return;
            }

            var uiController = basePlayer.GetComponent<Controller>();

            if (!uiController)
            {
                LogWarning("Could not find the gMonetizeUIController component on player {0}, possibly a bug?", player);
                return;
            }

            UnityEngine.Object.Destroy(uiController);

            LogDebug("De-initialized UI on player {0}", player);
        }

        #endregion

        [Conditional("DEBUG")]
        private void RunDebugRender()
        {
            LogDebug("RunDebugRender");
            var player = players.FindPlayer("чечняхуйня");

            LogDebug("Debug player found: {0}:{1}", player.Name, player.Id);

            ((BasePlayer)player.Object).SendMessage("gMonetize_DebugUI");
        }

        private class Controller : FacepunchBehaviour
        {
            private const string RPC_ADDUI = "AddUI",
                RPC_DESTROYUI = "DestroyUI";

            private static readonly JsonSerializerSettings s_JsonSerializerSettings = new JsonSerializerSettings
            {
                Formatting = Formatting.None,
                DefaultValueHandling = DefaultValueHandling.Ignore
            };

            private BasePlayer _player;
            private SendInfo _sendInfo;

            #region Unity event handlers

            private void Awake()
            {
                _player = GetComponent<BasePlayer>();
                LogDebug("Initialized on player {0}", _player.displayName);

                _sendInfo = new SendInfo(_player.Connection)
                {
                    priority = Priority.Normal,
                    method = SendMethod.Reliable,
                    channel = 0
                };
            }

            private void OnDestroy()
            {
                SendDestroyUI("gmonetize/maincontainer");
            }

            #endregion

            #region UI RPC helpers

            private string SerializeUI(IEnumerable<CuiElement> elements)
            {
                List<CuiElement> list = Pool.GetList<CuiElement>();
                list.AddRange(elements);

                string json = JsonConvert.SerializeObject(list, s_JsonSerializerSettings).Replace("\\n", "\n");

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
                    _sendInfo,
                    null,
                    RPC_ADDUI,
                    uiJson
                );
            }

            private void SendDestroyUI(string elementName)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
                    null,
                    RPC_DESTROYUI,
                    elementName
                );
            }

            private void SendDestroyUI(string en0, string en1)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
                    null,
                    RPC_DESTROYUI,
                    en0
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
                    null,
                    RPC_DESTROYUI,
                    en1
                );
            }

            private void SendDestroyUI(string en0, string en1, string en2)
            {
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
                    null,
                    RPC_DESTROYUI,
                    en0
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
                    null,
                    RPC_DESTROYUI,
                    en1
                );
                CommunityEntity.ServerInstance.ClientRPCEx(
                    _sendInfo,
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

            void gMonetize_DebugUI()
            {
                SendAddUI(Builder.MainContainer());
            }
        }

        private class Builder
        {
            public static IEnumerable<CuiElement> MainContainer()
            {
                return new[]
                {
                    new CuiElement
                    {
                        Parent = "Hud.Menu",
                        Name = "gmonetize/maincontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Material = "assets/content/ui/uibackgroundblur.mat",
                                Color = "0.05 0.05 0.05 0.9"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/maincontainer",
                        Name = "gmonetize/innercontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0 0 0 0"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.1 0.2",
                                AnchorMax = "0.9 0.95"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/innercontainer",
                        Name = "gmonetize/headercontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0 0 0 0"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0.94",
                                AnchorMax = "1 1",
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/headercontainer",
                        Name = "gmonetize/titlecontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0.5 0.5 0.5 0.5",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.08 0",
                                AnchorMax = "0.96 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/headercontainer",
                        Name = "gmonetize/prevbtn",
                        Components =
                        {
                            new CuiButtonComponent
                            {
                                Color = "0.5 0.5 0.5 0.5",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "0.035 1",
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/prevbtn",
                        Name = "gmonetize/prevbtn/icon",
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Url = "https://i.imgur.com/TiYyODy.png",
                                Color = "1 1 1 0.3"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.2 0.2",
                                AnchorMax = "0.8 0.8"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/headercontainer",
                        Name = "gmonetize/nextbtn",
                        Components =
                        {
                            new CuiButtonComponent
                            {
                                Color = "0.6 0.6 0.6 0.2",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.04 0",
                                AnchorMax = "0.075 1",
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/nextbtn",
                        Name = "gmonetize/nextbtn/icon",
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Url = "https://i.imgur.com/tBYlfGM.png",
                                Color = "1 1 1 0.1"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.2 0.2",
                                AnchorMax = "0.8 0.8"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/headercontainer",
                        Name = "gmonetize/closebtn",
                        Components =
                        {
                            new CuiButtonComponent
                            {
                                Color = "0.5 0.2 0.2 0.6",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.965 0",
                                AnchorMax = "1 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/closebtn",
                        Name = "gmonetize/closebtn/icon",
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Url = "https://i.imgur.com/9RkJQY2.png",
                                Color = "1 1 1 0.3"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.2 0.2",
                                AnchorMax = "0.8 0.8"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/innercontainer",
                        Name = "gmonetize/itemlistcontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0 0 0 0"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "1 0.93"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemlistcontainer",
                        Name = "gmonetize/itemcard",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0.5 0.5 0.5 0.5",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0.7",
                                AnchorMax = "0.14 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard",
                        Name = "gmonetize/itemcard/footercontainer",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                Color = "0 0 0 0"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.05 0.03",
                                AnchorMax = "0.95 0.2"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard/footercontainer",
                        Name = "gmonetize/itemcard/itemname",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Color = "1 1 1 0.8",
                                Text = "Тупа пулем...",
                                FontSize = 12,
                                Align = TextAnchor.MiddleLeft,
                                Font = "robotocondensed-regular.ttf"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0.5",
                                AnchorMax = "0.8 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard/footercontainer",
                        Name = "gmonetize/itemcard/itemamount",
                        Components =
                        {
                            new CuiTextComponent
                            {
                                Color = "1 1 1 0.2",
                                Text = "x1098723",
                                FontSize = 10,
                                Align = TextAnchor.LowerLeft
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0 0",
                                AnchorMax = "0.8 0.5"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard/footercontainer",
                        Name = "gmonetize/itemcard/claimbtn",
                        Components =
                        {
                            new CuiButtonComponent
                            {
                                Color = "0.7 0.8 0.35 0.3",
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.8 0",
                                AnchorMax = "1 1"
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard/claimbtn",
                        Name = "gmonetize/itemcard/claimbtn/icon",
                        Components =
                        {
                            new CuiRawImageComponent
                            {
                                Url = "https://i.imgur.com/Gq3GKTE.png",
                                Color = "0.75 0.9 0.3 0.3"
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.1 0.1",
                                AnchorMax = "0.9 0.9",
                            }
                        }
                    },
                    new CuiElement
                    {
                        Parent = "gmonetize/itemcard",
                        Name = "gmonetize/itemcard/icon",
                        Components =
                        {
                            new CuiImageComponent
                            {
                                ItemId = -2069578888,
                            },
                            new CuiRectTransformComponent
                            {
                                AnchorMin = "0.05 0.25",
                                AnchorMax = "0.95 0.96"
                            }
                        }
                    }
                };
            }
        }

        private static class Names
        {
            public const string MAIN_CONTAINER = "gmonetize/maincontainer";
            public const string INNER_CONTAINER = "gmonetize/innercontainer";
            public const string HEADER_CONTAINER = "gmonetize/headercontainer";
            public const string TITLE_CONTAINER = "gmonetize/titlecontainer";
            public const string CLOSE_BTN = "gmonetize/closebtn";
            public const string CLOSE_BTN_ICON = "gmonetize/closebtn/icon";
            public const string PREV_BTN = "gmonetize/prevbtn";
            public const string PREV_BTN_ICON = "gmonetize/prevbtn/icon";
            public const string NEXT_BTN = "gmonetize/nextbtn";
            public const string NEXT_BTN_ICON = "gmonetize/nextbtn/icon";
            public const string ITEMLIST_CONTAINER = "gmonetize/itemlistcontainer";

            public class ItemCardNames
            {
                private readonly string _id;

                public ItemCardNames(string id)
                {
                    _id = id;
                }

                public static implicit operator string(ItemCardNames icn)
                {
                    return icn.Self();
                }

                public string Self()
                {
                    return $"gmonetize/itemcard[{_id}]";
                }

                public string InnerContainer()
                {
                    return Self() + "/innercontainer";
                }

                public string Icon()
                {
                    return Self() + "/icon";
                }

                public string FooterContainer()
                {
                    return Self() + "/footercontainer";
                }

                public string ItemName()
                {
                    return Self() + "/itemname";
                }

                public string ItemAmount()
                {
                    return Self() + "/itemamount";
                }

                public class ItemCardButtonNames
                {
                    private readonly ItemCardNames _itemCardNames;

                    public ItemCardButtonNames(ItemCardNames icn)
                    {
                        _itemCardNames = icn;
                    }

                    public static implicit operator string(ItemCardButtonNames icbn)
                    {
                        return icbn.Self();
                    }

                    public string Self()
                    {
                        return _itemCardNames + "/claimbtn";
                    }

                    public string Text()
                    {
                        return Self() + "/text";
                    }
                }
            }
        }
    }
}
