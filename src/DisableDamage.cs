using System.Collections.Generic;
using Oxide.Core;

namespace Oxide.Plugins
{
    [Info("Disable Damage", "2CHEVSKII", "1.0.0")]
    [Description("Allows players with permission to disable other player's damage.")]
    public class DisableDamage : CovalencePlugin
    {

        #region [Fields]


        //////////////////////////////////////////
        //// Config variables                 ////
        //////////////////////////////////////////
        bool displayerdmg = true;
        bool disNPCdmg = true;
        bool disStructdmg = true;
        bool disAnimaldmg = true;
        bool disBarreldmg = true;
        bool disHelidmg = true;
        bool disTransportdmg = true;
        bool announceTarget = true;
        bool announceName = true;
        bool savedisabled = true;

        bool disDefaultdmg = false;

        /// <summary>
        /// Players with disabled damage
        /// </summary>
        List<ulong> DisabledDamage { get; set; }

        /// <summary>
        /// Permission to use the chat command
        /// </summary>
        const string PERMISSION_USE = "disabledamage.use";

        //////////////////////////////////////////
        //// Strings                          ////
        //////////////////////////////////////////
        const string M_PREFIX = "Prefix";
        const string M_SELF_DAMAGE_ENABLED = "Self damage enabled";
        const string M_SELF_DAMAGE_DISABLED = "Self damage disabled";
        const string M_PLAYER_DAMAGE_ENABLED = "Player's damage enabled";
        const string M_PLAYER_DAMAGE_DISABLED = "Player's damage disabled";
        const string M_ENABLED_BY = "Player enabled your damage";
        const string M_DISABLED_BY = "Player disabled your damage";
        const string M_NO_PERMISSION = "No permission";
        const string M_PLAYER_NOT_FOUND = "No player found";
        const string M_HELP = "Help message";
        const string M_UNAVAILABLE = "Command unavailable";


        #endregion

        #region [Configuration]


        void CheckConfig<T>(string menu, string key, ref T value)
        {
            if (Config[menu, key] is T)
                value = (T)Config[menu, key];
            else
                Config[menu, key] = value;
        }

        protected override void LoadDefaultConfig() { }

        void LoadConfiguration()
        {
            CheckConfig("General", "Save disabled players to data", ref savedisabled);
            CheckConfig("General", "Announce target player", ref announceTarget);
            CheckConfig("General", "Display name of user to target player", ref announceName);
            CheckConfig("General", "Damage disabled by default", ref disDefaultdmg);
            CheckConfig("Types", "Disable damage to players", ref displayerdmg);
            CheckConfig("Types", "Disable damage to NPCs", ref disNPCdmg);
            CheckConfig("Types", "Disable damage to Buildings and structures", ref disStructdmg);
            CheckConfig("Types", "Disable damage to animals", ref disAnimaldmg);
            CheckConfig("Types", "Disable damage to barrels", ref disBarreldmg);
            CheckConfig("Types", "Disable damage to transport", ref disTransportdmg);
            CheckConfig("Types", "Disable damage to helicopter", ref disHelidmg);
            SaveConfig();
        }


        #endregion

        #region [Localization]


        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");

        void Replier(BasePlayer player, string message, params string[] args) => player.ChatMessage($"{lang.GetMessage(M_PREFIX, this)} {string.Format(lang.GetMessage(message, this), args)}");

        Dictionary<string, string> defmessages = new Dictionary<string, string>
        {
            [M_PREFIX] = "<color=red>[</color>DISABLE DAMAGE<color=red>]</color>",
            [M_NO_PERMISSION] = "<color=red>Y</color>ou have no permission to use this command<color=red>!</color>",
            [M_SELF_DAMAGE_ENABLED] = "Your damage has been <color=#36d859>enabled</color>.",
            [M_SELF_DAMAGE_DISABLED] = "Your damage has been <color=red>disabled</color>.",
            [M_PLAYER_DAMAGE_ENABLED] = "You <color=#36d859>enabled</color> damage for player <color=#36a1d8>{0}</color>.",
            [M_PLAYER_DAMAGE_DISABLED] = "You <color=red>disabled</color> damage for player <color=#36a1d8>{0}</color>.",
            [M_ENABLED_BY] = "Your damage has been <color=#36d859>enabled</color> by <color=#36a1d8>{0}</color>.",
            [M_DISABLED_BY] = "Your damage has been <color=red>disabled</color> by <color=#36a1d8>{0}</color>.",
            [M_PLAYER_NOT_FOUND] = "<color=red>N</color>o player found with that name<color=red>!</color>",
            [M_HELP] = "<color=yellow>Wrong command usage!</color>\n<color=#36a1d8>/dd</color> - enable/disable your damage\n<color=#36a1d8>/dd <username or userid></color> - disable damage for specific user.",
            [M_UNAVAILABLE] = "This command is <color=yellow>unavailable</color> while \"<color=#F2BC14>Damage disabled by default</color>\" is \"<color=#195FFF>true</color>\" in the config file!"
        };


