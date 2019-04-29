using Oxide.Core;
using System.Collections.Generic;
using System.Linq;
using System;
using UnityEngine;
using Oxide.Core.Configuration;
using Oxide.Game.Rust.Cui;

namespace Oxide.Plugins
{
    [Info("Player Gifts", "Server-rust.ru - Лучщие бесплатные плагины", "0.1.1")]
    class PlayerGifts : RustPlugin
    {
        Dictionary<BasePlayer, int> timers = new Dictionary<BasePlayer, int>();
        List<ulong> activePlayers = new List<ulong>();

        #region CONFIGURATION
        public int GameActive = 15;
        string EnableGUIPlayerMin = "0.01041665 0.07825926";
        string EnableGUIPlayerMax = "0.1805 0.122";
        string GUIEnabledColor = "0.44 0.55 0.26 0.70";
        string GUIColor = "0.44 0.55 0.26 1";
        string ImagesGUI = "https://i.imgur.com/w5FhWrR.png";

        private void LoadDefaultConfig()
        {
            GetConfig("Настройки", "Время активности на сервере за какое выдается подарок (в минутах)", ref GameActive);
            GetConfig("GUI", "Цвет фона кнопки 'Забрать подарок'", ref GUIEnabledColor);
            GetConfig("GUI", "Цвет фона заполнения", ref GUIColor);
            GetConfig("GUI", "Ссылка на изображение", ref ImagesGUI);
            GetConfig("GUI", "Anchor Min Основной панели", ref EnableGUIPlayerMin);
            GetConfig("GUI", "Anchor Max Основной панели", ref EnableGUIPlayerMax);
            SaveConfig();
        }

        private void GetConfig<T>(string menu, string Key, ref T var)
        {
            if (Config[menu, Key] != null)
            {
                var = (T)Convert.ChangeType(Config[menu, Key], typeof(T));
            }
            Config[menu, Key] = var;
        }

        #endregion

        #region Core

        bool CanTake(BasePlayer player) => !player.inventory.containerMain.IsFull() || !player.inventory.containerBelt.IsFull();

        bool TakeGifts(BasePlayer player, string gift = "Player Gifts")
        {
            if (data.GiftPlayers[player.userID].ActiveGifts != 0)
            {
                if (!CanTake(player))
                {
                    SendReply(player, Messages["InvFull"]);
                    return false;
                }

                var item = gifts[gift].Items.GetRandom();
                var amount = item.GetRandom();
                player.inventory.GiveItem(ItemManager.CreateByName(item.Shortname, amount, 0));
                var x = ItemManager.CreateByPartialName(item.Shortname);
                data.GiftPlayers[player.userID].ActiveGifts = data.GiftPlayers[player.userID].ActiveGifts = 0;
                SaveData();
                CuiHelper.DestroyUi(player, "GetGift");
                UpdateTimer(player);
                if (amount > 1)
                    SendReply(player, Messages["GiveGift"], x.info.displayName.english, amount);
                else
                {
                    SendReply(player, Messages["GiveGiftAmount"], x.info.displayName.english);
                }
                SaveData();
                return true;
            }
            return false;
        }

