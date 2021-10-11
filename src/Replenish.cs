using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Replenish", "Skrallex & 2CHEVSKII", "2.0.0")]
    [Description("Save and restore items in selected containers")]
    class Replenish : CovalencePlugin
    {
        PluginSettings      settings;
        List<ContainerData> data;

        const string PERMISSION_USE = "replenish.use";

        const string M_PREFIX              = "Chat prefix",
                     M_NO_PERMISSION       = "No permission",
                     M_HELP                = "Help message",
                     M_CONTAINER_NOT_EMPTY = "Container is not empty",
                     M_CONTAINER_INFO      = "Container info",
                     M_CONTAINER_SET       = "Container set for replenish",
                     M_CONTAINER_UNSET     = "Container removed from replenish list",
                     M_TIMED               = "Timed restore",
                     M_WIPE_ONLY           = "Wipe restore",
                     M_COMMAND_ONLY        = "Command restore",
                     M_CONTAINER_NOT_FOUND = "Container not found",
                     M_CONTAINER_NOT_SAVED = "Container not saved",
                     M_NO_CONTAINERS_SAVED = "No saved containers";

        static Replenish Instance;

        bool isNewWipe;

        #region Command Handler

        void CommandHandler(IPlayer player, string _, string[] args)
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
                            return;
                        }
                        else
                        {
                            if (args.Length == 1)
                            {
                                SaveContainer(container, settings.DefaultReplenishTimer);

                                string msg = GetSaveTimeMessage(settings.DefaultReplenishTimer);

                                Message(player, M_CONTAINER_SET, string.Format(msg, settings.DefaultReplenishTimer.ToString("0")));
                                return;
                            }

                            switch (args[1].ToLower())
                            {
                                case "cmd":
                                    SaveContainer(container, 0f);

                                    Message(player, M_CONTAINER_SET, M_COMMAND_ONLY);
                                    return;

                                case "wipe":
                                    SaveContainer(container, -1f);

                                    Message(player, M_CONTAINER_SET, M_WIPE_ONLY);
                                    return;

                                default:
                                    float seconds;
                                    if (float.TryParse(
                                        args[1],
                                        NumberStyles.Number,
                                        CultureInfo.InvariantCulture,
                                        out seconds
                                    ))
                                    {
                                        SaveContainer(container, seconds);

                                        string msg = GetSaveTimeMessage(seconds);

                                        Message(player, M_CONTAINER_SET, string.Format(msg, seconds.ToString("0")));
                                        return;
                                    }
                                    break;
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

                            if (!RemoveContainer(container))
                            {
                                Message(player, M_CONTAINER_NOT_SAVED);
                                return;
                            }

                            Message(player, M_CONTAINER_UNSET);
                            return;
                        }
                        else
                        {
                            int id;

                            if (int.TryParse(args[1], out id))
                            {
                                if (!RemoveContainer(id))
                                {
                                    Message(player, M_CONTAINER_NOT_SAVED);
                                    return;
                                }

                                Message(player,M_CONTAINER_UNSET);
                                return;
                            }
                        }
                        break;

                    case "info":
                        int cId;
                        if (args.Length == 1)
                        {
                            if (container)
                            {
                                var cData = FindContainerByEntity(container);

                                if (cData == null)
                                {
                                    Message(player, M_CONTAINER_NOT_SAVED);
                                }
                                else
                                {
                                    MessageRaw(
                                        player,
                                        BuildContainerInfo(
                                            player,
                                            data.IndexOf(cData),
                                            cData.Position,
                                            cData.NetId,
                                            cData.ReplenishTimer
                                        )
                                    );
                                }
                            }
                            else
                            {
                                Message(player, M_CONTAINER_NOT_FOUND);
                                return;
                            }
                        }
                        else if(int.TryParse(args[0], out cId))
                        {
                            var cData = FindContainerById(cId);

                            if (cData == null)
                            {
                                Message(player,M_CONTAINER_NOT_FOUND);
                                return;
                            }
                            else
                            {
                                MessageRaw(
                                    player,
                                    BuildContainerInfo(
                                        player,
                                        data.IndexOf(cData),
                                        cData.Position,
                                        cData.NetId,
                                        cData.ReplenishTimer
                                    )
                                );
                            }
                        }
                        break;
                    case "list":
                        if (data.Count == 0)
                        {
                            Message(player, M_NO_CONTAINERS_SAVED);
                        }
                        else
                        {
                            StringBuilder builder = new StringBuilder();

                            for (int i = 0; i < data.Count; i++)
                            {
                                var cData = data[i];

                                var info = BuildContainerInfo(
                                    player,
                                    i,
                                    cData.Position,
                                    cData.NetId,
                                    cData.ReplenishTimer
                                );

                                builder.AppendLine(info);
                            }

                            MessageRaw(player, builder.ToString());
                        }
                        break;
                }
            }

            Message(player, M_HELP);
        }

            #endregion

        ContainerData FindContainerById(int id)
        {
            if (id > data.Count || id <= 0)
            {
                return null;
            }

            return data[id - 1];
        }

        ContainerData FindContainerByEntity(StorageContainer container)
        {
            return data.Find(c => c.NetId == container.net.ID);
        }

        bool RemoveContainer(StorageContainer container)
        {
            var cData = FindContainerByEntity(container);

            if (cData == null)
            {
                return false;
            }


        }

        bool RemoveContainer(int id)
        {
            var cData = FindContainerById(id);

            if (cData == null)
            {
                return false;
            }
        }

        void SaveContainer(StorageContainer container, float timer)
        {

        }

        void ListRestoreContainers(IPlayer player)
        {

        }

        void ShowContainerInfo(IPlayer player, StorageContainer container)
        {

        }

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

        bool CanRestoreContainer(StorageContainer container, out string reason)
        {

        }

        void DoContainerRestore(StorageContainer container, IEnumerable<SerializableItem> items)
        {
            List<Item> itemList = Pool.GetList<Item>();

            foreach (var item in items)
            {
                itemList.Add(item.ToItem());
            }

            for (int i = 0; i < itemList.Count; i++)
            {
                itemList[i].MoveToContainer(container.inventory, ignoreStackLimit: true);
            }

            Pool.FreeList(ref itemList);
        }

        void SetupRestore(List<ContainerData> dataList)
        {

        }

        #region Oxide hooks

        void Init()
        {
            Instance = this;

            permission.RegisterPermission(PERMISSION_USE, this);

            AddCovalenceCommand("replenish", "CommandHandler");
        }

        void OnServerInitialized()
        {
            data = LoadData();

            SetupRestore(data);
        }

        void OnNewSave()
        {
            isNewWipe = true;
        }

        #endregion

        #region Lang API

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string> {
                [M_PREFIX] = "[Replenish]",
                [M_NO_PERMISSION] = "You have no access to this command",
                [M_HELP] = "Usage: /replenish [command] <args>\n" +
                           "Commands:\n" +
                           "save (<digit|'wipe'|'cmd'>) - Save container you are currently looking at or change settings for container\n" +
                           "remove (<digit>) - Remove container from replenish\n" +
                           "info (<digit>) - Get information about container\n" +
                           "list - Get list of saved containers\n" +
                           "restore (<digit>) - Restore specified container\n" +
                           "restoreall - Restore all containers",
                [M_CONTAINER_SET] = "Replenish set for container #{0} {1}:{2} - {3}",
                [M_CONTAINER_UNSET] = "Replenish was unsed for container {0}:{1}",
                [M_CONTAINER_INFO] = "#{0}: {1}, {2} | {3}",
                [M_CONTAINER_NOT_EMPTY] = "Container must be empty",
                [M_TIMED] = "Restore every {0} seconds",
                [M_WIPE_ONLY] = "Restore every new wipe",
                [M_COMMAND_ONLY] = "Restore on command",
                [M_CONTAINER_NOT_FOUND] = "Container not found",
                [M_CONTAINER_NOT_SAVED] = "This container is not saved"
            }, this);
        }

        string BuildContainerInfo(
            IPlayer player,
            int index,
            Vector3 position,
            uint netId,
            float restoreTimer
        )
        {
            string fmt = GetMessage(player, M_CONTAINER_INFO);

            string msg = string.Format(
                fmt,
                index,
                $"{position.x:0}.{position.y:0}.{position.z:0}",
                netId,
                GetMessage(player, GetSaveTimeMessage(restoreTimer))
            );

            return msg;
        }

        string GetSaveTimeMessage(float time)
        {
            if (time == 0)
            {
                return M_COMMAND_ONLY;
            }

            if (time < 0)
                return M_WIPE_ONLY;

            return M_TIMED;
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
            const string filename = "Replenish";

            try
            {
                var dataFile = Interface.Oxide.DataFileSystem.GetFile(filename);

                var list = dataFile.ReadObject<List<ContainerData>>();

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
            const string filename = "Replenish";

            var datafile = Interface.Oxide.DataFileSystem.GetFile(filename);

            datafile.Clear();

            datafile.WriteObject(list);
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

        struct SerializableItem
        {
            public int   ItemId;
            public int   Amount;
            public ulong SkinId;

            public SerializableItem(Item item, bool destroyItem = false)
            {
                ItemId = item.info.itemid;
                Amount = item.amount;
                SkinId = item.skin;

                if (destroyItem)
                {
                    item.Remove();
                }
            }

            public Item ToItem()
            {
                var item = ItemManager.CreateByItemID(ItemId, Amount, SkinId);

                return item;
            }
        }

        class ContainerData
        {
            public uint                   NetId;
            public Vector3                Position;
            public List<SerializableItem> ItemList;
            public float                  ReplenishTimer;

            [JsonIgnore]
            public bool RestoreOnWipe => ReplenishTimer < 0;
            [JsonIgnore]
            public bool RestoreOnCommand => ReplenishTimer == 0;

            public ContainerData(StorageContainer container, float timer)
            {
                NetId = container.net.ID;
                Position = container.transform.position;
                ReplenishTimer = timer;
                ItemList = new List<SerializableItem>();

                var items = container.inventory.itemList;

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];

                    var sItem = new SerializableItem(item, false);

                    ItemList.Add(sItem);
                }
            }
        }

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

            [JsonProperty("Required container to be empty (destroyed)")]
            public bool RequiresEmpty { get; set; }
        }

        #endregion
    }
}
