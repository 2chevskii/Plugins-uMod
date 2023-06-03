using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Configuration;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("Where is My Horse", "2CHEVSKII", "2.0.0")]
    [Description("Here is your horse, sir!")]
    class WhereIsMyHorse : CovalencePlugin
    {
        private const string                    PERMISSION_USE = "whereismyhorse.use";
        private const string                    PERMISSION_USE_ON_PLAYER = "whereismyhorse.useonplayer";
        private const string                    HORSE_PREFAB = "assets/rust.ai/nextai/testridablehorse.prefab";
        private       Configuration             _configuration;
        private       List<HorseSpawnData>      _spawnData;

        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_USE_ON_PLAYER, this);

            foreach (string command in _configuration.Commands)
            {
                covalence.RegisterCommand(command, this, HandleCommand);
            }
        }

        #region Oxide hooks

        void OnServerInitialized()
        {
            LoadData();
        }

        void Unload()
        {
            SaveData();
        }

        #endregion

        bool HandleCommand(IPlayer player, string _, string[] args)
        {
            if (args.Length == 0)
            {
                HandleRegularCommand(player);
            }

            if (args[0] == "list")
            {
                HandleListCommand(player);
            }

            return true;
        }

        void HandleListCommand(IPlayer player)
        {
            if (!player.HasPermission(PERMISSION_USE))
            {
                MessagePlayer(player, Messages.NO_PERMISSION);
                return;
            }

            bool noHorses = true;

            var basePlayer = (BasePlayer)player.Object;
            for (var i = _spawnData.Count - 1; i >= 0; i--)
            {
                var data = _spawnData[i];
                if (!data.IsNetworkableAlive)
                {
                    _spawnData.RemoveAt(i);
                    continue;
                }

                if (data.OwnerId == player.Id)
                {
                    var ent = data.GetEntity();
                    string name = ent._name;
                    var breed = ent.breeds[ent.currentBreed].breedName.english;
                    var distance = Vector3.Distance(basePlayer.ServerPosition, ent.ServerPosition);

                    MessagePlayer(
                        player,
                        Messages.HORSE_INFO,
                        name,
                        breed,
                        distance
                    );

                    noHorses = false;
                }
            }

            if (noHorses)
            {
                MessagePlayer(player, Messages.NO_HORSES);
            }
        }

        void HandleRegularCommand(IPlayer player)
        {
            if (!player.HasPermission(PERMISSION_USE))
            {
                MessagePlayer(player, Messages.NO_PERMISSION);
                return;
            }

            int playerCooldown = GetPlayerCooldown(player);
            int cooldownSecondsLeft;
            if (IsPlayerOnCooldown(player, playerCooldown, out cooldownSecondsLeft))
            {
                MessagePlayer(player, Messages.COOLDOWN, cooldownSecondsLeft);
                return;
            }

            BasePlayer basePlayer = (BasePlayer)player.Object;

            if (IsHorseNearby(basePlayer.ServerPosition))
            {
                MessagePlayer(player, Messages.HORSE_NEARBY);
                return;
            }

            if (_configuration.UseNoEscape && GetIsEscapeBlocked(player))
            {
                MessagePlayer(player, Messages.ESCAPE_BLOCKED);
                return;
            }

            if (!_configuration.AllowInside && !basePlayer.IsOutside())
            {
                MessagePlayer(player, Messages.CANNOT_SPAWN_INSIDE_BUILDING);
                return;
            }

            Vector3 spawnPosition;
            if (!TryFindSpawnPosition(basePlayer, out spawnPosition))
            {
                MessagePlayer(player, Messages.NO_SPAWN_POSITION);
                return;
            }

            // TODO: Call CanSpawnWmHorse(player, player)

            RidableHorse poorAnimal = SpawnHorseAtPosition(spawnPosition, basePlayer);

            var spawnData = HorseSpawnData.FromRidableHorse(poorAnimal);

            _spawnData.Add(spawnData);

            Interface.CallHook("OnWmHorseSpawned", spawnData.ToDictionary());
        }

        RidableHorse SpawnHorseAtPosition(Vector3 position, BasePlayer owner)
        {
            BaseEntity poorAnimal = GameManager.server.CreateEntity(HORSE_PREFAB, position);
            poorAnimal.Spawn();
            poorAnimal.OwnerID = owner.userID;

            return (RidableHorse)poorAnimal;
        }

        bool TryFindSpawnPosition(BasePlayer player, out Vector3 spawnPoint)
        {
            object hookResult = Interface.CallHook("GetWmHorseSpawnPosition", player.UserIDString);

            if (hookResult is Vector3)
            {
                spawnPoint = (Vector3)hookResult;
                return true;
            }

            Vector3 playerPosition = player.ServerPosition;
            Quaternion playerRotation = player.eyes.rotation;

            const float SPAWN_DISTANCE = 3f;

            // TODO: Scanning from min to max for each angle
            const float SPAWN_DISTANCE_MIN = 1f;
            const float SPAWN_DISTANCE_MAX = 4f;
            const float SPAWN_DISTANCE_STEP = .2f;


            int mod = 1;
            for (int i = 0; i <= 90; i++)
            {
                Quaternion rotation = playerRotation * Quaternion.Inverse(Quaternion.AngleAxis(i * mod, Vector3.up));
                Vector3 direction = rotation * Vector3.forward * SPAWN_DISTANCE;
                Vector3 pos = playerPosition + direction;

                float height = TerrainMeta.HeightMap.GetHeight(pos);

                List<BuildingBlock> list = Pool.GetList<BuildingBlock>();
                Vis.Entities(pos, 1f, list);

                if (list.Count != 0)
                {
                    BuildingBlock bb = list[0];
                    height = bb.transform.position.y;
                }

                Pool.FreeList(ref list);

                if (height > 0 && Math.Abs(height - playerPosition.y) <= 5f)
                {
                    pos.y = height;
                    spawnPoint = pos;
                    return true;
                }

                if (i == 90)
                {
                    if (mod == -1)
                    {
                        break;
                    }

                    i = mod = -1;
                }
            }

            spawnPoint = default(Vector3);
            return false;
        }

        HorseSpawnData GetMostRecentHorseSpawnData(IPlayer player)
        {
            HorseSpawnData data = _spawnData.Where(x => x.OwnerId == player.Id)
                                            .OrderBy(x => x.SpawnRealtimeSinceStartup)
                                            .FirstOrDefault();

            return data;
        }

        void SetPlayerCooldown(IPlayer player)
        {
            _lastSpawnTimeIndex[player.Id] = Time.realtimeSinceStartup;
        }

        bool IsPlayerOnCooldown(IPlayer player, int cooldown, out int cooldownSecondsLeft)
        {
            cooldownSecondsLeft = 0;
            HorseSpawnData lastSpawnData = GetMostRecentHorseSpawnData(player);
            if (lastSpawnData == null)
                return false;

            float timeSinceLastSpawn = Time.realtimeSinceStartup - lastSpawnData.SpawnRealtimeSinceStartup;

            float cooldownLeft = cooldown - timeSinceLastSpawn;
            if (cooldownLeft >= 1)
            {
                cooldownSecondsLeft = Mathf.CeilToInt(cooldownLeft);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Returns smallest cooldown based on groups player is assigned to and configuration file
        /// </summary>
        /// <param name="player"></param>
        /// <returns></returns>
        int GetPlayerCooldown(IPlayer player)
        {
            KeyValuePair<string, int>[] playerCooldownGroups =
                _configuration.CooldownGroups.Where(group => player.BelongsToGroup(group.Key)).ToArray();

            if (playerCooldownGroups.Length == 0)
            {
                return _configuration.DefaultCooldown;
            }

            return playerCooldownGroups.Min(x => x.Value);
        }

        bool IsHorseNearby(Vector3 position)
        {
            List<RidableHorse> list = Pool.GetList<RidableHorse>();
            Vis.Entities(position, _configuration.NearbyRange, list);
            bool isNearby = list.Count != 0;
            Pool.FreeList(ref list);
            return isNearby;
        }

        bool GetIsEscapeBlocked(IPlayer player)
        {
            object result = Interface.CallHook("IsEscapeBlocked", player.Id);
            if (result != null && result is bool && (bool)result == true)
            {
                return true;
            }

            return false;
        }

        #region Message handling

        void MessagePlayer(IPlayer player, string message, params object[] args)
        {
            string prefix = lang.GetMessage(Messages.PREFIX, this, player.Id);
            string format = lang.GetMessage(message, this, player.Id);

            string fullFormat;

            if (string.IsNullOrEmpty(prefix))
            {
                fullFormat = format;
            }
            else
            {
                fullFormat = prefix + format;
            }

            string formattedMessage = string.Format(fullFormat, args);

            player.Message(formattedMessage);
        }

        protected override void LoadDefaultMessages()
        {
            foreach (KeyValuePair<string, Dictionary<string, string>> pair in Messages.DefaultMessages)
            {
                lang.RegisterMessages(pair.Value, this, pair.Key);
            }
        }

        #endregion

        #region Configuration handling

        protected override void LoadDefaultConfig()
        {
            LogWarning("Loading default configuration");
            _configuration = Configuration.GetDefault(this);
            SaveConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_configuration);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                Configuration.Migrate(this, Config);
                _configuration = Config.ReadObject<Configuration>();
                if (_configuration == null)
                {
                    LogError("Configuration is null");
                    LoadDefaultConfig();
                    return;
                }

                bool shouldSave = false;

                if (_configuration.NearbyRange < 0)
                {
                    LogError(
                        "Configuration property 'Disallow spawning horse this close to another' cannot be less than zero!"
                    );
                    _configuration.NearbyRange = 5f;
                    shouldSave = true;
                }

                if (_configuration.CooldownGroups == null)
                {
                    LogError("Configuration property 'Cooldown groups' cannot be null");
                    _configuration.CooldownGroups = new Dictionary<string, int>();
                    shouldSave = true;
                }

                foreach (KeyValuePair<string, int> pair in _configuration.CooldownGroups.ToArray())
                {
                    if (pair.Value < 0)
                    {
                        LogError("Configuration property 'Cooldown groups' should not contain values less than zero!");
                        _configuration.CooldownGroups[pair.Key] = 0;
                        shouldSave = true;
                    }
                }

                if (_configuration.Commands == null)
                {
                    LogError("Configuration property 'Commands' cannot be null");
                    _configuration.Commands = Array.Empty<string>();
                    shouldSave = true;
                }

                if (_configuration.Commands.Length == 0)
                {
                    LogWarning(
                        "Configuration does not specify any commands, plugin will be unavailable for players to use"
                    );
                }

                if (_configuration.DefaultCooldown < 0)
                {
                    LogWarning("Configuration property 'Default cooldown' cannot be less than zero!");
                    _configuration.DefaultCooldown = 0;
                    shouldSave = true;
                }

                if (shouldSave)
                {
                    SaveConfig();
                    LogWarning("Configuration saved with corrected values");
                }
            }
            catch (Exception e)
            {
                LogError("Could not load configuration: {0}", e.ToString());
                LoadDefaultConfig();
            }
        }

        #endregion

        #region Persistence handling

        private void LoadData()
        {
            DynamicConfigFile file = Interface.Oxide.DataFileSystem.GetFile("spawn_data");
            _spawnData = file.ReadObject<List<HorseSpawnData>>();
            if (_spawnData == null)
            {
                _spawnData = new List<HorseSpawnData>();
            }
        }

        private void SaveData()
        {
            DynamicConfigFile file = Interface.Oxide.DataFileSystem.GetFile("spawn_data");
            file.WriteObject(_spawnData);
        }

        #endregion

        #region Lang API messages class

        class Messages
        {
            public const string PREFIX                       = "Chat prefix";
            public const string NO_PERMISSION                = "No permission";
            public const string COOLDOWN                     = "Cooldown in progress";
            public const string HORSE_NEARBY                 = "Horse nearby";
            public const string ESCAPE_BLOCKED               = "Spawning blocked by NoEscape";
            public const string NO_SPAWN_POSITION            = "Could not find spawn position";
            public const string CANNOT_SPAWN_INSIDE_BUILDING = "Cannot spawn inside building";
            public const string HORSE_INFO                   = "Horse info";
            public const string NO_HORSES                    = "No horses";

            public static readonly Dictionary<string, Dictionary<string, string>> DefaultMessages =
                new Dictionary<string, Dictionary<string, string>> {
                    {
                        "en", new Dictionary<string, string> {
                            {PREFIX, "[WHERE IS MY HORSE]"},
                            {NO_PERMISSION, "You are not allowed to use this command"}
                        }
                    }
                };
        }

        #endregion

        #region Configuration class

        class Configuration
        {
            private static readonly ConfigurationMigration[] Migrations = {
                new ConfigurationMigration(
                    new VersionNumber(0, 0, 0),
                    new VersionNumber(2, 0, 0),
                    config =>
                    {
                        config["Configuration version (Do not modify)"] = new VersionNumber(2, 0, 0);
                        config["Cooldown groups"] = config["Cooldowns"];
                        config.Remove("Cooldowns");
                        config["Allow spawning horses in buildings"] = config["Allow usage inside building"];
                        config.Remove("Allow usage inside building");
                        config["Prevent examining horses for non-owners"] = config["Prevent looting for non-owner"];
                        config.Remove("Prevent looting for non-owner");
                        config["Check if player is raid-blocked by NoEscape"] = config["Use NoEscape"];
                        config.Remove("Use NoEscape");
                        config["Disallow spawning horse this close to another"] = 5f;
                        config["Commands"] = new[] {"horse"};
                        config["Default cooldown"] = 300;
                        config["Max count of owned horses"] = 1;
                    }
                )
            };

            [JsonProperty("Configuration version (Do not modify)")]
            public VersionNumber Version { get; set; }

            [JsonProperty("Cooldown groups")]
            public Dictionary<string, int> CooldownGroups { get; set; }

            [JsonProperty("Default cooldown")]
            public int DefaultCooldown { get; set; }

            [JsonProperty("Allow spawning horses in buildings")]
            public bool AllowInside { get; set; }

            [JsonProperty("Prevent examining horses for non-owners")]
            public bool PreventNonOwnerLooting { get; set; }

            [JsonProperty("Disallow spawning horse this close to another")]
            public float NearbyRange { get; set; }

            [JsonProperty("Check if player is raid-blocked by NoEscape")]
            public bool UseNoEscape { get; set; }

            [JsonProperty("Commands")]
            public string[] Commands { get; set; }

            [JsonProperty("Max count of owned horses")]
            public int MaxOwnedHorses { get; set; }

            public static Configuration GetDefault(WhereIsMyHorse plugin)
            {
                return new Configuration {
                    Version = plugin.Version,
                    CooldownGroups = new Dictionary<string, int> {
                        {"nocooldown", 0},
                        {"vip", 60}
                    },
                    AllowInside = false,
                    PreventNonOwnerLooting = false,
                    NearbyRange = 5f,
                    UseNoEscape = true,
                    Commands = new[] {"horse"},
                    DefaultCooldown = 300,
                    MaxOwnedHorses = 1
                };
            }

            /// <summary>
            /// Migrates configuration file, if it's incompatible with current plugin version.
            /// Only upgrade migrations are supported (for example, from version 1.0.0 to version 2.0.0)
            /// </summary>
            /// <param name="currentVersion"></param>
            /// <param name="configFile"></param>
            public static void Migrate(WhereIsMyHorse plugin, DynamicConfigFile configFile)
            {
                Dictionary<string, int> configVersionObj =
                    configFile.Get<Dictionary<string, int>>("Configuration version (Do not modify)");
                VersionNumber configVersion;
                if (configVersionObj == null)
                {
                    configVersion = default(VersionNumber);
                }
                else
                {
                    configVersion = new VersionNumber(
                        configVersionObj["Major"],
                        configVersionObj["Minor"],
                        configVersionObj["Patch"]
                    );
                }

                if (configVersion == plugin.Version)
                {
                    return;
                }

                foreach (ConfigurationMigration currentMigration in Migrations)
                {
                    if (currentMigration.From != configVersion)
                    {
                        continue;
                    }

                    plugin.LogWarning(
                        "Migrating configuration file from v{0} to v{1}",
                        configVersion,
                        currentMigration.To
                    );
                    currentMigration.Migrate(configFile);
                    configVersion = currentMigration.To;
                }

                configFile.Save();

                plugin.LogWarning("Configuration migrated!");
            }

            private class ConfigurationMigration
            {
                public VersionNumber From { get; }
                public VersionNumber To { get; }
                public Action<DynamicConfigFile> Migrate { get; }

                public ConfigurationMigration(VersionNumber from, VersionNumber to, Action<DynamicConfigFile> migrate)
                {
                    From = from;
                    To = to;
                    Migrate = migrate;
                }
            }
        }

        #endregion

        #region Persistence data class

        private class HorseSpawnData
        {
            public string OwnerId { get; set; }
            public NetworkableId NetId { get; set; }
            public float SpawnRealtimeSinceStartup { get; set; }

            public bool IsNetworkableAlive
            {
                get
                {
                    BaseNetworkable _;
                    return BaseNetworkable.serverEntities.entityList.TryGetValue(NetId, out _);
                }
            }

            public static HorseSpawnData FromRidableHorse(RidableHorse horse)
            {
                return new HorseSpawnData {
                    OwnerId = horse.OwnerID.ToString(),
                    NetId = horse.net.ID,
                    SpawnRealtimeSinceStartup = horse.spawnTime
                };
            }

            public RidableHorse GetEntity()
            {
                BaseNetworkable networkable;
                if (!BaseNetworkable.serverEntities.entityList.TryGetValue(NetId, out networkable))
                {
                    return null;
                }

                return (RidableHorse)networkable;
            }

            public IReadOnlyDictionary<string, object> ToDictionary()
            {
                return new Dictionary<string, object> {
                    {"OwnerId", OwnerId},
                    {"NetId", NetId},
                    {"SpawnRealtimeSinceStartup", SpawnRealtimeSinceStartup}
                };
            }
        }

        #endregion
    }
}
