using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using UnityEngine;

namespace Oxide.Plugins
{
    [Info("CTFEvents UI", "2CHEVSKII", "0.1.0")]
    class CTFEventsUI : CovalencePlugin
    {

        void OnServerInitialized()
        {
            foreach (BasePlayer basePlayer in BasePlayer.activePlayerList)
            {
                basePlayer.gameObject.AddComponent<Ui>();
            }
        }

        void Unload()
        {
            foreach (BasePlayer basePlayer in BasePlayer.activePlayerList)
            {
                UnityEngine.Object.Destroy(basePlayer.GetComponent<Ui>());
            }
        }

        class Ui : MonoBehaviour
        {
            BasePlayer player;

            void Awake()
            {
                player = GetComponent<BasePlayer>();
            }

            void Start()
            {
                player.ChatMessage("UI init");
            }

            void OnCtfEnterZone(object flag)
            {
                player.ChatMessage("Enter zone");
            }

            void OnCtfLeaveZone(object flag)
            {
                player.ChatMessage("Leave zone");
            }

            void OnCtfStatusUpdate(object flag)
            {
                player.ChatMessage("Status update");
            }
        }
    }
}
