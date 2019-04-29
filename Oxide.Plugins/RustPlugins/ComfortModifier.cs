using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("ComfortModifier", "2CHEVSKII", "0.1.0")]
    [Description("Configure comfort values")]
    class ComfortModifier : RustPlugin
    {
        void Loaded() { foreach(var player in BasePlayer.activePlayerList) AttachComponent(player); }

        void OnPlayerInit(BasePlayer player) => AttachComponent(player);

        void AttachComponent(BasePlayer player) => player.gameObject.AddComponent<MetabolismChanger>();

        void Unload() { foreach(var player in BasePlayer.activePlayerList) player.GetComponent<MetabolismChanger>().DetachComponent(); }

        class MetabolismChanger : MonoBehaviour
        {

            public BasePlayer player;

            void Awake()
            {
                player = GetComponent<BasePlayer>();
                player.ChatMessage($"Attached component {ToString()}");
            }

            void Update()
            {
                if(player.FindTrigger<TriggerComfort>() != null) player.metabolism.comfort.value = player.metabolism.comfort.max;
                else player.metabolism.comfort.value = player.metabolism.comfort.min;
            }
            
            void OnDestroy() => player.ChatMessage($"Detached component {ToString()}");

            public void DetachComponent() => Destroy(this);

        }
    }
}
