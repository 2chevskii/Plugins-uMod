using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Rust;
using Newtonsoft.Json;

/* TODO:
 * [x] Make option for other barricades
 * [x] Make option to check for authorization of player who placed barricade
 * [-] Make option to upkeep the wall as other building blocks
 */

// Original idea and plugin version => Guilty Spark

namespace Oxide.Plugins
{
    [Info("HighWallBarricades", "2CHEVSKII", "2.2.2")]
    [Description("Makes High External Walls decay.")]
    internal class HighWallBarricades : RustPlugin
    {

        #region -Fields-


        //Config
        private Configuration config;

        //Storages
        private Dictionary<DecayEntity, int> Storage { get; set; }
        private List<DecayEntity> DecayNeeded { get; set; }
        private Dictionary<DecayEntity, float> StandartProtection { get; set; }


        #endregion

        #region -Configuration-


        private class Configuration
        {
            [JsonProperty(PropertyName = "Time between decay ticks")]
            internal int DecayTime { get; set; }
            [JsonProperty(PropertyName = "Decay tick damage")]
            internal float DecayDamage { get; set; }
            [JsonProperty(PropertyName = "Barricade initial health")]
            internal float InitialHealth { get; set; }
            [JsonProperty(PropertyName = "Should barricades decay while inside the cupboard range")]
            internal bool DecayInCupRange { get; set; }
            [JsonProperty(PropertyName = "Check if owner of barricade is authorized")]
            internal bool CheckBarricadeOwner { get; set; }
            [JsonProperty(PropertyName = "Disable standart decay for barricades")]
            internal bool DisableStandartDecay { get; set; }
            [JsonProperty(PropertyName = "Enabled types of barricades")]
            internal Dictionary<string, bool> EnabledEntities { get; set; }
            [JsonProperty(PropertyName = "Configuration version (Needed for auto-update, don't modify)")]
            internal VersionNumber ConfigVersion { get; set; }
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                DecayTime = 600,
                DecayDamage = 0.2f,
                InitialHealth = 1.0f,
                DecayInCupRange = false,
                CheckBarricadeOwner = true,
                DisableStandartDecay = false,
                EnabledEntities = new Dictionary<string, bool>
                    {
                        { "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab", true },
                        { "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.concrete.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.metal.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.sandbags.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.stone.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.wood.prefab", true },
                        { "assets/prefabs/deployable/barricades/barricade.woodwire.prefab", true },
                        { "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab", true },
                        { "assets/prefabs/building/gates.external.high/gates.external.high.wood/gates.external.high.wood.prefab", true }
                    },
                ConfigVersion = Version
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<Configuration>();
                if(config == null)
                    throw new JsonException("Configuration failed to load, creating new one!");
            }
            catch
            {
                Config.Clear();
                config = GetDefaultConfig();
                SaveConfig();
            }
            if(config.ConfigVersion == null)
            {
                Config.Clear();
                config = GetDefaultConfig();
                SaveConfig();
                Puts("Configuration has been updated!");
            }
            if(config.ConfigVersion < Version)
            {
                if(config.ConfigVersion == new VersionNumber(2, 1, 0))
                {
                    var tempconfig = new Configuration
                    {
                        DecayTime = config.DecayTime,
                        DecayDamage = config.DecayDamage,
                        InitialHealth = config.InitialHealth,
                        DecayInCupRange = config.DecayInCupRange,
                        CheckBarricadeOwner = true,
                        DisableStandartDecay = config.DisableStandartDecay,
                        EnabledEntities = config.EnabledEntities
                    };
                    config = tempconfig;
                }
                config.ConfigVersion = Version;
                SaveConfig();
                Puts("Configuration has been updated!");
            }
            if(config.EnabledEntities == null)
            {
                config.EnabledEntities = new Dictionary<string, bool>
                {
                    { "assets/prefabs/building/wall.external.high.stone/wall.external.high.stone.prefab", true },
                    { "assets/prefabs/building/wall.external.high.wood/wall.external.high.wood.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.concrete.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.metal.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.sandbags.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.stone.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.wood.prefab", true },
                    { "assets/prefabs/deployable/barricades/barricade.woodwire.prefab", true },
                    { "assets/prefabs/building/gates.external.high/gates.external.high.stone/gates.external.high.stone.prefab", true },
                    { "assets/prefabs/building/gates.external.high/gates.external.high.wood/gates.external.high.wood.prefab", true }
                };
                SaveConfig();
            }
        }

