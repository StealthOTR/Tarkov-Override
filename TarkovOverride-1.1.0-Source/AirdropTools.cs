using System;
using EFT;
using EFT.Airdrop;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private GameWorld airdropRaid;
        private float nextManualAirdrop;
        private string airdropMessage="Request a normal plane-delivered supply drop on a supported map.";
        private bool CanRequestAirdrop(out string reason)
        {
            if(airdropRaid!=world)
            {
                airdropRaid=world;nextManualAirdrop=0;
                airdropMessage="Request a normal plane-delivered supply drop on a supported map.";
            }
            if(!PracticeReady()){reason="Enter a raid with a living local player first.";return false;}
            if(extractRequested){reason="The raid is ending.";return false;}
            var client=world as ClientGameWorld;
            var processor=client!=null?client.ClientSynchronizableObjectLogicProcessor:null;
            var manager=processor!=null?processor.ServerAirdropManager:null;
            if(manager==null||!manager.IsInited){reason="Native airdrops are unavailable or not initialized on this map.";return false;}
            if(manager._airdropPoints==null||manager._airdropPoints.Count==0)
            {reason="No active airdrop points are available on this map.";return false;}
            if(!manager.CanSummonAirdrop){reason="Native airdrops are disabled for this raid.";return false;}
            float remaining=nextManualAirdrop-Time.unscaledTime;
            if(remaining>0){reason="Another request is available in "+Mathf.CeilToInt(remaining)+" seconds.";return false;}
            reason="Ready. The plane uses a map drop point, not your exact position.";return true;
        }
        private void RequestAirdrop()
        {
            string reason;if(!CanRequestAirdrop(out reason)){airdropMessage=reason;return;}
            try
            {
                // This virtual call dispatches to ClientGameWorld.InitAirdrop in solo SPT.
                // It selects a native point, takes a real plane from its pool and initializes
                // the offline plane logic, which creates the crate and requests normal loot.
                world.InitAirdrop(null,false,Vector3.zero);
                nextManualAirdrop=Time.unscaledTime+60;
                airdropMessage="Airdrop requested through the native plane system. Watch and listen for the aircraft; delivery is not instant.";
            }
            catch(Exception error)
            {airdropMessage="Airdrop request failed; see the BepInEx log.";Logger.LogWarning("Manual airdrop: "+error);}
        }
        private void DrawAirdropControls()
        {
            MenuSection("SUPPLY DROP");
            string reason;bool ready=CanRequestAirdrop(out reason),previous=GUI.enabled;
            GUI.enabled=previous&&ready;
            Rect button=MenuRow();if(MenuButton(button,"Request airdrop"))RequestAirdrop();
            MenuHover(button,"Request a real plane-delivered airdrop with normal SPT loot at a native map drop point. Available only when this raid's airdrop manager is enabled and has active points. Requests have a 60-second cooldown.");
            GUI.enabled=previous;
            MenuNote(airdropMessage);if(!ready)MenuNote(reason);
        }
    }
}
