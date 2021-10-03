using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

using Facepunch;

using Oxide.Core.Libraries.Covalence;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Entity Cleanup", "2CHEVSKII", "4.0.0")]
    [Description("Easy way to cleanup your server from unnecessary entities.")]
    public class EntityCleanup : CovalencePlugin
    {
        const string PERMISSION_USE = "entitycleanup.use";

        const string M_PREFIX           = "Chat prefix",
                     M_NO_PERMISSION    = "No permission",
                     M_INVALID_USAGE    = "Invalid usage",
                     M_CLEANUP_STARTED  = "Cleanup started",
                     M_CLEANUP_FINISHED = "Cleanup finished",
                     M_CLEANUP_RUNNING  = "Cleanup running";

        PluginSettings settings;
        CleanupHandler handler;

        #region Command handler

        void CommandHandler(IPlayer player, string command, string[] args)
        {
            if (!player.HasPermission(PERMISSION_USE))
            {
                Message(player, M_NO_PERMISSION);
                return;
            }

            switch (args.Length)
            {
                case 0:
                    if (!handler.TryStartCleanup())
                    {
                        Message(player, M_CLEANUP_RUNNING);
                    }

                    return;
                default:
                    Message(player, M_INVALID_USAGE);
                    break;
            }
        }

        #endregion
        
        #region Oxide hooks

        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            AddCovalenceCommand("entitycleanup", "CommandHandler");
        }

        void OnServerInitialized()
        {
            handler = ServerMgr.Instance.gameObject.AddComponent<CleanupHandler>();
            handler.Init(this);
        }

        void Unload()
        {
            handler.Shutdown();
        }

        #endregion

        #region LangAPI

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(
                new Dictionary<string, string> {
                    [M_PREFIX] = "[Entity Cleanup] ",
                    [M_NO_PERMISSION] = "<color=red>You have no access to this command</color>",
                    [M_INVALID_USAGE] = "Invalid command usage",
                    [M_CLEANUP_STARTED] = "Cleaning up old server entities...",
                    [M_CLEANUP_FINISHED] = "Cleanup completed, purged <color=#34ebba>{0}</color> old entities",
                    [M_CLEANUP_RUNNING] = "Cleanup is already running, wait until it completes"
                },
                this
            );
        }

        string GetMessage(IPlayer player, string langKey)
        {
            return lang.GetMessage(langKey, this, player.Id);
        }

        void Message(IPlayer player, string langKey, params object[] args)
        {
            var prefix = GetMessage(player, M_PREFIX);
            var format = GetMessage(player, langKey);
            var message = string.Format(format, args);

            player.Message(prefix + message);
        }

        void Announce(string langKey, params object[] args)
        {
            foreach (IPlayer player in players.Connected)
            {
                Message(player, langKey, args);
            }
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
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region Nested types

        #region CleanupHandler

        class CleanupHandler : MonoBehaviour
        {
            PluginSettings        settings;
            List<BaseNetworkable> entityList;
            Coroutine             cleanupRoutine;
            HashSet<string>       deployables;

            bool IsCleanupRunning => cleanupRoutine != null;

            event Action      OnCleanupStarted;
            event Action<int> OnCleanupComplete;

            public void Init(EntityCleanup plugin)
            {
                settings = plugin.settings;

                OnCleanupStarted = () =>
                {
                    plugin.Announce(M_CLEANUP_STARTED);
                };

                OnCleanupComplete = purgedEntityCount =>
                {
                    plugin.Announce(M_CLEANUP_FINISHED, purgedEntityCount);
                };

                entityList = Pool.GetList<BaseNetworkable>();
                deployables = Pool.Get<HashSet<string>>();

                var modDeployables = from itemDef in ItemManager.GetItemDefinitions()
                                     group itemDef by itemDef.GetComponent<ItemModDeployable>()
                                     into deps where deps.Key != null select deps.Key;

                foreach (var prefab in from depl in modDeployables select depl.entityPrefab.resourcePath)
                {
                    deployables.Add(prefab);
                }
            }

            public void Shutdown()
            {
                CancelInvoke();

                if (IsCleanupRunning)
                {
                    StopCoroutine(cleanupRoutine);
                    cleanupRoutine = null;
                }

                Pool.FreeList(ref entityList);
                deployables.Clear();
                Pool.Free(ref deployables);
                Destroy(this);
            }

            public bool TryStartCleanup()
            {
                if (IsCleanupRunning)
                {
                    return false;
                }

                StartCleanup();
                InitializeTimedCleanup();

                return true;
            }

            #region Unity messages

            void Start()
            {
                InitializeTimedCleanup();
            }

            #endregion

            void InitializeTimedCleanup()
            {
                CancelInvoke(nameof(StartCleanup));

                if (settings.Interval > 0)
                {
                    InvokeRepeating(nameof(StartCleanup), settings.Interval, settings.Interval);
                }
            }

            void StartCleanup()
            {
                if (IsCleanupRunning)
                {
                    return;
                }

                cleanupRoutine = StartCoroutine(Cleanup());
            }

            IEnumerator Cleanup()
            {
                entityList.AddRange(BaseNetworkable.serverEntities);
                int cleanedCount = 0;

                OnCleanupStarted();

                for (int i = 0; i < entityList.Count; i++)
                {
                    var entity = entityList[i] as BaseEntity;

                    if (entity && IsCleanupCandidate(entity))
                    {
                        entity.Kill(BaseNetworkable.DestroyMode.Gib);
                        cleanedCount++;
                    }

                    yield return new WaitForEndOfFrame();
                }

                OnCleanupComplete(cleanedCount);

                cleanupRoutine = null;
            }

            bool IsCleanupCandidate(BaseEntity entity)
            {
                if (entity.parentEntity.IsSet())
                {
                    return false;
                }

                if (entity.OwnerID == 0ul)
                {
                    return false;
                }

                if (
                    settings.Whitelist.Contains(entity.ShortPrefabName) ||
                    settings.Whitelist.Contains(entity.PrefabName)
                )
                {
                    return false;
                }

                if (entity is StabilityEntity && settings.CleanupBuildings ||
                    deployables.Contains(entity.gameObject.name) && settings.CleanupDeployables)
                {
                    BuildingPrivlidge buildingPrivilege = entity.GetBuildingPrivilege();
                    bool hasBuildingPrivilege = buildingPrivilege != null;

                    if (hasBuildingPrivilege)
                    {
                        if (!settings.RemoveInsidePrivilege)
                        {
                            return false;
                        }

                        if (settings.CheckOwnerIdPrivilegeAuthorized && buildingPrivilege.IsAuthed(entity.OwnerID))
                        {
                            return false;
                        }

                        return settings.InsideHealthFractionTheshold == 0f || entity.Health() / entity.MaxHealth() < settings.InsideHealthFractionTheshold;
                    }

                    if (!settings.RemoveOutsidePrivilege)
                    {
                        return false;
                    }
                    
                    return settings.OutsideHealthFractionTheshold == 0f || entity.Health() / entity.MaxHealth() < settings.OutsideHealthFractionTheshold;;
                }

                return false;
            }
        }

        #endregion

        #region PluginSettings

        class PluginSettings
        {
            public static PluginSettings Default => new PluginSettings {
                Interval = 3600,
                CleanupBuildings = true,
                CleanupDeployables = true,
                RemoveOutsidePrivilege = true,
                OutsideHealthFractionTheshold = 0.2f,
                RemoveInsidePrivilege = false,
                InsideHealthFractionTheshold = 0f,
                Whitelist = Array.Empty<string>(),
                CheckOwnerIdPrivilegeAuthorized = true
            };

            public int      Interval                        { get; set; }
            public bool     CleanupBuildings                { get; set; }
            public bool     CleanupDeployables              { get; set; }
            public bool     RemoveOutsidePrivilege          { get; set; }
            public float    OutsideHealthFractionTheshold   { get; set; }
            public bool     RemoveInsidePrivilege           { get; set; }
            public float    InsideHealthFractionTheshold    { get; set; }
            public string[] Whitelist                       { get; set; }
            public bool     CheckOwnerIdPrivilegeAuthorized { get; set; }
        }

        #endregion

        #endregion
    }
}