        protected override void LoadDefaultConfig()
        {
            config = GetDefaultConfig();
            SaveConfig();
            Puts("New configuration file created...");
        }

        protected override void SaveConfig() => Config.WriteObject(config);


        #endregion

        #region -Oxide hooks-

        private void Init()
        {
            Storage = new Dictionary<DecayEntity, int>();
            DecayNeeded = new List<DecayEntity>();
        }

        private void OnServerInitialized()
        {
            foreach(var sbb in UnityEngine.Object.FindObjectsOfType<DecayEntity>())
                if(config.EnabledEntities.Keys.Contains(sbb.PrefabName) && config.EnabledEntities[sbb.PrefabName])
                {
                    Storage.Add(sbb, config.DecayTime);
                    if(config.DisableStandartDecay)
                        ProtectFromDecay(sbb);
                }
        }

        private void Loaded() => timer.Every(5f, () => CheckDecayNeeded());

        private void OnEntityBuilt(Planner planner, GameObject gameObject)
        {
            if(gameObject != null && gameObject.GetComponent<DecayEntity>() != null && (config.EnabledEntities.Keys.Contains(gameObject.GetComponent<DecayEntity>().PrefabName) && config.EnabledEntities[gameObject.GetComponent<DecayEntity>().PrefabName]) && !Storage.ContainsKey(gameObject.GetComponent<DecayEntity>()))
            {
                if(config.InitialHealth <= 0f)
                    gameObject.GetComponent<DecayEntity>().Kill(BaseNetworkable.DestroyMode.Gib);
                else
                {
                    Storage.Add(gameObject.GetComponent<DecayEntity>(), config.DecayTime);
                    gameObject.GetComponent<DecayEntity>().healthFraction = config.InitialHealth;
                    if(config.DisableStandartDecay)
                        ProtectFromDecay(gameObject.GetComponent<DecayEntity>());
                    gameObject.GetComponent<DecayEntity>().SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
                }
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            if(entity != null && entity.GetComponent<DecayEntity>() != null && Storage.ContainsKey(entity.GetComponent<DecayEntity>()))
                Storage.Remove(entity.GetComponent<DecayEntity>());
        }

        private void Unload()
        {
            if(StandartProtection != null)
                foreach(var kvp in StandartProtection)
                {
                    if(!kvp.Key.IsDestroyed)
                        kvp.Key.baseProtection.amounts[(int)DamageType.Decay] = kvp.Value;
                }
        }


        #endregion

        #region -Core-


        private void CheckDecayNeeded()
        {
            foreach(var block in Storage.Keys.ToList())
            {
                if(block.GetBuildingPrivilege() == null)
                {
                    DecayNeeded.Add(block);
                }
                else if(block.GetBuildingPrivilege() != null && config.DecayInCupRange)
                {
                    if(config.CheckBarricadeOwner && block.GetBuildingPrivilege().authorizedPlayers.Any(o => o.userid == block.OwnerID))
                        Storage[block] = config.DecayTime;
                    else
                        DecayNeeded.Add(block);
                }
                else
                    Storage[block] = config.DecayTime;
            }
            foreach(var block in DecayNeeded)
            {
                Storage[block] -= 5;
                if(Storage[block] <= 0)
                {
                    block.health -= block.MaxHealth() * config.DecayDamage;
                    Storage[block] = config.DecayTime;
                }
                if(block.health <= 0)
                {
                    block.Kill(BaseNetworkable.DestroyMode.Gib);
                    if(Storage.ContainsKey(block)) Storage.Remove(block);
                }
                block.SendNetworkUpdate(BasePlayer.NetworkQueue.Update);
            }
            DecayNeeded.Clear();
        }

        private void ProtectFromDecay(DecayEntity entity)
        {
            if(entity.baseProtection.Get(DamageType.Decay) != 100f)
            {
                if(StandartProtection == null) StandartProtection = new Dictionary<DecayEntity, float>();
                StandartProtection.Add(entity, entity.baseProtection.Get(DamageType.Decay));
                entity.baseProtection.amounts[(int)DamageType.Decay] = 100f;
            }
        }


        #endregion

    }
}