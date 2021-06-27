using System;
using System.Collections.Generic;

using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("Teleport Gun", "2CHEVSKII", "0.2.0")]
    [Description("Shoot something to teleport to it!")]
    class TeleportGun : CovalencePlugin
    {

        #region -Hooks-


        void Init()
        {
            permission.RegisterPermission(PERMISSION_USE, this);
            LoadConfigVariables();
        }

        protected override void LoadDefaultConfig() { }

        protected override void LoadDefaultMessages() => lang.RegisterMessages(defaultMessages, this, "en");

        void OnPlayerAttack(BasePlayer attacker, HitInfo info)
        {
            Item item;
            if (enabledplayers.TryGetValue(attacker.IPlayer, out item) && info.Weapon?.GetItem() == item)
            {
                attacker.MovePosition(info.HitPositionWorld);
            }
        }

        void OnActiveItemChanged(BasePlayer player)
        {
            if (enabledplayers.ContainsKey(player.IPlayer) && autodisable)
            {
                Disable(player);
            }
        }


        #endregion

        #region -Fields-


        bool autodisable = true;
        int autodisabletimer = 0;
        const string PERMISSION_USE = "teleportgun.use";
        const string CHAT_PREFIX = "Chat prefix";
        const string ENABLED = "Enabled";
        const string DISABLED = "Disabled";
        const string WRONG_ITEM = "Wrong item";
        const string NO_PERMISSION = "No permission";
        Dictionary<string, string> defaultMessages = new Dictionary<string, string>
        {
            [CHAT_PREFIX] = "<color=yellow>[</color>TELEPORT GUN<color=yellow>]</color>",
            [ENABLED] = "<color=#47FF11>E</color>nabled teleport gun<color=#47FF11>.</color>",
            [DISABLED] = "<color=red>D</color>isabled teleport gun<color=red>.</color>",
            [NO_PERMISSION] = "<color=red>Y</color>ou have no permission to use teleport gun<color=red>!</color>",
            [WRONG_ITEM] = "<color=yellow>A</color>ctive item must be a gun<color=yellow>!</color>"
        };
        Dictionary<IPlayer, Item> enabledplayers = new Dictionary<IPlayer, Item>();
        Dictionary<BasePlayer, Timer> timers = new Dictionary<BasePlayer, Timer>();

        #endregion

        #region -Command-


        [Command("tpgun")]
        void CmdTpGun(IPlayer player, string command, string[] args)
        {
            if (enabledplayers.ContainsKey(player))
            {
                Disable((BasePlayer)player.Object);
            }
            else if (!player.HasPermission(PERMISSION_USE))
            {
                Replier(player, NO_PERMISSION);
            }
            else Enable((BasePlayer)player.Object);
        }


        #endregion

        #region -Helpers-

        void Enable(BasePlayer player)
        {
            var heldEnt = player.GetHeldEntity();

            if (heldEnt is BaseProjectile)
            {
                enabledplayers.Add(player.IPlayer, heldEnt.GetItem());
                if (autodisabletimer > 0)
                    timers.Add(player, timer.Once(Convert.ToSingle(autodisabletimer), () => Disable(player)));
                Replier(player, ENABLED);
            }
            else Replier(player, WRONG_ITEM);
        }

        void Disable(BasePlayer player)
        {
            enabledplayers.Remove(player.IPlayer);
            if(timers.ContainsKey(player))
            {
                timers[player].Destroy();
                timers.Remove(player);
            }
            Replier(player, DISABLED);
        }

        void LoadConfigVariables()
        {
            CheckConfig("Auto disable when active item changed", ref autodisable);
            CheckConfig("Auto disable after timer (seconds)", ref autodisabletimer);
            SaveConfig();
        }

        void CheckConfig<T>(string key, ref T value)
        {
            if (Config[key] is T)
                value = (T)Config[key];
            else
                Config[key] = value;
        }

        void Replier(BasePlayer player, string message) => Replier(player.IPlayer, message);
        void Replier(IPlayer player, string message) => player.Message(lang.GetMessage(message, this, player.Id), lang.GetMessage(CHAT_PREFIX, this, player.Id));


        #endregion

    }
}
