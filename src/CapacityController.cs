using Newtonsoft.Json;

using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Capacity Controller", "2CHEVSKII", "2.1.0")]
    [Description("Allows capacity modification of certain containers.")]
    class CapacityController : CovalencePlugin
    {
        #region Fields

        Configuration config;
        IEnumerator routine;
        Dictionary<string, string> shortNameToDisplay;

        #endregion

        #region Oxide hooks

        void Init()
        {
            shortNameToDisplay = new Dictionary<string, string>();

            Unsubscribe("OnEntitySpawned");
            Unsubscribe("OnItemAddedToContainer");
        }

        void OnServerInitialized()
        {
            routine = GetContainers(() =>
            {
                Subscribe("OnEntitySpawned");
                Subscribe("OnItemAddedToContainer");

                routine = null;
            });

            ServerMgr.Instance.StartCoroutine(routine);
        }

        void OnEntitySpawned(StorageContainer entity)
        {
            string name;

            if (shortNameToDisplay.TryGetValue(entity.ShortPrefabName, out name) && config.deployables.ContainsKey(name))
            {
                entity.inventory.capacity = config.deployables[name];
                entity.SendNetworkUpdate();
            }
        }

        void OnItemAddedToContainer(ItemContainer _, Item item)
        {
            var def = item.info;

            if (config.weapons.ContainsKey(def.displayName.english))
            {
                item.contents.capacity = config.weapons[def.displayName.english];
            }
        }

        void Unload()
        {
            if (routine != null)
            {
                ServerMgr.Instance.StopCoroutine(routine);
            }
        }

        #endregion

        #region Core

        IEnumerator GetContainers(Action callback)
        {
            var defs = ItemManager.GetItemDefinitions();
            foreach (var def in defs)
            {
                var modDeployable = def.GetComponent<ItemModDeployable>();

                if (modDeployable)
                {
                    var ent = GameManager.server.CreatePrefab(modDeployable.entityPrefab.resourcePath, false)?.GetComponent<StorageContainer>();

                    if (ent)
                    {
                        shortNameToDisplay[ent.ShortPrefabName] = def.displayName.english;
                        if (!config.deployables.ContainsKey(def.displayName.english))
                        {
                            config.deployables[def.displayName.english] = ent.inventorySlots;
                        }
                    }
                    UnityEngine.Object.Destroy(ent);
                }
                else
                {
                    var modContainer = def.GetComponent<ItemModContainer>();

                    if (modContainer)
                    {
                        var item = ItemManager.Create(def);
                        if (item != null && item.GetHeldEntity() is BaseProjectile && modContainer.capacity > 0)
                        {
                            if (!config.weapons.ContainsKey(def.displayName.english))
                            {
                                config.weapons[def.displayName.english] = modContainer.capacity;
                            }
                        }

                        item?.Remove();
                    }
                }
                yield return new WaitForEndOfFrame();
            }

            SaveConfig();

            callback();
        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            config = Configuration.Default;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                config = Config.ReadObject<Configuration>();

                if (config == null) throw new Exception();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(config);
        }

        class Configuration
        {
            public static Configuration Default => new Configuration
            {
                weapons = new Dictionary<string, int>(),
                deployables = new Dictionary<string, int>()
            };

            [JsonProperty("Weapons")]
            public Dictionary<string, int> weapons;
            [JsonProperty("Deployables")]
            public Dictionary<string, int> deployables;

            public void SetWeaponCapacity(string name, int capacity)
            {
                weapons[name] = capacity;
            }

            public void SetDeployableCapacity(string name, int capacity)
            {
                deployables[name] = capacity;
            }

            public int GetWeaponCapacity(string name)
            {
                if (weapons.ContainsKey(name))
                {
                    return weapons[name];
                }

                return -1;
            }

            public int GetDeployableCapacity(string name)
            {
                if (deployables.ContainsKey(name))
                {
                    return deployables[name];
                }

                return -1;
            }
        }

        #endregion
    }
}
