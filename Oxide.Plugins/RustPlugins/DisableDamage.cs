using System.Collections.Generic;
using Oxide.Core;

/* DisableDamage -> Public Rust Plugin : https://rustworkshop.space/resources/disable-damage.17/
 * Author -> 2CHEVSKII :
 * : https://umod.org/user/2CHEVSKII 
 * : https://github.com/2chevskii/ 
 * : https://rustworkshop.space/members/2chevskii.8/ 
 * : https://www.youtube.com/channel/UCgq5jjofrmIXCagJXqrMG9w 
 * Changelog:
 * [0.1.0] - Initial release
 * [0.2.0] - Added option for barrels
 *         - Added option for transport (Copters and boats)
 *         - Added option for helicopter
 * */

namespace Oxide.Plugins
{
    [Info("Disable Damage", "2CHEVSKII", "0.2.0")]
    [Description("Players with permission can disable damage for other players")]
    internal class DisableDamage : RustPlugin
    {

        #region [Fields]


        //////////////////////////////////////////
        //// Config variables                 ////
        //////////////////////////////////////////
        private bool displayerdmg = true;
        private bool disNPCdmg = true;
        private bool disStructdmg = true;
        private bool disAnimaldmg = true;
        private bool disBarreldmg = true;
        private bool disHelidmg = true;
        private bool disTransportdmg = true;
        private bool announceTarget = true;
        private bool announceName = true;
        private bool savedisabled = true;

        /// <summary>
        /// Players with disabled damage
        /// </summary>
        private List<ulong> disdmg { get; set; }

        /// <summary>
        /// Permission to use the chat command
        /// </summary>
        private const string perm = "disabledamage.use";

        //////////////////////////////////////////
        //// Strings                          ////
        //////////////////////////////////////////
        private const string mprefix = "Prefix";
        private const string myourdenabled = "Self damage enabled";
        private const string myourddisabled = "Self damage disabled";
        private const string mplayerdenabled = "Player's damage enabled";
        private const string mplayerddisabled = "Player's damage disabled";
        private const string menby = "Player enabled your damage";
        private const string mdisby = "Player disabled your damage";
        private const string mnoperm = "No permission";
        private const string mnoplayer = "No player found";
        private const string mhelp = "Help message";


        #endregion

        #region [Configuration]


        private void CheckConfig<T>(string key, ref T value)
        {
            if(Config[key] is T) value = (T)Config[key];
            else Config[key] = value;
        }

        protected override void LoadDefaultConfig() { }

        private void LoadConfiguration()
        {
            CheckConfig("Save disabled players to data", ref savedisabled);
            CheckConfig("Announce target player", ref announceTarget);
            CheckConfig("Display name of user to target player", ref announceName);
            CheckConfig("Disable damage to players", ref displayerdmg);
            CheckConfig("Disable damage to NPCs", ref disNPCdmg);
            CheckConfig("Disable damage to Buildings and structures", ref disStructdmg);
            CheckConfig("Disable damage to animals", ref disAnimaldmg);
            CheckConfig("Disable damage to barrels", ref disBarreldmg);
            CheckConfig("Disable damage to transport", ref disTransportdmg);
            CheckConfig("Disable damage to helicopter", ref disHelidmg);
            SaveConfig();
        }


        #endregion
        
        #region [Localization]


        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");

        private void Replier(BasePlayer player, string message, params string[] args) => player.ChatMessage($"{lang.GetMessage(mprefix, this)} {string.Format(lang.GetMessage(message, this), args)}");

