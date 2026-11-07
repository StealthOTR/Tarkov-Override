using System;
using System.Collections.Generic;

using BepInEx.Configuration;
using EFT;


using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> nearbyAlerts,alertSound;
        private ConfigEntry<float> alertRadius,alertCooldown,alertVolume;
        private float nextAlert,nextAlertSound;
        private string alertCaption;
        private HashSet<Player> alertInside=new HashSet<Player>(),alertNext=new HashSet<Player>();
        private AudioSource alertAudio;
        private AudioClip alertClip;
        private void BindRaidAwareness()
        {
            nearbyAlerts=Setting("32 Nearby enemy alerts","Nearby enemy alerts",false,"Show a warning when living AI enter your radius, independently of ESP. Matching nonempty group IDs are excluded; modded alliances may not share a group ID.");
            alertRadius=Slider("32 Nearby enemy alerts","Alert radius",50,5,200,"Enemy warning radius in metres.");
            alertSound=Setting("32 Nearby enemy alerts","Alert sound",false,"Play a short tone when a new enemy enters the radius. A five-metre re-entry margin and cooldown prevent repeated sounds at the boundary.");
            alertCooldown=Slider("32 Nearby enemy alerts","Alert sound cooldown",3,0.5f,30,"Minimum seconds between new-enemy sounds.");
            alertVolume=Slider("32 Nearby enemy alerts","Alert volume",0.25f,0,1,"Volume of the optional warning tone.");
            nearbyAlerts.Value=false;
            BindExtractionOverrides();
        }
        private void TickRaidAwareness()
        {
            if(!MenuOpen()&&!Cursor.visible&&!local.IsInventoryOpened)
            {
                if(Down("ToggleInfiniteMedical")&&medicalAvailable)infiniteMedical.Value=!infiniteMedical.Value;
                if(Down("ToggleNoWeight")&&weightAvailable)noWeight.Value=!noWeight.Value;
                if(Down("ToggleInteractionRange")&&interactionAvailable)extendedInteraction.Value=!extendedInteraction.Value;
                if(Down("ToggleFireRate")&&rateAvailable)customFireRate.Value=!customFireRate.Value;
                if(Down("ToggleEnemyAlerts"))nearbyAlerts.Value=!nearbyAlerts.Value;
                if(Down("ToggleAvailableExtractions")&&exitsAvailable)alwaysExits.Value=!alwaysExits.Value;
                if(Down("RepairEquippedGear"))RepairEquippedGear();
            }
            try{TickExtractionOverrides();}
            catch(Exception error){alwaysExits.Value=false;ResetExtractionOverrides();Logger.LogWarning("Extraction override disabled: "+error);}
            if(!nearbyAlerts.Value){alertCaption=null;alertInside.Clear();alertNext.Clear();if(alertAudio!=null)alertAudio.Stop();}
            else if(Time.unscaledTime>=nextAlert)
            {
                nextAlert=Time.unscaledTime+0.25f;
                try{TickEnemyAlert();}
                catch(Exception error){nearbyAlerts.Value=false;alertCaption=null;Logger.LogWarning("Nearby alerts disabled: "+error);}
            }
        }
        private void TickEnemyAlert()
        {
            alertNext.Clear();int count=0;Player nearest=null;float closest=float.MaxValue;bool entering=false;
            foreach(var player in world.AllAlivePlayersList)
            {
                if(!ValidPlayer(player)||(!string.IsNullOrEmpty(local.GroupId)&&player.GroupId==local.GroupId))continue;
                float distance=Vector3.Distance(local.Position,player.Position);
                bool wasInside=alertInside.Contains(player);
                if(distance<=alertRadius.Value+(wasInside?5f:0f))alertNext.Add(player);
                if(distance>alertRadius.Value)continue;
                count++;if(!wasInside)entering=true;
                if(distance<closest){closest=distance;nearest=player;}
            }
            var previous=alertInside;alertInside=alertNext;alertNext=previous;
            if(nearest==null){alertCaption=null;return;}
            Vector3 direction=nearest.Position-local.Position;
            string bearing="nearby";
            if(camera!=null)
            {
                float ahead=Vector3.Dot(camera.transform.forward,direction.normalized),side=Vector3.Dot(camera.transform.right,direction.normalized);
                bearing=ahead<-0.5f?"behind":ahead>0.7f?"ahead":side<0?"left":"right";
            }
            alertCaption="NEARBY: "+count+" | "+EnemyType(nearest)+" "+closest.ToString("0")+"m "+bearing;
            if(entering&&alertSound.Value&&Application.isFocused&&!MenuOpen()&&Time.unscaledTime>=nextAlertSound)
            {nextAlertSound=Time.unscaledTime+alertCooldown.Value;PlayEnemyAlert();}
        }
        private void PlayEnemyAlert()
        {
            if(alertAudio==null)
            {
                alertAudio=gameObject.AddComponent<AudioSource>();alertAudio.playOnAwake=false;alertAudio.spatialBlend=0;
                const int rate=22050,length=3307;var samples=new float[length];
                for(int i=0;i<length;i++)samples[i]=(float)Math.Sin(2*Math.PI*880*i/rate)*0.35f*(float)Math.Sin(Math.PI*i/length);
                alertClip=AudioClip.Create("FieldToolkitEnemyAlert",length,1,rate,false);alertClip.SetData(samples,0);
            }
            alertAudio.PlayOneShot(alertClip,alertVolume.Value);
        }
        private void DrawRaidAwareness()
        {
            if(nearbyAlerts.Value&&!string.IsNullOrEmpty(alertCaption))
                Label(new Vector2(Screen.width*0.5f,70),new GUIContent(alertCaption),new Color(1,0.55f,0.18f));
        }
        private void ResetRaidAwareness()
        {
            if(nearbyAlerts!=null)nearbyAlerts.Value=false;
            alertInside.Clear();alertNext.Clear();alertCaption=null;
            nextAlert=nextAlertSound=0;if(alertAudio!=null)alertAudio.Stop();
            ResetExtractionOverrides();
        }
        private void ShutdownAwarenessAudio()
        {if(alertAudio!=null)Destroy(alertAudio);if(alertClip!=null)Destroy(alertClip);alertAudio=null;alertClip=null;}
    }
}

