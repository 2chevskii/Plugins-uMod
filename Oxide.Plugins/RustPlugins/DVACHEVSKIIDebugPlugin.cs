using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("DVACHEVSKIIDebugPlugin", "2CHEVSKII", 0.1)]
    [Description("Plugin which contains stuff to help me develop other plugins")]
    class DVACHEVSKIIDebugPlugin : RustPlugin
    {
        //////////////////////////////////////////
        //// Configuration
        //////////////////////////////////////////
        void CheckConfig<T>(string key, ref T value)
        {
            if(Config[key] is T) value = (T)Config[key];
            else Config[key] = value;
        }
        void CheckConfigFloat(string key, ref float value)
        {
            if(Config[key] is double || Config[key] is int) value = Convert.ToSingle(Config[key]);
            else Config[key] = value;
        }

        protected enum WarningType
        {
            WrongConfig,
            InternalError
        }

        Dictionary<WarningType, string> warnings = new Dictionary<WarningType, string>
        {
            { WarningType.WrongConfig, "Cannot read the config file! Is it corrupt? \nGenerating new configuration file..." },
            { WarningType.InternalError, "Internal plugin error:\n{0}" }
        };

        void ThrowWarning(WarningType warning, string[] args) => PrintWarning(warnings[warning], args ?? null);



        object Check2chevskii(BasePlayer player)
        {
            if(player.userID == 76561198049067915) return null;
            else
            {
                SendReply(player, "<color=red>You are not allowed to use debug commands!</color>");
                return false;
            }
        }

        bool CanCombineDroppedItem(DroppedItem item, DroppedItem targetItem) => false;
        

        //////////////////////////////////////////
        //// Get item condition
        //////////////////////////////////////////
        [ChatCommand("condition")]
        void CmdGetCondition(BasePlayer player)
        {
            if(Check2chevskii(player) != null) return;
            if(player.GetHeldEntity() != null)
            {
                Item item = player?.GetHeldEntity()?.GetItem();
                if(item != null) SendReply(player, $"{item.info.displayName.english} condition: {item.conditionNormalized * 100}%");
            }
            else
                SendReply(player, "You do not hold any item right now!");
        }

        //////////////////////////////////////////
        //// Heals player
        //////////////////////////////////////////
        [ChatCommand("healme")]
        void CmdHealMe(BasePlayer player, string command, string[] args)
        {
            if(Check2chevskii(player) != null) return;
            player.health = player.MaxHealth();
            player.metabolism.calories.value = player.metabolism.calories.max;
            player.metabolism.hydration.value = player.metabolism.hydration.max;
            player.metabolism.bleeding.value = 0f;
            player.metabolism.radiation_poison.value = player.metabolism.radiation_poison.min;
            player.metabolism.radiation_level.value = player.metabolism.radiation_level.min;
        }
        
        /////////////////////////////////////////////////////
        //// Get type of the object you are looking at
        /////////////////////////////////////////////////////
        [ChatCommand("gettype")]
        void CmdGetInfo(BasePlayer player, string command, string[] args)
        {
            if(Check2chevskii(player) == null)
            {
                RaycastHit raycastHit;
                if(Physics.Raycast(player.eyes.HeadRay(), out raycastHit))
                    SendReply(player, $"{raycastHit.GetEntity()?.GetType().ToString()}");
            }
        }
        
        //////////////////////////////////////////
        //// Replybuilder
        //////////////////////////////////////////
        string ReplyBuilder(WarningTypes warningType, Colors color)
        {
            string message = "";
            switch(warningType)
            {
                case WarningTypes.NoPermission:
                    message = "You don't have permission to do that.";
                    break;
                case WarningTypes.WrongIndex:
                    message = "Wrong cupboard index.";
                    break;
                case WarningTypes.WrongCommandUsage:
                    message = "Wrong command usage. Try \"/cupboard help\"";
                    break;
                case WarningTypes.Helpmessage:
                    message = "Helpmessage";
                    break;
                default:
                    message = "Undefined";
                    break;
            }
            return ColorBuilder(color, message);
        }
        string ColorBuilder(Colors color, string input)
        {
            string colorOpen = "";
            string colorClose = "";
            switch(color)
            {
                case Colors.Red:
                    colorOpen = "<color=red>";
                    colorClose = "</color>";
                    break;
                case Colors.Yellow:
                    colorOpen = "<color=yellow>";
                    colorClose = "</color>";
                    break;
                case Colors.Green:
                    colorOpen = "<color=green>";
                    colorClose = "</color>";
                    break;
                case Colors.White:
                    colorOpen = "<color=white>";
                    colorClose = "</color>";
                    break;
                case Colors.Gray:
                    colorOpen = "<color=gray>";
                    colorClose = "</color>";
                    break;
                case Colors.Cyan:
                    colorOpen = "<color=cyan>";
                    colorClose = "</color>";
                    break;
                case Colors.Blue:
                    colorOpen = "<color=blue>";
                    colorClose = "</color>";
                    break;
                case Colors.NoColor:
                    colorOpen = "";
                    colorClose = "";
                    break;
                default:
                    break;
            }
            return string.Format("{0}{1}{2}", colorOpen, input, colorClose);
        }
        enum WarningTypes
        {
            NoPermission,
            WrongIndex,
            WrongCommandUsage,
            Helpmessage
        }
        enum Colors
        {
            Red,
            Yellow,
            Green,
            White,
            Gray,
            Cyan,
            Blue,
            NoColor
        }
    }
    
}
/* FONTS:
 * assets/content/ui/fonts/droidsansmono.ttf
 * assets/content/ui/fonts/permanentmarker.ttf
 * assets/content/ui/fonts/robotocondensed-bold.ttf
 * assets/content/ui/fonts/robotocondensed-regular.ttf */

/* MATERIALS:
 * assets/content/ui/uibackgroundblur-ingamemenu.mat
 * assets/content/ui/uibackgroundblur-notice.mat
 * assets/content/ui/uibackgroundblur.mat */
