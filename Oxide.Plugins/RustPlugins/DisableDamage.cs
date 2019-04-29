using System.Collections.Generic;
using Oxide.Core;

/* TODO:
 * Disable damage to entities (barrels etc)
 * */

namespace Oxide.Plugins
{
    [Info("Disable Damage", "2CHEVSKII", "0.1.0")]
    [Description("Players with permission can disable damage for other players")]
    class DisableDamage : RustPlugin
    {

        #region [Fields]


        //////////////////////////////////////////
        //// Config variables                 ////
        //////////////////////////////////////////
        bool displayerdmg = true;
        bool disNPCdmg = true;
        bool disStructdmg = true;
        bool disAnimaldmg = true;
        bool announceTarget = true;
        bool announceName = true;
        bool savedisabled = true;

        /// <summary>
        /// Players with disabled damage
        /// </summary>
        List<ulong> disdmg;

        /// <summary>
        /// Permission to use the chat command
        /// </summary>
        const string perm = "disabledamage.use";

        //////////////////////////////////////////
        //// Strings                          ////
        //////////////////////////////////////////
        const string mprefix = "Prefix";
        const string myourdenabled = "Self damage enabled";
        const string myourddisabled = "Self damage disabled";
        const string mplayerdenabled = "Player's damage enabled";
        const string mplayerddisabled = "Player's damage disabled";
        const string menby = "Player enabled your damage";
        const string mdisby = "Player disabled your damage";
        const string mnoperm = "No permission";
        const string mnoplayer = "No player found";
        const string mhelp = "Help message";


        #endregion

        #region [Configuration]


        void CheckConfig<T>(string key, ref T value)
        {
            if(Config[key] is T) value = (T)Config[key];
            else Config[key] = value;
        }

        protected override void LoadDefaultConfig() { }

        void LoadConfiguration()
        {
            CheckConfig("Save disabled players to data", ref savedisabled);
            CheckConfig("Announce target player", ref announceTarget);
            CheckConfig("Display name of user to target player", ref announceName);
            CheckConfig("Disable damage to players", ref displayerdmg);
            CheckConfig("Disable damage to NPCs", ref disNPCdmg);
            CheckConfig("Disable damage to Buildings and structures", ref disStructdmg);
            CheckConfig("Disable damage to animals", ref disAnimaldmg);
            SaveConfig();
        }


        #endregion
        
        #region [Localization]


        protected override void LoadDefaultMessages() => lang.RegisterMessages(defmessages, this, "en");

        void Replier(BasePlayer player, string message, params string[] args) => player.ChatMessage($"{lang.GetMessage(mprefix, this)} {string.Format(lang.GetMessage(message, this), args)}");

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
        void CmdDD(BasePlayer player, string command, string[] args)
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


        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
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
                            if((entity is BasePlayer && displayerdmg) || (entity is BaseNpc && disNPCdmg) || (entity is BaseAnimalNPC && disAnimaldmg) || (entity is BuildingBlock && disStructdmg) || ((entity.GetComponent<Deployable>() != null) && disStructdmg))
                            {
                                info?.damageTypes?.ScaleAll(0);
                            }
                        }
                    }
                }
            }
        }
        
        void Init()
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

        void Unload() { if(savedisabled) Interface.Oxide.DataFileSystem.GetFile("DisableDamage").WriteObject(disdmg); }


        #endregion

    }
}