        #endregion

        #region [Commands]


        [ChatCommand("dd")]
        void CmdDD(BasePlayer player, string command, string[] args)
        {
            if (!permission.UserHasPermission(player.UserIDString, PERMISSION_USE))
            {
                Replier(player, M_NO_PERMISSION);
            }
            else if (disDefaultdmg)
            {
                Replier(player, M_UNAVAILABLE);
            }
            else
            {
                switch (args.Length)
                {
                    case 0:
                        if (!DisabledDamage.Contains(player.userID))
                        {
                            DisabledDamage.Add(player.userID);
                            Replier(player, M_SELF_DAMAGE_DISABLED);
                        }
                        else
                        {
                            DisabledDamage.Remove(player.userID);
                            Replier(player, M_SELF_DAMAGE_ENABLED);
                        }
                        break;
                    case 1:
                        BasePlayer findplayer = BasePlayer.Find(args[0].ToLower());
                        if (findplayer != null)
                        {
                            if (!DisabledDamage.Contains(player.userID))
                            {
                                DisabledDamage.Add(findplayer.userID);
                                Replier(player, M_PLAYER_DAMAGE_DISABLED, findplayer.displayName);
                                if (announceTarget)
                                {
                                    if (announceName)
                                        Replier(findplayer, M_DISABLED_BY, player.displayName);
                                    else
                                        Replier(findplayer, M_SELF_DAMAGE_DISABLED);
                                }
                            }
                            else
                            {
                                DisabledDamage.Remove(findplayer.userID);
                                Replier(player, M_PLAYER_DAMAGE_ENABLED, findplayer.displayName);
                                if (announceTarget)
                                {
                                    if (announceName)
                                        Replier(findplayer, M_ENABLED_BY, player.displayName);
                                    else
                                        Replier(findplayer, M_SELF_DAMAGE_ENABLED);
                                }
                            }
                        }
                        else
                            Replier(player, M_PLAYER_NOT_FOUND);
                        break;
                    default:
                        Replier(player, M_HELP);
                        break;
                }
            }
        }


        #endregion

        #region [Hooks]


        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info != null && info.InitiatorPlayer != null && entity != null)
            {
                BasePlayer player = info.InitiatorPlayer;
                if ((disDefaultdmg || (player.IsConnected && player.userID.IsSteamId() && DisabledDamage.Contains(player.userID)))
                        && ((entity is BasePlayer && displayerdmg)
                                || (entity is BaseNpc && disNPCdmg)
                                || (entity is BaseAnimalNPC && disAnimaldmg)
                                || (entity is BuildingBlock && disStructdmg)
                                || ((entity.GetComponent<Deployable>() != null) && disStructdmg)
                                || (entity.PrefabName.Contains("barrel") && disBarreldmg)
                                || (entity is BaseHelicopter && disHelidmg)
                                || ((entity is BaseVehicle) && disTransportdmg)))
                {
                    info.damageTypes.ScaleAll(0);
                }
            }
        }

        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            LoadConfiguration();
            if (!disDefaultdmg)
            {
                if (Interface.Oxide.DataFileSystem.ExistsDatafile("DisableDamage"))
                {
                    try
                    {
                        DisabledDamage = Interface.Oxide.DataFileSystem.GetFile("DisableDamage").ReadObject<List<ulong>>();
                    }
                    catch
                    {
                        DisabledDamage = new List<ulong>();
                    }
                }
                else
                    DisabledDamage = new List<ulong>();
            }
        }

        void Unload() { if (!disDefaultdmg && savedisabled) Interface.Oxide.DataFileSystem.GetFile("DisableDamage").WriteObject(DisabledDamage); }


        #endregion

        class PluginSettings
        {
            public bool DisableDamageToPlayers { get; set; }
            public bool DisableDamageToNpc { get; set; }
            public bool DisableDamageToAnimals { get; set; }
            public bool DisableDamageToStructures { get; set; }
            public bool DisableDamageToTransport { get; set; }
            public bool DisableDamageToPatrolHeli { get; set; }
            public bool DisableDamageToBradley { get; set; }

            public bool RestoreOnStartup { get; set; }


        }
    }
}