        Dictionary<string, string> defmessages = new Dictionary<string, string>
        {
            [mprefix] = "<color=red>[</color>DISABLE DAMAGE<color=red>]</color>",
            [mnoperm] = "<color=red>Y</color>ou have no permission to use this command<color=red>!</color>",
            [myourdenabled] = "Your damage has been <color=#36d859>enabled</color>.",
            [myourddisabled] = "Your damage has been <color=red>disabled</color>.",
            [mplayerdenabled] = "You <color=#36d859>enabled</color> damage for player <color=#36a1d8>{0}</color>.",
            [mplayerddisabled] = "You <color=red>disabled</color> damage for player <color=#36a1d8>{0}</color>.",
            [menby] = "Your damage has been <color=#36d859>enabled</color> by <color=#36a1d8>{0}.",
            [mdisby] = "Your damage has been <color=red>disabled</color> by <color=#36a1d8>{0}.",
            [mnoplayer] = "<color=red>N</color>o player found with that name<color=red>!</color>",
            [mhelp] = "<color=yellow>Wrong command usage!</color>\n<color=#36a1d8>/dd</color> - enable/disable your damage\n<color=#36a1d8>/dd <username or userid></color> - disable damage for specific user."
        };

        
        #endregion
        
        #region [Commands]


        [ChatCommand("dd")]
        private void CmdDD(BasePlayer player, string command, string[] args)
        {
            if(permission.UserHasPermission(player.UserIDString, perm))
            {
                switch(args.Length)
                {
                    case 0:
                        if(!disdmg.Contains(player.userID))
                        {
                            disdmg.Add(player.userID);
                            Replier(player, myourddisabled);
                        }
                        else
                        {
                            disdmg.Remove(player.userID);
                            Replier(player, myourdenabled);
                        }
                        break;
                    case 1:
                        var findplayer = BasePlayer.Find(args[0].ToLower());
                        if(findplayer != null)
                        {
                            if(!disdmg.Contains(player.userID))
                            {
                                disdmg.Add(findplayer.userID);
                                Replier(player, mplayerddisabled, findplayer.displayName);
                                if(announceTarget)
                                {
                                    if(announceName) Replier(findplayer, mdisby, player.displayName);
                                    else Replier(findplayer, myourddisabled);
                                }
                            }
                            else
                            {
                                disdmg.Remove(findplayer.userID);
                                Replier(player, mplayerdenabled, findplayer.displayName);
                                if(announceTarget)
                                {
                                    if(announceName) Replier(findplayer, menby, player.displayName);
                                    else Replier(findplayer, myourdenabled);
                                }
                            }
                        }
                        else Replier(player, mnoplayer);
                        break;
                    default:
                        Replier(player, mhelp);
                        break;
                }
            }
            else Replier(player, mnoperm);
        }


        #endregion

        #region [Hooks]


        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if(info != null && info.InitiatorPlayer != null && entity != null)
            {
                if(BasePlayer.activePlayerList.Contains(info.InitiatorPlayer))
                {
                    if(info.InitiatorPlayer.userID != 0 && info.InitiatorPlayer.userID.IsSteamId())
                    {
                        var id = info.InitiatorPlayer.userID;
                        if(disdmg.Contains(id))
                        {
                            if((entity is BasePlayer && displayerdmg) 
                                || (entity is BaseNpc && disNPCdmg) 
                                || (entity is BaseAnimalNPC && disAnimaldmg) 
                                || (entity is BuildingBlock && disStructdmg) 
                                || ((entity.GetComponent<Deployable>() != null) && disStructdmg) 
                                || (entity.PrefabName.Contains("barrel") && disBarreldmg)
                                || (entity is BaseHelicopter)
                                || (entity is MiniCopter || entity is MotorBoat) && disTransportdmg)
                            {
                                info?.damageTypes?.ScaleAll(0);
                            }
                        }
                    }
                }
            }
        }

        private void Init()
        {
            permission.RegisterPermission(perm, this);
            LoadConfiguration();
            if(Interface.Oxide.DataFileSystem.ExistsDatafile("DisableDamage"))
            {
                try
                {
                    disdmg = Interface.Oxide.DataFileSystem.GetFile("DisableDamage").ReadObject<List<ulong>>();
                }
                catch
                {
                    disdmg = new List<ulong>();
                }
            }
            else disdmg = new List<ulong>();
        }

        private void Unload() { if(savedisabled) Interface.Oxide.DataFileSystem.GetFile("DisableDamage").WriteObject(disdmg); }


        #endregion

    }
}
