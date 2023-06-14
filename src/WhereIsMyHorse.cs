using System;
using System.Collections.Generic;
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
        private const string               PERMISSION_USE           = "whereismyhorse.use";
        private const string               PERMISSION_USE_ON_PLAYER = "whereismyhorse.useonplayer";
        private const string               HORSE_PREFAB             = "assets/rust.ai/nextai/testridablehorse.prefab";
        private       Configuration        _configuration;
        private       List<HorseSpawnData> _spawnData;

        #region Oxide hooks

        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_USE_ON_PLAYER, this);

            foreach (string command in _configuration.Commands)
            {
                covalence.RegisterCommand(command, this, HandleCommand);
            }
        }

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
            else if (args[0] == "list")
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

            BasePlayer basePlayer = (BasePlayer)player.Object;
            for (int i = _spawnData.Count - 1; i >= 0; i--)
            {
                HorseSpawnData data = _spawnData[i];
                if (!data.IsAlive)
                {
                    _spawnData.RemoveAt(i);
                    continue;
                }

                if (data.OwnerId == player.Id)
                {
                    RidableHorse ent = data.GetEntity();
                    string name = ent._name;
                    string breed = ent.breeds[ent.currentBreed].breedName.english;
                    float distance = Vector3.Distance(basePlayer.ServerPosition, ent.ServerPosition);

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

            Log("Player {0} min cooldown: {1}s", player.Name, playerCooldown);
            int cooldownSecondsLeft;
            if (IsPlayerOnCooldown(player, playerCooldown, out cooldownSecondsLeft))
            {
                MessagePlayer(player, Messages.COOLDOWN, cooldownSecondsLeft);
                return;
            }

            if (!CanPlayerSpawnAnotherHorse(player))
            {
                MessagePlayer(player, Messages.MAX_HORSES_COUNT, _configuration.MaxOwnedHorses);
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

            HorseSpawnData spawnData = HorseSpawnData.FromRidableHorse(poorAnimal);

            _spawnData.Add(spawnData);

            Interface.CallHook("OnWmHorseSpawned", spawnData.ToDictionary());
        }

        bool CanPlayerSpawnAnotherHorse(IPlayer player)
        {
            return _configuration.MaxOwnedHorses > GetAliveHorsesCount(player);
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
            /*const float SPAWN_DISTANCE_MIN = 1f;
            const float SPAWN_DISTANCE_MAX = 4f;
            const float SPAWN_DISTANCE_STEP = .2f;*/


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
            if (result != null && result is bool && (bool)result)
            {
                return true;
            }

            return false;
        }

        bool IsMaxCountReached(IPlayer player) => GetPlayerHorseCount(player) <
                                                  GetPlayerGroupsConfig(player, _configuration.OwnedHorsesCount, false);

        bool IsOnCooldown(IPlayer player) => GetSecondsSinceLastRecallOrSpawn(player) >=
                                             GetPlayerGroupsConfig(player, _configuration.Cooldown, true);

        int GetSecondsSinceLastRecallOrSpawn(IPlayer player) { }

        int GetPlayerHorseCount(IPlayer player) { }

        T GetPlayerGroupsConfig<T>(IPlayer player, Configuration.GroupsConfiguration<T> configuration, bool min)
            where T : IComparable<T>
        {
            T selectedValue = default(T);
            bool isSelected = false;

            foreach (KeyValuePair<string, T> pair in configuration.Groups)
            {
                string groupName = pair.Key;

                if (!player.BelongsToGroup(groupName))
                {
                    continue;
                }

                T value = pair.Value;

                if (!isSelected &&
                    ((min && value.CompareTo(configuration.Default) == -1) ||
                     (!min && value.CompareTo(configuration.Default) == 1)))
                {
                    selectedValue = value;
                    isSelected = true;
                    continue;
                }

                if ((min && value.CompareTo(selectedValue) == -1) || (!min && value.CompareTo(selectedValue) == 1))
                {
                    selectedValue = value;
                    isSelected = true;
                }
            }

            if (!isSelected)
                return configuration.Default;

            return selectedValue;
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
            _configuration = Configuration.GetDefault();
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
                _configuration = Config.ReadObject<Configuration>();
                if (_configuration == null)
                {
                    LogError("Configuration is null");
                    LoadDefaultConfig();
                    return;
                }

                /*bool shouldSave = false;

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
                }*/
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
            public const string MAX_HORSES_COUNT             = "Maximum count of horses spawned";

            public static readonly Dictionary<string, Dictionary<string, string>> DefaultMessages =
                new Dictionary<string, Dictionary<string, string>> {
                    {
                        "en", new Dictionary<string, string> {
                            {PREFIX, "[WHERE IS MY HORSE]"},
                            {NO_PERMISSION, "You are not allowed to use this command"},
                            {COOLDOWN, "You've recently called your horse, wait a little ({0} seconds left)"},
                            {HORSE_NEARBY, "There is a horse nearby already"},
                            {ESCAPE_BLOCKED, "You have an escape block in progress"},
                            {NO_SPAWN_POSITION, "Could not find a spawn position for your horse"},
                            {CANNOT_SPAWN_INSIDE_BUILDING, "Cannot call a horse inside building"},
                            {HORSE_INFO, "{0} - {1}, {2:0.0}m away from you"},
                            {NO_HORSES, "You have no horses"},
                            {MAX_HORSES_COUNT, "You've maximum count of horses ({0})"}
                        }
                    }
                };
        }

        #endregion

        #region Configuration class

        class Configuration
        {
            [JsonProperty("Cooldown")]
            public GroupsConfiguration<int> Cooldown { get; set; }

            [JsonProperty("Owned horses count")]
            public GroupsConfiguration<int> OwnedHorsesCount { get; set; }

            [JsonProperty("Allow spawning horses in buildings")]
            public bool AllowInside { get; set; }

            [JsonProperty("Prevent examining horses for non-owners")]
            public bool PreventNonOwnerLooting { get; set; }

            [JsonProperty("Disallow spawning horse this close to another")]
            public float NearbyRange { get; set; }

            [JsonProperty("NoEscape integration")]
            public NoEscapeIntegrationConfiguration NoEscapeIntegration { get; set; }

            [JsonProperty("Return settings")]
            public HorseReturnConfiguration ReturnSettings { get; set; }

            [JsonProperty("Commands")]
            public string[] Commands { get; set; }

            [JsonProperty("Decrease count of owned horses upon horse death")]
            public bool DecreaseOwnedCountOnHorseDeath { get; set; }

            [JsonProperty("Count horses given by other players")]
            public bool CountGivenHorses { get; set; }

            public static Configuration GetDefault() => new Configuration {
                CooldownGroups = new Dictionary<string, int> {
                    {"nocooldown", 0},
                    {"vip", 60}
                },
                AllowInside = false,
                PreventNonOwnerLooting = false,
                NearbyRange = 5f,
                NoEscapeIntegration = new NoEscapeIntegrationConfiguration {
                    CheckOnSpawn = true,
                    CheckOnRemove = true
                },
                ReturnSettings = new HorseReturnConfiguration {
                    Enabled = false,
                    HealHorse = false,
                    ResetCooldown = false
                },
                Commands = new[] {"horse"},
                DefaultCooldown = 300,
                MaxOwnedHorses = 1
            };

            public class NoEscapeIntegrationConfiguration
            {
                [JsonProperty("Check on spawn")]
                public bool CheckOnSpawn { get; set; }

                [JsonProperty("Check on return")]
                public bool CheckOnRemove { get; set; }
            }

            public class HorseReturnConfiguration
            {
                public bool Enabled { get; set; }

                [JsonProperty("Heal horse on return")]
                public bool HealHorse { get; set; }

                [JsonProperty("Reset cooldown")]
                public bool ResetCooldown { get; set; }
            }

            public class GroupsConfiguration<T>
            {
                public T Default { get; set; }
                public Dictionary<string, T> Groups { get; set; }
            }
        }

        #endregion

        #region Persistence data class

        private class HorseSpawnData
        {
            public string OwnerId { get; set; }
            public NetworkableId NetId { get; set; }
            public float SpawnRealtimeSinceStartup { get; set; }
            public DateTimeOffset CreatedAt { get; set; }
            public DateTimeOffset RecalledAt { get; set; }
            public bool IsReturned { get; set; }
            public DateTimeOffset ReturnedAt { get; set; }


            public bool IsAlive
            {
                get
                {
                    BaseNetworkable networkable;
                    return BaseNetworkable.serverEntities.entityList.TryGetValue(NetId, out networkable) &&
                           ((networkable as RidableHorse)?.IsAlive() ?? false);
                }
            }

            public RidableHorse GetEntity()
            {
                BaseNetworkable networkable;
                if (!BaseNetworkable.serverEntities.entityList.TryGetValue(NetId, out networkable))
                {
                    return null;
                }

                if (!((networkable as RidableHorse)?.IsAlive() ?? false))
                {
                    return null;
                }

                return (RidableHorse)networkable;
            }

            public IReadOnlyDictionary<string, object> Serialize()
            {
                return new Dictionary<string, object> {
                    {"OwnerId", OwnerId},
                    {"NetId", NetId},
                    {"CreatedAt", CreatedAt},
                    {"RecalledAt", RecalledAt},
                    {"IsReturned", IsReturned},
                    {"ReturnedAt", ReturnedAt}
                };
            }
        }

        #endregion
    }
}
