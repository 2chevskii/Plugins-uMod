using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

//using Layer = Rust.Layer;
using Pool = Facepunch.Pool;
using Time = UnityEngine.Time;

namespace Oxide.Plugins
{
    [Info("Where is My Horse", "2CHEVSKII", "1.0.0")]
    [Description("Here is your horse, sir!")]
    class WhereIsMyHorse : CovalencePlugin
    {
        #region Fields

        const string PERMISSION_USE    = "whereismyhorse.use";
        const string PERMISSION_USE_ON = "whereismyhorse.useon";
        const int    DEFAULT_COOLDOWN  = 300;
        const string HORSE_PREFAB      = "assets/rust.ai/nextai/testridablehorse.prefab";

        const string M_CHAT_PREFIX        = "Prefix",
                     M_NO_PERMISSION      = "No permission",
                     M_CANT_SPAWN_INDOORS = "Can't spawn indoors",
                     M_SPAWNED            = "Spawned",
                     M_COOLDOWN           = "Cooldown",
                     M_NO_ESCAPE          = "NoEscape",
                     M_NRE                = "NRE",
                     M_HORSE_NEARBY       = "Horse nearby",
                     M_PLAYER_NOT_FOUND   = "Player not found",
                     M_HORSE_SPAWNED      = "Horse spawned (on player)",
                     M_NO_POINT_FOR_SPAWN = "No point for spawn";

        const float /*RAYCAST_DISTANCE   = 20f,*/
                    HORSE_NEARBY_RANGE = 5f;

        //readonly int layerMask = LayerMask.GetMask(
        //    nameof(Layer.Terrain),
        //    nameof(Layer.Construction),
        //    nameof(Layer.World),
        //    nameof(Layer.Clutter)
        //);

        //readonly int layerMaskConstruction = LayerMask.GetMask(nameof(Layer.Construction));

        readonly Dictionary<string, float> lastUsed = new Dictionary<string, float>();

        PluginSettings settings;

        #endregion

        #region Oxide hooks

        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            permission.RegisterPermission(PERMISSION_USE_ON, this);

            foreach (var perm in settings.CooldownGroups.Keys)
            {
                permission.RegisterPermission(ConstructPermission(perm), this);
            }

            AddCovalenceCommand("horse", nameof(CommandHandler));
        }

        #endregion

        #region Core

