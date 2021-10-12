#define DEBUG

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;
using UnityEngine.Assertions;

namespace Oxide.Plugins
{
    [Info("Replenish", "2CHEVSKII", "2.0.0")]
    [Description("Save and restore items in selected containers")]
    class Replenish : CovalencePlugin
    {
        PluginSettings      settings;
        ReplenishController controller;

        const string PERMISSION_USE = "replenish.use";

        const string DATAFILE_NAME = "replenish.data";

        const string M_PREFIX              = "Chat prefix",
                     M_NO_PERMISSION       = "No permission",
                     M_HELP                = "Help message",
                     M_CONTAINER_SET       = "Container set for replenish",
                     M_CONTAINER_UNSET     = "Container removed from replenish list",
                     M_CONTAINER_INFO      = "Container info",
                     M_CONTAINER_NOT_EMPTY = "Container is not empty",
                     M_TIMED               = "Timed restore",
                     M_WIPE_ONLY           = "Wipe restore",
                     M_COMMAND_ONLY        = "Command restore",
                     M_CONTAINER_NOT_FOUND = "Container not found",
                     M_CONTAINER_NOT_SAVED = "Container not saved",
                     M_NO_CONTAINERS_SAVED = "No saved containers",
                     M_CONTAINER_RESTORED  = "Container restored",
                     M_ALL_RESTORED        = "All containers restored",
                     M_ALL_REMOVED         = "All containers removed from restore";

        static Replenish Instance;

        bool isNewWipe;

        #region Command Handler

        void CommandHandler(IPlayer player, string _, string[] args) // TODO: This method requires some serious refactoring
        {
            if (player.IsServer || !player.HasPermission(PERMISSION_USE))
            {
                Message(player, M_NO_PERMISSION);
                return;
            }

            if (args.Length != 0)
            {
                var container = GetContainerInSight(player);
                switch (args[0].ToLower())
                {
                    case "save":
                        if (!container)
                        {
                            Message(player, M_CONTAINER_NOT_FOUND);
                        }
                        else
                        {
                            if (args.Length == 1)
                            {
                                var cData = controller.SaveContainer(container, settings.DefaultReplenishTimer);

                                string msg = GetSaveTimeMessage(cData.Mode);

                                Message(player, M_CONTAINER_SET, string.Format(msg, cData.RestoreTime));
                            }
                            else
                            {
                                switch (args[1].ToLower())
                                {
                                    case "cmd":
                                        controller.SaveContainer(container, 0f);

                                        Message(player, M_CONTAINER_SET, GetMessage(player, M_COMMAND_ONLY));
                                        break;

                                    case "wipe":
                                        controller.SaveContainer(container, -1f);

                                        Message(player, M_CONTAINER_SET, GetMessage(player, M_WIPE_ONLY));
                                        break;

                                    default:
                                        float seconds;
                                        if (float.TryParse(
                                            args[1],
                                            NumberStyles.Number,
                                            CultureInfo.InvariantCulture,
                                            out seconds
                                        ))
                                        {
                                            var cData = controller.SaveContainer(container, seconds);

                                            string msg = GetSaveTimeMessage(cData.Mode);

                                            Message(
                                                player,
                                                M_CONTAINER_SET,
                                                string.Format(GetMessage(player, msg), cData.RestoreTime)
                                            );
                                        }
                                        else
                                        {
                                            Message(player, M_HELP);
                                        }

                                        break;
                                }
                            }
                        }

                        break;

                    case "remove":
                        if (args.Length == 1)
                        {
                            if (!container)
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                                return;
                            }

                            if (!controller.RemoveContainer(container))
                            {
                                Message(player, M_CONTAINER_NOT_SAVED);
                                return;
                            }

                            Message(player, M_CONTAINER_UNSET);
                        }
                        else
                        {
                            int id;

                            if (int.TryParse(args[1], out id))
                            {
                                if (!controller.RemoveContainer(id))
                                {
                                    Message(player, M_CONTAINER_NOT_SAVED);
                                }
                                else
                                {
                                    Message(player, M_CONTAINER_UNSET);
                                }
                            }
                        }

                        break;

                    case "info":
                        int cId;
                        if (args.Length == 1)
                        {
                            if (container)
                            {
                                var cData = controller.FindDataByEntity(container);

                                if (cData == null)
                                {
                                    Message(player, M_CONTAINER_NOT_SAVED);
                                }
                                else
                                {
                                    MessageRaw(player, BuildContainerInfo(player, cData));
                                }
                            }
                            else
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                            }
                        }
                        else if (int.TryParse(args[1], out cId))
                        {
                            var cData = controller.FindDataById(cId);

                            if (cData == null)
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                            }
                            else
                            {
                                MessageRaw(player, BuildContainerInfo(player, cData));
                            }
                        }
                        else
                        {
                            Message(player, M_HELP);
                        }

