using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using System;

/* About:
 * Vlad-00003 - Original creator of this plugin (No more supports it)
 * 2CHEVSKII - Current maintainer (Responsible for all what's happening here)
 * Changelog:
 * Major rework and code cleanup
 * Option to dynamically disable collision when stated amount exceed
 * Option to log plugin activity
 * */

/* TODO:
* optimize the plugin using coroutines
*/

// Original idea and plugin version => Vlad-00003

namespace Oxide.Plugins
{
    [Info("IgnoreCollision", "2CHEVSKII", "1.1.0")]
    [Description("This plugin removes collisions between dropped items")]
    class IgnoreCollision : RustPlugin
    {

        #region [Fields]


        bool ignore = true;
        bool dynamicIgnore = true;
        //bool combineDetection = false;
        bool logs = true;
        int ignoreStartAmount = 300;
        int checkinterval = 3;
        List<DroppedItem> droppedItems = new List<DroppedItem>();
        Dictionary<DroppedItem, KeyValuePair<DroppedItem, int>> delayedItems = new Dictionary<DroppedItem, KeyValuePair<DroppedItem, int>>();
        bool nowDisabled = false;


        #endregion

        #region [Config]


        protected override void LoadDefaultConfig() { }

        void LoadConfigVariables()
        {
            CheckConfig("1.Disable collision:", ref ignore);
            CheckConfig("2.Dynamic collision disabling:", ref dynamicIgnore);
            CheckConfig("3.Amount to disable collision:", ref ignoreStartAmount);
            CheckConfig("4.How often to check for amount (sec):", ref checkinterval);
            //CheckConfig("5.Combine items even when disabled collision:", ref combineDetection); //doesnt work yet
            CheckConfig("5.Log plugin activity:", ref logs);
            SaveConfig();
        }

        void CheckConfig<T>(string key, ref T value)
        {
            if(Config[key] is T) value = (T)Config[key];
            else Config[key] = value;
        }


        #endregion

        #region [Hooks]


        void Init() => LoadConfigVariables();

        void Loaded()
        {
            if(ignore)
            {
                if(!dynamicIgnore) DisableCollision();
                else
                    timer.Every(checkinterval, () =>
                    {
                        CheckAmountOfDroppedItems();
                        //if(combineDetection) CheckCombine();
                    });
            }
            PrintConsoleMessage(WarningType.Load);
        }
        
        void Unload()
        {
            EnableCollision();
            PrintConsoleMessage(WarningType.Unload);
        }


        #endregion

        #region [Core]


        void DisableCollision() { Physics.IgnoreLayerCollision(26, 26, true); nowDisabled = true; }

        void EnableCollision() { Physics.IgnoreLayerCollision(26, 26, false); nowDisabled = false; }

        void CheckCombine()
        {
            if(nowDisabled)
            {
                RefreshDroppedItems();
                delayedItems.Clear();
                foreach(var droppeditem in droppedItems)
                {
                    var itemtocombinewith = droppedItems.Find(di => Vector3.Distance(di.ServerPosition, droppeditem.ServerPosition) < 2);
                    var finalAmount = droppeditem.item.amount + itemtocombinewith.item.amount;
                    if(finalAmount != 0 && finalAmount <= itemtocombinewith.item.info.stackable && droppeditem != null && itemtocombinewith != null && itemtocombinewith.item.info.stackable > 1 && droppeditem.item.info != itemtocombinewith.item.info || ((droppeditem.item.IsBlueprint() || itemtocombinewith.item.IsBlueprint()) && droppeditem.item.blueprintTarget != itemtocombinewith.item.blueprintTarget))
                    {
                        delayedItems.Add(droppeditem, new KeyValuePair<DroppedItem, int>(itemtocombinewith, finalAmount));
                    }
                }
                droppedItems.Clear();
                DelayedCombine(delayedItems);
            }
        }

        void DelayedCombine(Dictionary<DroppedItem, KeyValuePair<DroppedItem, int>> items)
        {
            foreach(var item in items.Keys)
            {
                item.DestroyItem();
                item.Kill();
                items[item].Key.item.amount = items[item].Value;
                items[item].Key.item.MarkDirty();
                if(items[item].Key.GetDespawnDuration() < float.PositiveInfinity)
                {
                    items[item].Key.Invoke(items[item].Key.IdleDestroy, items[item].Key.GetDespawnDuration());
                }
                Effect.server.Run("assets/bundled/prefabs/fx/notice/stack.world.fx.prefab", items[item].Key, 0u, Vector3.zero, Vector3.zero);
            }
        }
        
        void CheckAmountOfDroppedItems()
        {
            RefreshDroppedItems();
            int amount = droppedItems.Count;
            if(amount >= ignoreStartAmount)
            {
                if(!nowDisabled) PrintConsoleMessage(WarningType.TooManyItems);
                DisableCollision();
            }
            else
            {
                if(nowDisabled) PrintConsoleMessage(WarningType.TooLittleItems);
                EnableCollision();
            }
        }
        
        void RefreshDroppedItems() => droppedItems = GameObject.FindObjectsOfType<DroppedItem>().ToList();


        #endregion

        #region [Logs]


        void PrintConsoleMessage(WarningType warningType)
        {
            switch(warningType)
            {
                case WarningType.Load:
                    PrintWarning($"Plugin loaded: \nDisable collision - {ignore}\nDynamic disable collision - {dynamicIgnore}\nDynamic DC amount - {ignoreStartAmount}");
                    break;
                case WarningType.Unload:
                    PrintWarning($"Plugin is being unloaded, all items collision enabled!");
                    break;
                case WarningType.TooManyItems:
                    if(logs) PrintWarning($"Dropped item limit exceed ({ignoreStartAmount}) - collision disabled!");
                    break;
                case WarningType.TooLittleItems:
                    if(logs) PrintWarning($"Dropped items less than limit ({ignoreStartAmount}) - collision enabled!");
                    break;
                default:
                    break;
            }
        }
        
        enum WarningType
        {
            Load,
            Unload,
            TooManyItems,
            TooLittleItems
        }


        #endregion

    }
}