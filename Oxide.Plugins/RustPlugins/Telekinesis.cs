using System.Collections.Generic;
using UnityEngine;

//08.04.2019
//Test plugin, unfinished

namespace Oxide.Plugins
{
    [Info("Telekinesis", "2CHEVSKII", "0.1.0")]
    class Telekinesis : RustPlugin
    {
        List<BasePlayer> activatedplayers { get; set; } = new List<BasePlayer>();
        List<KeyValuePair<BasePlayer, BaseEntity>> docarry { get; set; } = new List<KeyValuePair<BasePlayer, BaseEntity>>();
        [ChatCommand("tk")]
        void ChCmdTk(BasePlayer player, string command, string[] args)
        {
            if(!activatedplayers.Contains(player)) activatedplayers.Add(player);
            else activatedplayers.Remove(player);
            SendReply(player, $"Toggled telekinesis: {(activatedplayers.Contains(player) ? "ON" : "OFF")}");
        }

        void OnPlayerInput(BasePlayer player, InputState input)
        {
            if(activatedplayers.Contains(player))
            {
                if(input.WasJustPressed(BUTTON.FIRE_THIRD) && !docarry.Contains(docarry.Find(x => x.Key == player)))
                {
                    RaycastHit raycastHit;
                    bool debrisflag = Physics.Raycast(ray: player.eyes.HeadRay(), hitInfo: out raycastHit, maxDistance: 3f, layerMask: LayerMask.GetMask("Debris"));
                    if(debrisflag)
                    {
                        var worldmodel = raycastHit.GetEntity();
                        if(worldmodel != null)
                        {
                            docarry.Add(new KeyValuePair<BasePlayer, BaseEntity>(player, worldmodel));
                            raycastHit.GetRigidbody().detectCollisions = false;
                            //raycastHit.GetRigidbody().interpolation = RigidbodyInterpolation.None;
                            player.ChatMessage($"You now carry: {worldmodel.GetItem()?.info?.shortname}");
                        }
                    }
                }
                if(input.WasJustReleased(BUTTON.FIRE_THIRD) && docarry.Contains(docarry.Find(x => x.Key == player)))
                {
                    player.ChatMessage($"You do not carry {docarry.Find(x => x.Key == player).Value.ShortPrefabName} anymore");
                    docarry.Find(x => x.Key == player).Value.GetComponent<Rigidbody>().detectCollisions = true;
                    //docarry.Find(x => x.Key == player).Value.GetComponent<Rigidbody>().interpolation = RigidbodyInterpolation.Interpolate;
                    docarry.Remove(docarry.Find(x => x.Key == player));
                }
            }
        }

        void OnFrame()
        {
            foreach(var kvp in docarry)
            {
                RaycastHit raycastHit;
                if(Physics.Raycast(kvp.Key.eyes.HeadRay(), hitInfo: out raycastHit, maxDistance: 1f))
                    kvp.Value.transform.position = raycastHit.point;
                else
                    kvp.Value.transform.position = kvp.Key.eyes.position + kvp.Key.eyes.HeadForward();
                kvp.Value.transform.rotation = kvp.Key.eyes.rotation;
            }
        }

    }
}