        public BasePlayer FindBasePlayer(string nameOrUserId)
        {
            nameOrUserId = nameOrUserId.ToLower();
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player.displayName.ToLower().Contains(nameOrUserId) || player.UserIDString == nameOrUserId)
                    return player;
            }
            foreach (var player in BasePlayer.sleepingPlayerList)
            {
                if (player.displayName.ToLower().Contains(nameOrUserId) || player.UserIDString == nameOrUserId)
                    return player;
            }
            return default(BasePlayer);
        }

        void TimerHandler()
        {
            foreach (var player in timers.Keys.ToList())
            {
                var seconds = --timers[player];
                var resetTime = (GameActive * 60);
                if (seconds > resetTime)
                {
                    data.GiftPlayers[player.userID].Time = data.GiftPlayers[player.userID].Time = resetTime;
                    TimerHandler();
                    break;
                }

                if (seconds <= 0)
                {
                    var TimerGift = FormatTime(TimeSpan.FromSeconds(resetTime));
                    timers.Remove(player);
                    data.GiftPlayers[player.userID].ActiveGifts = data.GiftPlayers[player.userID].ActiveGifts + 1;
                    data.GiftPlayers[player.userID].Time = data.GiftPlayers[player.userID].Time = resetTime;
                    SaveData();
                    SendReply(player, Messages["TheTimeEnd"], TimerGift);
                    DrawUIGetGift(player);
                    continue;
                }
                if (data.GiftPlayers[player.userID].ActiveGifts == 0)
                {
                    DrawUIBalance(player, seconds);
                    data.GiftPlayers[player.userID].Time = data.GiftPlayers[player.userID].Time = seconds;
                }
            }
        }

        void UpdateTimer(BasePlayer player)
        {
            if (player == null) return;

            var resetTime = (GameActive * 60);
            timers[player] = data.GiftPlayers[player.userID].Time;
            DrawUIBalance(player, timers[player]);
        }

        void DeactivateTimer(BasePlayer player)
        {
            if (activePlayers.Contains(player.userID))
            {
                activePlayers.Remove(player.userID);
                timers.Remove(player);
            }
        }

        void ActivateTimer(ulong userId)
        {
            if (!activePlayers.Contains(userId))
            {
                activePlayers.Add(userId);
            }
        }

        public static string FormatTime(TimeSpan time)
        {
            string result = string.Empty;
            if (time.Days != 0)
                result += $"{Format(time.Days, "дней", "дня", "день")} ";

            if (time.Hours != 0)
                result += $"{Format(time.Hours, "часов", "часа", "час")} ";

            if (time.Minutes != 0)
                result += $"{Format(time.Minutes, "минут", "минуты", "минуту")} ";

            if (time.Seconds != 0)
                result += $"{Format(time.Seconds, "секунд", "секунды", "секунда")} ";

            return result;
        }

        private static string Format(int units, string form1, string form2, string form3)
        {
            var tmp = units % 10;

            if (units >= 5 && units <= 20 || tmp >= 5 && tmp <= 9)
                return $"{units} {form1}";

            if (tmp >= 2 && tmp <= 4)
                return $"{units} {form2}";

            return $"{units} {form3}";
        }
        #endregion

        #region COMMANDS

        [ChatCommand("gift")]
        void cmdGiveGift(BasePlayer player)
        {
            if (player == null) return;
            if (data.GiftPlayers[player.userID].ActiveGifts == 0)
            {
                SendReply(player, Messages["PlayerNHaveGift"]);
                return;
            }
            if (data.GiftPlayers[player.userID].ActiveGifts >= 1)
            {
                TakeGifts(player);
            }
        }

        [ConsoleCommand("getGift")]
        void CmdGetGift(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;

            if (data.GiftPlayers[player.userID].ActiveGifts == 0)
            {
                SendReply(player, Messages["PlayerNHaveGift"]);
                return;
            }
            if (data.GiftPlayers[player.userID].ActiveGifts >= 1)
            {
                TakeGifts(player);
            }
        }

        #endregion

        #region UI

        int getExperiencePercentInt(int skill)
        {
            var resetTime = (GameActive * 60);
            var next = resetTime;
            var Points = resetTime - skill;
            var reply = 683;
            var experienceProc = Convert.ToInt32((Points / (double)next) * 100);
            if (experienceProc >= 100)
                experienceProc = 99;
            else if (experienceProc == 0)
                experienceProc = 1;
            return experienceProc;
        }

        void DrawUIBalance(BasePlayer player, int seconds)
        {
            CuiHelper.DestroyUi(player, "OpenGift1");
            CuiHelper.DestroyUi(player, "ProcentBar");
            int percent = getExperiencePercentInt(seconds);
            var resetTime = (GameActive * 60);
            var TimerGift = (resetTime / 60);
            CuiElementContainer Container = new CuiElementContainer();
            CuiElement OpenGift = new CuiElement
            {
                Name = "OpenGift",
                Parent = "UIPlayer",
                Components =
                {
                    new CuiTextComponent {
                        Text = $"",
                        Align = TextAnchor.MiddleCenter,
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.15 0",
                        AnchorMax = "1 1"
                    }
                }
            };
            CuiElement ProcentBar = new CuiElement
            {
                Name = "ProcentBar",
                Parent = "OpenGift",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Command = "getGift",
                        Color = GUIColor,
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0.1",
                        AnchorMax = $"{1 - ((seconds + (float)TimerGift) / (float)resetTime)} 0.85"
                    }
                }
            };
            CuiElement OpenGift1 = new CuiElement
            {
                Name = "OpenGift1",
                Parent = "UIPlayer",
                Components =
                {
                    new CuiTextComponent {
                        Text = $"{percent}%",
                        Align = TextAnchor.MiddleCenter,
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.15 0",
                        AnchorMax = "1 1"
                    },
                        new CuiOutlineComponent {
                            Color = "0 0 0 0.5",
                            Distance = "1.0 -0.5"
                        }
                }
            };

            Container.Add(OpenGift);
            Container.Add(ProcentBar);
            Container.Add(OpenGift1);
            CuiHelper.AddUi(player, Container);

        }

        void DrawUIGetGift(BasePlayer player)
        {
            DrawUIPlayer(player);
            CuiHelper.DestroyUi(player, "OpenGift1");
            CuiElementContainer Container = new CuiElementContainer();

            CuiElement GetGift = new CuiElement
            {
                Name = "GetGift",
                Parent = "UIPlayer",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Command = "getGift",
                        Color = "0.44 0.55 0.26 1",
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0.15 0.11",
                        AnchorMax = "0.985 0.88"
                    }
                }
            };

            CuiElement TextGetGift = new CuiElement
            {
                Name = "TextGetGift",
                Parent = "GetGift",
                Components =
                {
                    new CuiTextComponent {
                        Text = Messages["GiveGifts"],
                        Align = TextAnchor.MiddleCenter
                    },
                    new CuiRectTransformComponent {
                        AnchorMin = "0 0",
                        AnchorMax = "1 1"
                    },
                        new CuiOutlineComponent {
                            Color = "0 0 0 0.5", Distance = "1.0 -0.5"

                        }
                }
            };
            Container.Add(GetGift);
            Container.Add(TextGetGift);
            CuiHelper.AddUi(player, Container);

        }

        void DrawUIPlayer(BasePlayer player)
        {
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.In(0.1f, () => DrawUIPlayer(player));
                return;
            }
            CuiElementContainer Container = new CuiElementContainer();
            CuiElement GiftIcon = new CuiElement
            {
                Name = "GiftIcon",
                Parent = "BPUI",
                Components = {
                        new CuiRawImageComponent {
                            Url = ImagesGUI,
                            Color = "1 1 1 0.7"
                        },
                        new CuiRectTransformComponent {
                        AnchorMin = "0.1 0.1",
                        AnchorMax = "0.9 0.9"
                        }
                }
            };
            CuiElement BPUI = new CuiElement
            {
                Name = "BPUI",
                Parent = "UIPlayer",
                Components = {
                        new CuiImageComponent {
                            Color = "0 0 0 0.1"
                        },
                        new CuiRectTransformComponent {
                             AnchorMin = "0 0",
                        AnchorMax = "0.14 0.98"
                        }
                    }
            };
            CuiElement UIPlayer = new CuiElement
            {
                Name = "UIPlayer",
                Parent = "Hud",
                Components =
                {
                    new CuiButtonComponent
                    {
                        Command = "getGift",
                        Color = "0.32 0.32 0.41 0.22",
                    },
                    new CuiRectTransformComponent {
                         AnchorMin = EnableGUIPlayerMin,
                        AnchorMax = EnableGUIPlayerMax
                    }
                }
            };

            Container.Add(UIPlayer);
            Container.Add(BPUI);
            Container.Add(GiftIcon);
            CuiHelper.AddUi(player, Container);
            var resetTime = (GameActive * 60);
            DrawUIBalance(player, resetTime);
        }

        void DestroyUIPlayer(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, "UIPlayer");
        }

        #endregion

        #region OXIDE HOOKS

        void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                DestroyUIPlayer(player);
                DeactivateTimer(player);
            }
            SaveData();
        }

        void OnPlayerDisconnected(BasePlayer player)
        {
            DeactivateTimer(player);
        }

        void OnServerInitialized()
        {
            LoadDefaultConfig();
            GiftData = Interface.Oxide.DataFileSystem.GetFile("PlayerGifts/Players");
            LoadData();
            lang.RegisterMessages(Messages, this, "en");
            Messages = lang.GetMessages("en", this);
            timer.Every(1f, TimerHandler);
            foreach (var player in BasePlayer.activePlayerList)
            {
                OnPlayerInit(player);
            }
        }

        void LoadData()
        {
            try
            {
                data = Interface.GetMod().DataFileSystem.ReadObject<DataStorage>("PlayerGifts/Players");
            }

            catch
            {
                data = new DataStorage();
            }

            if (Interface.Oxide.DataFileSystem.ExistsDatafile("PlayerGifts/Gifts"))
                gifts = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<string, GiftDefinition>>("PlayerGifts/Gifts");
            else
            {
                gifts.Add("Player Gifts", new GiftDefinition()
                {
                    Type = "Gifts",
                    Items = new List<CaseItem>
                    {
                        new CaseItem
                        {
                            Shortname = "rifle.ak",
                            Min = 1,
                            Max = 1
                        }
                    }
                });
                Interface.Oxide.DataFileSystem.WriteObject("PlayerGifts/Gifts", gifts);
            }
        }

        void SaveData()
        {
            GiftData.WriteObject(data);
        }

        void OnServerSave()
        {
            SaveData();
        }

        void OnPlayerInit(BasePlayer player)
        {
            if (player.HasPlayerFlag(BasePlayer.PlayerFlags.ReceivingSnapshot))
            {
                timer.In(1f, () => OnPlayerInit(player));
                return;
            }
            if (player == null) return;

            var Time = (GameActive * 60);
            if (!data.GiftPlayers.ContainsKey(player.userID))
            {
                data.GiftPlayers.Add(player.userID, new GiftsData()
                {
                    Name = player.displayName,
                    ActiveGifts = 0,
                    Time = Time,
                });
                SaveData();
            }
            if (data.GiftPlayers[player.userID].ActiveGifts == 0)
            {
                UpdateTimer(player);
                ActivateTimer(player.userID);
                DrawUIPlayer(player);
            }
            else
            {
                DrawUIGetGift(player);
            }

        }
        #endregion

        #region DATA

        class DataStorage
        {
            public Dictionary<ulong, GiftsData> GiftPlayers = new Dictionary<ulong, GiftsData>();
            public DataStorage() { }
        }

        class GiftsData
        {
            public string Name;
            public int ActiveGifts;
            public int Time;
        }

        DataStorage data;

        private DynamicConfigFile GiftData;

        static PlayerGifts instance;

        public class GiftDefinition
        {
            public string Type;
            public List<CaseItem> Items;
            public CaseItem Open() => Items.GetRandom();
        }

        public class CaseItem
        {
            public string Shortname;
            public int Min;
            public int Max;
            public int GetRandom() => UnityEngine.Random.Range(Min, Max + 1);
        }

        public Dictionary<string, GiftDefinition> gifts = new Dictionary<string, GiftDefinition>();

        #endregion

        #region LOCALIZATION

        Dictionary<string, string> Messages = new Dictionary<string, string>()
        {
            {"InvFull", "У Вас переполнен инвентарь" },
            {"GiveGift", "Вы получили {0} в размере: {1} шт." },
            {"GiveGiftAmount", "Вы получили {0}" },
            {"TheTimeEnd", "Вы пробыли на сервере: <color=#A6FFAC>{0}</color>, у нас для Вас подарок!\nОткройте чат, и нажмите кнопку <color=#A6FFAC>Забрать подарок</color>, либо используйте <color=#A6FFAC>/gift</color> что бы получить его" },
            {"PlayerNHaveGift", "Для Вас пока нету подарков" },
            {"GiveGifts", "ЗАБРАТЬ ПОДАРОК" },
        };

        #endregion
    }
}
                     