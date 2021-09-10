using System;
using System.Collections.Generic;
using System.Linq;

using Newtonsoft.Json;

using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

using UnityEngine;

using Layer = Rust.Layer;
using Physics = UnityEngine.Physics;
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

        readonly int layerMask = LayerMask.GetMask(
            nameof(Layer.Terrain),
            nameof(Layer.Construction),
            nameof(Layer.World),
            nameof(Layer.Clutter)
        );

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

            AddCovalenceCommand("horse", nameof(SpawnHorse));
        }

        #endregion

        #region Core

        string ConstructPermission(string perm)
        {
            return "whereismyhorse." + perm;
        }

        bool CheckPermission(IPlayer player, string perm, bool allowServer = false)
        {
            if (player.IsServer)
            {
                return allowServer;
            }

            if (!player.HasPermission(perm))
            {
                Message(player, "No permission");
                return false;
            }

            return true;
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

            Message(player, "Cooldown", (int)(cd - timeSince));

            return false;
        }

        void CommandHandler(IPlayer player, string command, string[] args)
        {
            if (args.Length == 0)
            {
                SpawnHorse(player);
            }
            else
            {
                var targetId = args[0];
                var targetPlayer = players.FindPlayer(targetId);

                if (targetPlayer == null || !targetPlayer.IsConnected || targetPlayer.IsSleeping || targetPlayer.Health <= 0)
                {
                    Message(player, "Player not found", targetId);
                }
                else
                {
                    SpawnHorse(player, true);
                }
            }
        }

        void SpawnHorse(IPlayer player, bool bypassChecks = false) // bypass permissions and other stuff
        {
            if (!CheckPermission(player, PERMISSION_USE))
            {
                return;
            }

            if (!CheckCooldown(player))
            {
                return;
            }

            if (settings.UseNoEscape && Interface.CallHook("IsEscapeBlocked", player.Id) != null)
            {
                Message(player, "NoEscape");
                return;
            }

            var basePlayer = (BasePlayer)player.Object;

            if (!settings.AllowInside && !basePlayer.IsOutside())
            {
                Message(player, "Can't spawn indoors");
                return;
            }

            RaycastHit hit;

            if (!Physics.Raycast(basePlayer.eyes.HeadRay(), out hit, 20f, layerMask))
            {
                Message(player, "No point for spawn");
                return;
            }

            var list = Pool.GetList<RidableHorse>();

            Vis.Entities(hit.point, 3f, list);

            var hc = list.Count;

            Pool.FreeList(ref list);

            if (hc > 0)
            {
                Message(player, "Horse nearby");
                return;
            }

            var rot = Quaternion.LookRotation(-basePlayer.eyes.BodyForward(), Vector3.up);

            var horse = GameManager.server.CreateEntity(HORSE_PREFAB, hit.point, rot);

            if (!horse)
            {
                Message(player, "NRE");
            }
            else
            {
                horse.transform.Rotate(Vector3.up, 90f);
                lastUsed[player.Id] = Time.realtimeSinceStartup;
                Message(player, "Spawned");
                horse.Spawn();
            }
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
            { "No permission", "You have no access to that command." },
            { "No point for spawn", "No raycast hit! Look at something." },
            { "Can't spawn indoors", "You can use that command only when outside!" },
            { "Spawned", "Your horse has been spawned, sir! Don't forget to feed it!" },
            { "Cooldown", "You have called you horse recently, wait a bit, please. ({0} seconds left)" },
            { "Prefix", "[WHERE IS MY HORSE]" },
            { "NoEscape", "Can't use command while escape blocked!" },
            { "NRE", "Could not spawn a horse, it's null. Maybe next time?" },
            { "Horse nearby", "There is a horse very close, consider using it instead." },
            { "Player not found", "Player {0} was not found." },
            { "Horse spawned", "Horse was spawned for player {0}" }
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