                        break;
                    case "list":
                        var data = controller.GetSaveData();

                        if (data.Count == 0)
                        {
                            Message(player, M_NO_CONTAINERS_SAVED);
                        }
                        else
                        {
                            StringBuilder builder = new StringBuilder();

                            builder.AppendLine();

                            for (int i = 0; i < data.Count; i++)
                            {
                                var cData = data[i];

                                var info = BuildContainerInfo(player, cData);

                                builder.AppendLine(info);
                            }

                            MessageRaw(player, builder.ToString());
                        }

                        break;
                    case "restore":
                        int containerId;
                        if (args.Length == 1)
                        {
                            if (!container)
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                            }
                            else
                            {
                                var containerData = controller.FindDataByEntity(container);

                                if (containerData == null)
                                {
                                    Message(player, M_CONTAINER_NOT_SAVED);
                                }
                                else
                                {
                                    if (!controller.RestoreNow(containerData))
                                    {
                                        Message(player,M_CONTAINER_NOT_EMPTY);
                                    }
                                    else
                                    {
                                        Message(player, M_CONTAINER_RESTORED);
                                    }
                                }
                            }
                        }
                        else if (int.TryParse(args[1], out containerId))
                        {
                            var containerData = controller.FindDataById(containerId);

                            if (containerData == null)
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                            }
                            else
                            {
                                if (!controller.RestoreNow(containerData))
                                {
                                    Message(player, M_CONTAINER_NOT_EMPTY);
                                }
                                else
                                {
                                    Message(player, M_CONTAINER_RESTORED);
                                }
                            }
                        }
                        else
                        {
                            Message(player, M_HELP);
                        }

                        break;

                    case "restoreall":
                        var saved = controller.GetSaveData();

                        if (saved.Count == 0)
                        {
                            Message(player, M_NO_CONTAINERS_SAVED);
                        }
                        else
                        {
                            for (int i = saved.Count - 1; i >= 0; i--)
                            {
                                controller.RestoreNow(saved[i]);
                            }

                            Message(player, M_ALL_RESTORED);
                        }

                        break;

                    default:
                        Message(player, M_HELP);
                        break;
                    case "clear":
                        var savedData = controller.GetSaveData();

                        if (savedData.Count == 0)
                        {
                            Message(player, M_NO_CONTAINERS_SAVED);
                        }
                        else
                        {
                            for (int i = savedData.Count - 1; i >= 0; i--)
                            {
                                controller.RemoveContainer(controller.GetDataIndex(savedData[i]));
                            }

                            Message(player, M_ALL_REMOVED);
                        }
                        break;
                }
            }
        }

        #endregion

        #region Utility methods

        StorageContainer GetContainerInSight(IPlayer player)
        {
            var basePlayer = (BasePlayer)player.Object;

            RaycastHit hit;

            bool bHit = Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 5f, LayerMask.GetMask("Deployed"));

            if (bHit)
            {
                var ent = hit.GetEntity();

                return ent as StorageContainer;
            }

            return null;
        }

        #endregion

        #region Oxide hooks

        void Init()
        {
            Instance = this;

            permission.RegisterPermission(PERMISSION_USE, this);

            AddCovalenceCommand("replenish", "CommandHandler");
        }

        void OnServerInitialized()
        {
            controller = ServerMgr.Instance.gameObject.AddComponent<ReplenishController>();

            controller.Init(LoadData());
        }

        void Unload()
        {
            SaveData(controller.GetSaveData());

            UnityEngine.Object.Destroy(controller);

            Instance = null;
        }

        void OnNewSave()
        {
            isNewWipe = true;
        }

        #endregion

        #region Lang API

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string> {
                    [M_PREFIX] = "[Replenish] ",
                    [M_NO_PERMISSION] = "You have no access to this command",
                    [M_HELP] = "Usage: /replenish [command] <args>\n" +
                               "Commands:\n" +
                               "save (<digit|'wipe'|'cmd'>) - Save container you are currently looking at or change settings for container\n" +
                               "remove (<digit>) - Remove container from replenish\n" +
                               "info (<digit>) - Get information about container\n" +
                               "list - Get list of saved containers\n" +
                               "restore (<digit>) - Restore specified container\n" +
                               "restoreall - Restore all containers\n" +
                               "clear - Remove all containers from replenish",
                    [M_CONTAINER_SET] = "Container set to '{0}'",
                    [M_CONTAINER_UNSET] = "Container removed from replenish",
                    [M_CONTAINER_INFO] = "#{0}: {1}, {2} | {3}",
                    [M_CONTAINER_NOT_EMPTY] = "Container must be empty",
                    [M_TIMED] = "Restore every {0:0} seconds",
                    [M_WIPE_ONLY] = "Restore every new wipe",
                    [M_COMMAND_ONLY] = "Restore on command",
                    [M_CONTAINER_NOT_FOUND] = "Container not found",
                    [M_CONTAINER_NOT_SAVED] = "This container is not saved",
                    [M_NO_CONTAINERS_SAVED] = "Server has no saved containers",
                    [M_CONTAINER_RESTORED] = "Container was restored",
                    [M_ALL_RESTORED] = "Restored all containers",
                    [M_ALL_REMOVED] = "All containers were removed from replenish"
                },
                this
            );
        }

        string BuildContainerInfo(IPlayer player, ContainerData data)
        {
            string fmt = GetMessage(player, M_CONTAINER_INFO);
            string msg = string.Format(
                fmt,
                controller.GetDataIndex(data),
                $"[{data.Position.x:0}, {data.Position.y:0}, {data.Position.z:0}]",
                data.NetId,
                string.Format(GetMessage(player, GetSaveTimeMessage(data.Mode)), data.RestoreTime)
            );

            return msg;
        }

        string GetSaveTimeMessage(ContainerData.RestoreMode mode)
        {
            switch (mode)
            {
                case ContainerData.RestoreMode.Timed:
                    return M_TIMED;
                case ContainerData.RestoreMode.Wipe:
                    return M_WIPE_ONLY;
                case ContainerData.RestoreMode.Command:
                    return M_COMMAND_ONLY;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
            }
        }

        void MessageRaw(IPlayer player, string message)
        {
            string prefix = GetMessage(
                player,
                M_PREFIX
            );

            player.Message(prefix + message);
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            string format = GetMessage(player, langKey);
            string prefix = GetMessage(player, M_PREFIX);

            player.Message(prefix + string.Format(format, args));
        }

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
        }

        #endregion

        #region Data load

        List<ContainerData> LoadData()
        {
            try
            {
                var list = Interface.Oxide.DataFileSystem.ReadObject<List<ContainerData>>(DATAFILE_NAME);

                if (list == null)
                {
                    throw new Exception("Data is null");
                }

                return list;
            }
            catch (Exception e)
            {
                LogError("Failed to load plugin data: {0}", e.Message);

                return new List<ContainerData>();
            }
        }

        void SaveData(List<ContainerData> list)
        {
            Interface.Oxide.DataFileSystem.WriteObject(DATAFILE_NAME, list);
        }

        #endregion

        #region Configuration load

        protected override void LoadDefaultConfig()
        {
            settings = PluginSettings.Default;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null)
                {
                    throw new Exception("Configuration is null");
                }
            }
            catch (Exception e)
            {
                LogError("Failed to load plugin configuration: {0}", e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Nested types

        #region ReplenishController

        class ReplenishController : MonoBehaviour
        {
            List<ContainerData> allContainers;
            ContainerData       nextRestoreContainer;

            public ContainerData FindDataById(int id)
            {
                if (id <= 0 || id > allContainers.Count)
                {
                    return null;
                }

                return allContainers[id - 1];
            }

            public ContainerData FindDataByEntity(BaseNetworkable entity)
            {
                return allContainers.Find(c => c.NetId == entity.net.ID);
            }

            public int GetDataIndex(ContainerData data)
            {
                return allContainers.IndexOf(data) + 1;
            }

            public void Init(List<ContainerData> data)
            {
                data.ForEach(d => d.OnRestored());
                allContainers = data;
            }

            public List<ContainerData> GetSaveData()
            {
                return allContainers;
            }

            public ContainerData SaveContainer(StorageContainer container, float timer)
            {
                ContainerData cData = FindDataByEntity(container);

                if (cData == null)
                {
                    cData = new ContainerData(container, timer);
                    allContainers.Add(cData);
                }
                else
                {
                    cData.RestoreTime = timer;
                    cData.ItemList.Clear();
                    cData.ItemList.AddRange(container.inventory.itemList.Select(SerializableItem.FromItem));
                    cData.OnRestored();
                }

                UpdateQueue();

                return cData;
            }

            public bool RemoveContainer(StorageContainer container)
            {
                return RemoveSavedContainer(FindDataByEntity(container));
            }

            public bool RemoveContainer(int containerId)
            {
                return RemoveSavedContainer(FindDataById(containerId));
            }

            public bool RestoreNow(ContainerData data)
            {
                Assert.IsTrue(nextRestoreContainer != null, "NextRestoreContainer is null in RestoreTick!");

                var ent = BaseNetworkable.serverEntities.Find(data.NetId) as StorageContainer;

                bool shouldRestore = false;

                if (!ent)
                {
                    if (Instance.settings.RespawnContainer)
                    {
                        ent = RespawnContainer(data);
                        shouldRestore = true;
                    }
                    else
                    {
                        allContainers.Remove(data);
                    }
                }
                else
                {
                    shouldRestore = CheckEmpty(ent, data);
                }

                if (shouldRestore)
                {
                    BeforeRestore(ent);

                    RestoreContainer(ent, data);

                    AfterRestore(data);
                }

                return shouldRestore;
            }

            bool RemoveSavedContainer(ContainerData data)
            {
                if (data == null)
                {
                    return false;
                }

                if (nextRestoreContainer == data)
                {
                    nextRestoreContainer = null;
                }

                allContainers.Remove(data);

                UpdateQueue();

                return true;
            }

            void Start()
            {
                Invoke(nameof(UpdateQueue), 0.1f);

                if (Instance.isNewWipe)
                {
                    RestoreWipeContainers();
                }
            }

            StorageContainer RespawnContainer(ContainerData data)
            {
                var container = (StorageContainer)GameManager.server.CreateEntity(data.PrefabName, data.Position);

                container.Spawn();

                data.NetId = container.net.ID;

                return container;
            }

            void RestoreWipeContainers()
            {
                var list = allContainers.FindAll(c => c.Mode == ContainerData.RestoreMode.Wipe);

                for (int i = 0; i < list.Count; i++)
                {
                    var data = list[i];

                    var container = BaseNetworkable.serverEntities.Find(data.NetId) as StorageContainer;

                    if (!container)
                    {
                        if (!Instance.settings.RespawnContainer)
                        {
                            allContainers.Remove(data);
                            continue;
                        }

                        container = RespawnContainer(data);
                    }
                    else if (!CheckEmpty(container, data))
                    {
                        continue;
                    }

                    BeforeRestore(container);

                    RestoreContainer(container, data);
                }
            }

            void RestoreTick()
            {
                Assert.IsTrue(nextRestoreContainer != null, "NextRestoreContainer is null in RestoreTick!");

                var ent = BaseNetworkable.serverEntities.Find(nextRestoreContainer.NetId) as StorageContainer;

                bool shouldRestore = false;

                if (!ent)
                {
                    if (Instance.settings.RespawnContainer)
                    {
                        ent = RespawnContainer(nextRestoreContainer);
                        shouldRestore = true;
                    }
                    else
                    {
                        allContainers.Remove(nextRestoreContainer);
                    }
                }
                else
                {
                    shouldRestore = CheckEmpty(ent, nextRestoreContainer);
                }

                if (shouldRestore)
                {
                    BeforeRestore(ent);

                    RestoreContainer(ent, nextRestoreContainer);
                }

                AfterRestore(nextRestoreContainer);
            }

            bool CheckEmpty(StorageContainer container, ContainerData data)
            {
                if (Instance.settings.RequiresEmpty && container.inventory.itemList.Any())
                {
                    return false;
                }

                if (data.ItemList.Count > container.inventory.capacity - container.inventory.itemList.Count)
                {
                    return false;
                }

                return true;
            }

            void BeforeRestore(StorageContainer container)
            {
                if (Instance.settings.ClearContainerInventory)
                {
                    container.inventory.Clear();
                    ItemManager.DoRemoves();
                }
            }

            void RestoreContainer(StorageContainer container, ContainerData data)
            {
                for (int i = 0; i < data.ItemList.Count; i++)
                {
                    var item = data.ItemList[i].ToItem();

                    item.MoveToContainer(container.inventory);
                }
            }

            void AfterRestore(ContainerData data)
            {
                data.OnRestored();
                nextRestoreContainer = null;

                UpdateQueue();
            }

            void UpdateQueue()
            {
                CancelInvoke(nameof(RestoreTick));

                nextRestoreContainer = null;

                if (allContainers.Count == 0)
                {
                    return;
                }

                var data = allContainers.OrderBy(c => c.TimeLeftBeforeRestore()).FirstOrDefault();

                if (data != null)
                {
                    nextRestoreContainer = data;
                    Invoke(nameof(RestoreTick), data.TimeLeftBeforeRestore());
                }
            }
        }

        #endregion

        #region SerializableItem

        struct SerializableItem
        {
            public int   ItemId;
            public int   Amount;
            public ulong SkinId;

            public static SerializableItem FromItem(Item item)
            {
                return new SerializableItem {
                    ItemId = item.info.itemid,
                    Amount = item.amount,
                    SkinId = item.skin
                };
            }

            public Item ToItem()
            {
                var item = ItemManager.CreateByItemID(ItemId, Amount, SkinId);

                return item;
            }
        }

        #endregion

        #region ContainerData

        class ContainerData
        {
            public uint                   NetId;
            public Vector3                Position;
            public List<SerializableItem> ItemList;
            public string                 PrefabName;
            public float                  RestoreTime;

            [JsonIgnore] public float NextRestoreTime;

            [JsonIgnore]
            public RestoreMode Mode => RestoreTime < 0 ? RestoreMode.Wipe :
                RestoreTime == 0 ? RestoreMode.Command : RestoreMode.Timed;

            public ContainerData() { }

            public ContainerData(StorageContainer container, float timer)
            {
                NetId = container.net.ID;
                Position = container.transform.position;
                RestoreTime = timer;
                ItemList = new List<SerializableItem>();

                var items = container.inventory.itemList;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];

                    ItemList.Add(SerializableItem.FromItem(item));
                }

                PrefabName = container.PrefabName;
                OnRestored();
            }

            public float TimeLeftBeforeRestore()
            {
                if (Mode != 0)
                {
                    return float.MaxValue;
                }

                return NextRestoreTime - Time.realtimeSinceStartup;
            }

            public void OnRestored()
            {
                if (Mode == 0)
                {
                    NextRestoreTime = Time.realtimeSinceStartup + RestoreTime;
                }
            }

            public enum RestoreMode
            {
                Timed,
                Wipe,
                Command
            }
        }

        #endregion

        #region PluginSettings

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings {
                ClearContainerInventory = true,
                DefaultReplenishTimer = 1800f,
                RespawnContainer = false,
                RequiresEmpty = false
            };

            [JsonProperty("Clean container inventory upon replenish")]
            public bool ClearContainerInventory { get; set; }

            [JsonProperty("Default timer (0 for command-only, -1 for new wipe replenish)")]
            public float DefaultReplenishTimer { get; set; }

            [JsonProperty("Respawn container if it is destroyed")]
            public bool RespawnContainer { get; set; }

            [JsonProperty("Requires container to be empty (destroyed)")]
            public bool RequiresEmpty { get; set; }
        }

        #endregion

        #endregion
    }
}
