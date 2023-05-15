using System;
using System.Collections.Generic;

using Facepunch;

using JetBrains.Annotations;

using Newtonsoft.Json;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Realistic Explosions", "2CHEVSKII", "1.0.0")]
    [Description("Pushes back dropped items when they are near the explosion")]
    class RealisticExplosions : CovalencePlugin
    {
        static RealisticExplosions Instance;
        PluginSettings settings;

        #region Oxide hooks

        void Init()
        {
            Instance = this;
            RealisticExplosion.Init();
        }

        void Unload()
        {
            RealisticExplosion.Shutdown();
            Instance = null;
        }

        void OnExplosiveDropped(BasePlayer player, BaseEntity entity)
        {
            entity.gameObject.AddComponent<RealisticExplosion>();
        }

        void OnExplosiveThrown(BasePlayer player, BaseEntity entity)
        {
            entity.gameObject.AddComponent<RealisticExplosion>();
        }

        void OnRocketLaunched(BasePlayer player, BaseEntity entity)
        {
            entity.gameObject.AddComponent<RealisticExplosion>();
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
                LogError("Could not read configuration file:\n{0}", e.Message);
                LogWarning("Default configuration will be loaded");

                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        [UsedImplicitly]
        class RealisticExplosion : MonoBehaviour
        {
            static List<DroppedItem> ItemList;
            static List<BaseCorpse> CorpseList;

            BaseEntity entity;
            PluginSettings settings;

            public static void Init()
            {
                ItemList = Pool.GetList<DroppedItem>();
                CorpseList = Pool.GetList<BaseCorpse>();
            }

            public static void Shutdown()
            {
                Pool.Free(ref ItemList);
                Pool.Free(ref CorpseList);
            }

            void Awake()
            {
                settings = Instance.settings;
                entity = GetComponent<BaseEntity>();
            }

            void OnDestroy()
            {
                CollectItems();
                CollectCorpses();

                IterateEntities();

                Cleanup();
            }

            void IterateEntities()
            {
                for (var i = 0; i < ItemList.Count; i++)
                {
                    DroppedItem item = ItemList[i];

                    if (settings.CheckVisibility && !IsVisible(item))
                    {
                        continue;
                    }

                    Rigidbody rb = item.GetComponent<Rigidbody>();

                    ApplyForce(rb);
                }

                for (var i = 0; i < CorpseList.Count; i++)
                {
                    BaseCorpse corpse = CorpseList[i];

                    if (settings.CheckVisibility && !IsVisible(corpse))
                    {
                        continue;
                    }

                    Rigidbody rb = corpse.GetComponent<Rigidbody>();

                    ApplyForce(rb, true);
                }
            }

            void Cleanup()
            {
                ItemList.Clear();
                CorpseList.Clear();
            }

            void CollectItems()
            {
                if (!settings.AffectDroppedItems)
                {
                    return;
                }

                Vis.Entities(entity.transform.position, settings.ExplosionRadius, ItemList);
            }

            void CollectCorpses()
            {
                if (!settings.AffectRagdolls)
                {
                    return;
                }

                Vis.Entities(entity.transform.position, settings.ExplosionRadius, CorpseList);
            }

            void ApplyForce(Rigidbody rigidbody, bool isCorpse = false)
            {
                rigidbody.AddExplosionForce(
                    isCorpse ? settings.ExplosionForce * 5f : settings.ExplosionForce,
                    entity.transform.position,
                    settings.ExplosionRadius
                );
            }

            bool IsVisible(BaseEntity baseEntity)
            {
                return !settings.CheckVisibility
                    || baseEntity.IsVisible(entity.transform.position + new Vector3(0, 0.5f));
            }
        }

        class PluginSettings
        {
            public static PluginSettings Default =>
                new PluginSettings
                {
                    ExplosionRadius = 15f,
                    ExplosionForce = 500f,
                    CheckVisibility = false,
                    AffectDroppedItems = true,
                    AffectRagdolls = true
                };

            [JsonProperty("Explosion force")]
            public float ExplosionForce { get; set; }

            [JsonProperty("Explosion radius")]
            public float ExplosionRadius { get; set; }

            [JsonProperty("Check visibility from explosion to object (less performant)")]
            public bool CheckVisibility { get; set; }

            [JsonProperty("Affect ragdolls")]
            public bool AffectRagdolls { get; set; }

            [JsonProperty("Affect dropped items")]
            public bool AffectDroppedItems { get; set; }
        }
    }
}