        bool FindSpawnPoint(BasePlayer targetPlayer, out Vector3 spawnPoint, float spawnDistance = 3f) // move horse on top of the construction block if present
        {
            var refPos = targetPlayer.ServerPosition;
            var refRot = targetPlayer.eyes.rotation;

            var mod = 1;
            for (var i = 0; i <= 90; i++)
            {
                var rotation = refRot * Quaternion.Inverse(Quaternion.AngleAxis(i * mod, Vector3.up));
                var direction = rotation * Vector3.forward * spawnDistance;
                var pos = refPos + direction;

                float height = TerrainMeta.HeightMap.GetHeight(pos);

                var list = Pool.GetList<BuildingBlock>();
                Vis.Entities(pos, 1f, list);

                if (list.Count != 0)
                {
                    var bb = list[0];
                    height = bb.transform.position.y;
                }

                Pool.FreeList<BuildingBlock>(ref list);

                if (height > 0 && Math.Abs(height - refPos.y) <= 5f)
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

        string ConstructPermission(string perm)
        {
            return "whereismyhorse." + perm;
        }

        bool CheckPermission(IPlayer player, string perm, bool allowServer = false)
        {
            if (player.IsServer && allowServer || !player.IsServer && player.HasPermission(perm))
            {
                return true;
            }
            Message(player, M_NO_PERMISSION);
            return false;
        }

        int GetSmallestCooldown(IPlayer player)
        {
            var perms = settings.CooldownGroups.Keys.Where(p => player.HasPermission(ConstructPermission(p))).ToArray();

            if (perms.Length == 0)
            {
                return DEFAULT_COOLDOWN;
            }

            var min = perms.Select(p => settings.CooldownGroups[p]).Min();

            return min;
        }

        bool CheckCooldown(IPlayer player)
        {
            if (!lastUsed.ContainsKey(player.Id))
            {
                return true;
            }

            var cd = GetSmallestCooldown(player);
            var timeSince = Time.realtimeSinceStartup - lastUsed[player.Id];

            if (timeSince > cd)
            {
                return true;
            }

            Message(player, M_COOLDOWN, (int)(cd - timeSince));

            return false;
        }

        bool CheckNoEscape(IPlayer player)
        {
            if (!settings.UseNoEscape)
            {
                return true;
            }

            var callResult = Interface.CallHook("IsEscapeBlocked", player.Id);
            if (callResult == null || callResult is bool && (bool)callResult == false)
            {
                return true;
            }

            Message(player, M_NO_ESCAPE);
            return false;
        }

        bool CheckOutside(BasePlayer player)
        {
            if (settings.AllowInside || player.IsOutside())
            {
                return true;
            }

            Message(player.IPlayer, M_CANT_SPAWN_INDOORS);
            return false;
        }

        void CommandHandler(IPlayer player, string _, string[] args)
        {
            if (args.Length == 0)
            {
                if (!CheckPermission(player, PERMISSION_USE))
                {
                    return;
                }

                if (!CheckCooldown(player))
                {
                    return;
                }

                if (!CheckNoEscape(player))
                {
                    return;
                }

                var basePlayer = (BasePlayer)player.Object;

                if (!CheckOutside(basePlayer))
                {
                    return;
                }

                if (IsHorseNearby(basePlayer.transform.position))
                {
                    Message(player, M_HORSE_NEARBY);
                    return;
                }

                Vector3 position;
                if (!FindSpawnPoint(basePlayer, out position))
                {
                    Message(player, M_NO_POINT_FOR_SPAWN);
                    return;
                }

                var rotation = GetHorseRotation(basePlayer.eyes.rotation);

                //basePlayer.SendConsoleCommand("ddraw.text",);
                SpawnHorse(position, rotation);
                lastUsed[player.Id] = Time.realtimeSinceStartup;
                Message(player, M_SPAWNED);
            }
            else
            {
                if (!CheckPermission(player, PERMISSION_USE_ON, true))
                {
                    return;
                }

                var targetId = args[0];
                var targetPlayer = players.FindPlayer(targetId);

                if (targetPlayer == null || !targetPlayer.IsConnected || targetPlayer.IsSleeping || targetPlayer.Health <= 0)
                {
                    Message(player, M_PLAYER_NOT_FOUND, targetId);
                }
                else
                {
                    Vector3 position;
                    var basePlayer = (BasePlayer)targetPlayer.Object;

                    if (!FindSpawnPoint(basePlayer, out position))
                    {
                        Message(player, M_NO_POINT_FOR_SPAWN);
                        return;
                    }

                    var rotation = GetHorseRotation(basePlayer.eyes.rotation);

                    SpawnHorse(position, rotation);
                    Message(player, M_HORSE_SPAWNED, targetPlayer.Name);
                    Message(targetPlayer, M_SPAWNED);
                }
            }
        }

        bool IsHorseNearby(Vector3 position)
        {
            var list = Pool.GetList<RidableHorse>();
            var b = false;

            Vis.Entities(position, HORSE_NEARBY_RANGE, list);

            if (list.Count > 0)
            {
                b = true;
            }

            Pool.FreeList(ref list);
            return b;
        }

        Quaternion GetHorseRotation(Quaternion playerRotation)
        {
            return playerRotation * Quaternion.Inverse(Quaternion.AngleAxis(90, Vector3.up));
        }

        void SpawnHorse(Vector3 position, Quaternion rotation)
        {
            GameManager.server.CreateEntity(HORSE_PREFAB, position, rotation).Spawn();

        }

        #endregion

        #region Configuration

        protected override void LoadDefaultConfig()
        {
            Log("Loading default configuration...");
            settings = PluginSettings.Default;
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();

            try
            {
                settings = Config.ReadObject<PluginSettings>();

                if (settings == null || settings.CooldownGroups == null)
                {
                    throw new Exception("Configuration load error");
                }
            }
            catch (Exception e)
            {
                LogError(e.Message);
                LoadDefaultConfig();
            }
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(settings);
        }

        #endregion

        #region LangAPI

        void Message(IPlayer player, string langKey, params object[] args)
        {
            player.Message(lang.GetMessage(langKey, this, player.Id), lang.GetMessage("Prefix", this, player.Id), args);
        }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(new Dictionary<string, string> {
            { M_NO_PERMISSION, "You have no access to that command." },
            { M_NO_POINT_FOR_SPAWN, "Cannot spawn horse at the current position." },
            { M_CANT_SPAWN_INDOORS, "You can use that command only when outside!" },
            { M_SPAWNED, "Your horse has been spawned, sir! Don't forget to feed it!" },
            { M_COOLDOWN, "You have called you horse recently, wait a bit, please. ({0} seconds left)" },
            { M_CHAT_PREFIX, "[WHERE IS MY HORSE]" },
            { M_NO_ESCAPE, "Can't use command while escape blocked!" },
            { M_NRE, "Could not spawn a horse, it's null. Maybe next time?" },
            { M_HORSE_NEARBY, "There is a horse very close, consider using it instead." },
            { M_PLAYER_NOT_FOUND, "Player {0} was not found." },
            { M_HORSE_SPAWNED, "Horse was spawned for player {0}" }
        }, this, "en");

        #endregion

        #region Nested types

        class PluginSettings
        {
            public static PluginSettings Default =>
                new PluginSettings {
                    CooldownGroups = new Dictionary<string, int> {
                        ["nocooldown"] = 0,
                        ["vip"] = 30
                    },
                    AllowInside = false,
                    UseNoEscape = true
                };

            [JsonProperty("Cooldowns")]
            public Dictionary<string, int> CooldownGroups { get; set; }
            [JsonProperty("Allow usage inside building")]
            public bool AllowInside { get; set; }
            [JsonProperty("Use NoEscape")]
            public bool UseNoEscape { get; set; }
        }

        #endregion
    }
}
