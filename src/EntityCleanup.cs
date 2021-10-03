#define DEBUG

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Facepunch;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Entity Cleanup", "2CHEVSKII", "3.0.3")]
    [Description("Easy way to cleanup your server from unnecessary entities.")]
    public class EntityCleanup : CovalencePlugin
    {
        const string PERMISSION_USE = "entitycleanup.use";

        const string M_PREFIX           = "Chat prefix",
                     M_NO_PERMISSION    = "No permission",
                     M_HELP             = "Help message",
                     M_CLEANUP_STARTED  = "Cleanup started",
                     M_CLEANUP_FINISHED = "Cleanup finished",
                     M_CLEANUP_RUNNING  = "Cleanup running";

        PluginSettings settings;
        CleanupHandler handler;

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

                    return;

                case 1:

                    break;
            }

            Message(player, M_HELP);
        }

        #region Utility

        [Conditional("DEBUG")]
        static void LogDebug(string format, params object[] args)
        {
            Interface.Oxide.LogDebug("[EntityCleanup]" + format, args);
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
            handler.Init(settings);
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
                    [M_HELP] = "Usage: /entitycleanup <all|buildings|deployables|deployable prefab>",
                    [M_CLEANUP_STARTED] = "Cleaning up old server entities...",
                    [M_CLEANUP_FINISHED] = "Cleanup completed, purged <color=lightblue>{0}</color> old entities",
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

        class CleanupHandler : MonoBehaviour
        {
            PluginSettings        settings;
            List<BaseNetworkable> entityList;
            Coroutine             cleanupRoutine;
            HashSet<string>       deployables;

            bool IsCleanupRunning => cleanupRoutine != null;

            public void Init(PluginSettings settings)
            {
                this.settings = settings;
                entityList = Pool.GetList<BaseNetworkable>();

                var modDeployables = from itemDef in ItemManager.GetItemDefinitions()
                                     group itemDef by itemDef.GetComponent<ItemModDeployable>()
                                     into deps where deps.Key != null select deps.Key;

                LogDebug("Found {0} deployable prefabs", modDeployables.Count());

                foreach (var prefab in from depl in modDeployables select depl.entityPrefab.resourcePath)
                {
                    deployables.Add(prefab);
                }

                LogDebug(JsonConvert.SerializeObject(deployables, Formatting.Indented));
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
                LogDebug("Starting timed cleanup...");

                if (IsCleanupRunning)
                {
                    LogDebug("Cleanup is already running.");
                    return;
                }

                cleanupRoutine = StartCoroutine(Cleanup());
            }

            IEnumerator Cleanup()
            {
                entityList.AddRange(BaseNetworkable.serverEntities);
                int cleanedCount = 0;

                LogDebug("Current entity count: {0}", entityList.Count);

                for (int i = 0; i < entityList.Count; i++)
                {
                    var entity = entityList[i] as BaseEntity;

                    if (entity && IsCleanupCandidate(entity))
                    {
                        LogDebug("Removing entity {0} [{1}]", entity.ShortPrefabName, entity.net.ID);
                        entity.Kill(BaseNetworkable.DestroyMode.Gib);
                        cleanedCount++;
                    }

                    yield return new WaitForEndOfFrame();
                }

                LogDebug("Entities cleaned: {0}", cleanedCount);
                LogDebug("Resulting entity count: {0}", BaseNetworkable.serverEntities.Count);
                cleanupRoutine = null;
            }

            bool IsCleanupCandidate(BaseEntity entity)
            {
                // ignore child entities to avoid accidental removing of things like codelocks
                if (entity.parentEntity.IsSet())
                {
                    return false;
                }

                // ignore server created entities to avoid monument entities removal
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

                if (entity is BuildingBlock && !settings.CleanupBuildings)
                {
                    return false;
                }

                if (deployables.Contains(entity.gameObject.name) && !settings.CleanupDeployables)
                {
                    return false;
                }

                BuildingPrivlidge buildingPrivilege = entity.GetBuildingPrivilege();
                bool hasBuildingPrivilege = buildingPrivilege != null;

                if (hasBuildingPrivilege)
                {
                    if (!settings.RemoveOutsidePrivilege)
                    {
                        return false;
                    }

                    if (settings.CheckOwnerIdPrivilegeAuthorized && buildingPrivilege.IsAuthed(entity.OwnerID))
                    {
                        return false;
                    }

                    return entity.Health() / entity.MaxHealth() < settings.OutsideHealthFractionTheshold;
                }

                if (!settings.RemoveInsidePrivilege)
                {
                    return false;
                }

                return entity.Health() / entity.MaxHealth() < settings.InsideHealthFractionTheshold;
            }
        }

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

            public int Interval { get; set; }
            public bool CleanupBuildings { get; set; }
            public bool CleanupDeployables { get; set; }
            public bool RemoveOutsidePrivilege { get; set; }
            public float OutsideHealthFractionTheshold { get; set; }
            public bool RemoveInsidePrivilege { get; set; }
            public float InsideHealthFractionTheshold { get; set; }
            public string[] Whitelist { get; set; }
            public bool CheckOwnerIdPrivilegeAuthorized { get; set; }
        }

        #endregion

        //        #region Fields

        //        private PluginSettings Settings { get; set; }
        //        private const string PERMISSION = "entitycleanup.use";

        //        private HashSet<string> Deployables { get; } = new HashSet<string>();
        //        private Timer ScheduleTimer { get; set; }

        //        #endregion

        //        #region Config

        //        private class PluginSettings
        //        {
        //            [JsonProperty(PropertyName = "Scheduled cleanup seconds (x <= 0 to disable)")]
        //            internal int ScheduledCleanup { get; set; }
        //            [JsonProperty(PropertyName = "Scheduled cleanup building blocks")]
        //            internal bool ScheduledCleanupBuildings { get; set; }
        //            [JsonProperty(PropertyName = "Scheduled cleanup deployables")]
        //            internal bool ScheduledCleanupDeployables { get; set; }
        //            [JsonProperty(PropertyName = "Scheduled cleanup outside cupboard range")]
        //            internal bool ScheduledCleanupOutsideCupRange { get; set; }
        //            [JsonProperty(PropertyName = "Scheduled cleanup entities with hp less than specified (x = [0.0-1.0])")]
        //            internal float ScheduledCleanupDamaged { get; set; }
        //        }

        //        protected override void LoadConfig()
        //        {
        //            base.LoadConfig();
        //            try
        //            {
        //                Settings = Config.ReadObject<PluginSettings>();
        //                if (Settings == null)
        //                    throw new JsonException("Can't read config...");
        //                else
        //                    Puts("Configuration loaded...");
        //            }
        //            catch
        //            {
        //                LoadDefaultConfig();
        //            }
        //        }

        //        protected override void LoadDefaultConfig()
        //        {
        //            Config.Clear();
        //            Settings = GetDefaultSettings();
        //            SaveConfig();
        //            PrintWarning("Default configuration created...");
        //        }

        //        protected override void SaveConfig() => Config.WriteObject(Settings, true);

        //        private PluginSettings GetDefaultSettings() => new PluginSettings
        //        {
        //            ScheduledCleanup = 3600,
        //            ScheduledCleanupBuildings = true,
        //            ScheduledCleanupDeployables = true,
        //            ScheduledCleanupOutsideCupRange = true,
        //            ScheduledCleanupDamaged = 0f
        //        };

        //        #endregion

        //        #region LangAPI

        //        private const string mhelp = "Help message";
        //        private const string mnoperm = "No permissions";
        //        private const string mannounce = "Announcement";

        //        private Dictionary<string, string> DefaultMessages_EN { get; } = new Dictionary<string, string>
        //        {
        //            { mhelp, "Usage: cleanup (<all/buildings/deployables/deployable partial name>) (all)" },
        //            { mnoperm, "You have no access to that command" },
        //            { mannounce, "Server is cleaning up <color=#00FF5D>{0}</color> entities..." }
        //        };

        //        protected override void LoadDefaultMessages() => lang.RegisterMessages(DefaultMessages_EN, this, "en");

        //        private string GetReply(BasePlayer player, string message, params object[] args) => string.Format(lang.GetMessage(message, this, player?.UserIDString), args);

        //        #endregion

        //        #region Hooks

        //        private void Init()
        //        {
        //            permission.RegisterPermission(PERMISSION, this);
        //            permission.GrantGroupPermission("admin", PERMISSION, this);
        //            cmd.AddConsoleCommand("cleanup", this, delegate (Arg arg)
        //            {
        //                return CommandHandler(arg);
        //            });
        //            InitDeployables();
        //        }

        //        private void OnServerInitialized()
        //        {
        //            if (Settings.ScheduledCleanup > 0)
        //                StartScheduleTimer();
        //        }

        //        #endregion

        //        #region Commands

        //        private bool CommandHandler(Arg arg)
        //        {
        //            BasePlayer player = arg.Player();

        //            if (player && !permission.UserHasPermission(player.UserIDString, PERMISSION))
        //                arg.ReplyWith(GetReply(player, mnoperm));
        //            else if (!arg.HasArgs())
        //                ScheduledCleanup();
        //            else
        //            {
        //                switch (arg.Args.Length)
        //                {
        //                    case 1:
        //                        switch (arg.Args[0].ToLower())
        //                        {
        //                            case "all":
        //                                ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>()));
        //                                break;
        //                            default:
        //                                ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>(), false, arg.Args[0]));
        //                                break;
        //                        }
        //                        break;
        //                    case 2:
        //                        switch (arg.Args[0].ToLower())
        //                        {
        //                            case "all":
        //                                if (arg.Args[1].ToLower() == "all")
        //                                    ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>(), true));
        //                                else
        //                                    arg.ReplyWith(GetReply(player, mhelp));
        //                                break;
        //                            default:
        //                                if (arg.Args[1].ToLower() == "all")
        //                                    ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>(), true, arg.Args[1]));
        //                                else
        //                                    arg.ReplyWith(GetReply(player, mhelp));
        //                                break;
        //                        }
        //                        break;
        //                    default:
        //                        arg.ReplyWith(GetReply(player, mhelp));
        //                        break;
        //                }
        //            }
        //            return true;
        //        }

        //        #endregion

        //        #region Core

        //        private IEnumerator CollectData(List<BaseEntity> entities, bool all = false, string name = null)
        //        {
        //            IEnumerator<BaseNetworkable> enumerator = BaseNetworkable.serverEntities.GetEnumerator();
        //#if DEBUG
        //			Puts("Started data collect");
        //#endif
        //            while (enumerator.MoveNext())
        //            {
        //                BaseEntity baseEntity = enumerator.Current as BaseEntity;

        //                var parentEntity = baseEntity?.GetParentEntity();

        //                while (parentEntity != null && !parentEntity.IsDestroyed)
        //                {
        //                    baseEntity = parentEntity;
        //                    parentEntity = baseEntity?.GetParentEntity();
        //                }

        //                if (baseEntity == null)
        //                {
        //#if DEBUG
        //					Puts("Skipped not a baseEntity");
        //#endif
        //                    yield return new WaitForEndOfFrame();
        //                    continue;
        //                }

        //                if (baseEntity.OwnerID == 0)
        //                {
        //#if DEBUG
        //					Puts("Skipped baseEntity without ownerid");
        //#endif
        //                    yield return new WaitForEndOfFrame();

        //                    continue;
        //                }

        //                if (baseEntity.GetBuildingPrivilege() != null && !all && (baseEntity.Health() / baseEntity.MaxHealth()) > Settings.ScheduledCleanupDamaged)
        //                {
        //#if DEBUG
        //					Puts("Skipped BE with BP or HP");
        //#endif
        //                    yield return new WaitForEndOfFrame();
        //                    continue;
        //                }

        //                if ((name == null || name.ToLower() == "buildings") && baseEntity is StabilityEntity)
        //                {
        //#if DEBUG
        //					Puts("Added building block");
        //#endif
        //                    entities.Add(baseEntity);
        //                    yield return new WaitForEndOfFrame();
        //                    continue;
        //                }

        //                if (((name == null || name.ToLower() == "deployables") && Deployables.Contains(baseEntity.gameObject.name))
        //                    || (name != null && baseEntity.gameObject.name.Contains(name, CompareOptions.IgnoreCase)))
        //                {
        //#if DEBUG
        //					Puts("Added deployable");
        //#endif
        //                    entities.Add(baseEntity);
        //                    yield return new WaitForEndOfFrame();
        //                    continue;
        //                }
        //            }

        //            if (entities.Count < 1)
        //            {
        //#if DEBUG
        //				Puts("Attempting to clean, but nothing to be cleaned");
        //#endif
        //                yield break;
        //            }

        //            ServerMgr.Instance.StartCoroutine(Cleanup(entities));
        //        }

        //        private IEnumerator Cleanup(List<BaseEntity> entities)
        //        {
        //            Server.Broadcast(GetReply(null, mannounce, entities.Count));

        //            for (int i = 0; i < entities.Count; i++)
        //            {
        //                if (!entities[i].IsDestroyed)
        //                {
        //                    entities[i].Kill(BaseNetworkable.DestroyMode.None);
        //                    yield return new WaitForSeconds(0.05f);
        //                }
        //            }
        //#if DEBUG
        //			Puts($"Cleanup finished, {entities.Count} entities cleaned.");
        //#endif
        //        }

        //        #endregion

        //        #region Utility

        //        private void InitDeployables()
        //        {
        //            IEnumerable<ItemModDeployable> deps = from def in ItemManager.GetItemDefinitions()
        //                                                  where def.GetComponent<ItemModDeployable>() != null
        //                                                  select def.GetComponent<ItemModDeployable>();

        //            Puts($"Found {deps.Count()} deployables definitions");

        //            foreach (ItemModDeployable dep in deps)
        //                if (!Deployables.Contains(dep.entityPrefab.resourcePath))
        //                    Deployables.Add(dep.entityPrefab.resourcePath);
        //        }

        //        private void StartScheduleTimer()
        //        {
        //            if (ScheduleTimer != null && !ScheduleTimer.Destroyed)
        //                ScheduleTimer.Destroy();
        //            ScheduleTimer = timer.Once(Settings.ScheduledCleanup, () => ScheduledCleanup());
        //        }

        //        private void ScheduledCleanup()
        //        {
        //#if DEBUG
        //			Puts("Scheduled CU triggered");
        //#endif
        //            if (Settings.ScheduledCleanupBuildings && Settings.ScheduledCleanupDeployables)
        //            {
        //#if DEBUG
        //				Puts("Scheduled CU all");
        //#endif
        //                ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>()));
        //            }
        //            else if (Settings.ScheduledCleanupBuildings)
        //            {
        //#if DEBUG
        //				Puts("Scheduled CU building");
        //#endif
        //                ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>(), false, "buildings"));
        //            }
        //            else if (Settings.ScheduledCleanupDeployables)
        //            {
        //#if DEBUG
        //				Puts("Scheduled CU deployable");
        //#endif
        //                ServerMgr.Instance.StartCoroutine(CollectData(new List<BaseEntity>(), false, "deployables"));
        //                if (Settings.ScheduledCleanup > 0)
        //                    StartScheduleTimer();
        //            }
        //        }

        //        #endregion
    }
}
