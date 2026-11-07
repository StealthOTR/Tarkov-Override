using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using EFT.CameraControl;
using EFT.Interactive;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> nightVision,thermalVision,noFlash,noVisor,noClip;
        private ConfigEntry<float> clipSpeed,clipBoost,doorRadius;
        private string miscMessage="Enter a raid to use these tools.";
        private bool extractArmed,extractRequested;
        private float extractArmUntil;
        private BSG.CameraEffects.NightVision forcedNight;
        private ThermalVision forcedThermal;
        private bool visionCaptured,oldNight,oldThermal,oldNightEnabled,oldThermalEnabled;
        private VisorEffect hiddenVisor;
        private bool oldVisorEnabled;
        private GrenadeFlashScreenEffect hiddenFlash;
        private EyeBurn hiddenBurn;
        private bool oldFlashEnabled,oldBurnEnabled;
        private ICharacterController clipController;
        private Player clipPlayer;
        private NoClipControllerState clipState;
        private Vector3 clipPosition;
        private readonly HashSet<Door> doorSet=new HashSet<Door>();
        private HarmonyLib.Harmony miscHarmony;
        private bool miscHooksAvailable;

        private void BindMiscTools()
        {
            nightVision=Setting("26 Misc vision","Night vision",false,"Force the main camera's native night vision without needing goggles. Mutually exclusive with Thermal vision. Resets between raids.");
            thermalVision=Setting("26 Misc vision","Thermal vision",false,"Force the main camera's native thermal vision. Mutually exclusive with Night vision. Restores the previous camera state when switched off.");
            noFlash=Setting("26 Misc vision","No flash",false,"Suppress flashbang whiteout and eye-burn afterimages on your main camera. Does not change grenade damage or bot behaviour.");
            noVisor=Setting("26 Misc vision","No visor",false,"Hide the visor screen effect, including cracks and dirt, while retaining the equipped visor and its protection.");
            noClip=Setting("27 Misc movement","No-clip",false,"Fly through geometry. Use the JSON NoClipForward/Back/Left/Right/Up/Down bindings; movement stops in menus. Switch off in open space. Resets between raids.");
            clipSpeed=Slider("27 Misc movement","Flight speed",5,0.5f,30,"No-clip movement speed in metres per second.");
            clipBoost=Slider("27 Misc movement","Flight boost multiplier",3,1,10,"Speed multiplier while holding the JSON NoClipBoost binding.");
            doorRadius=Slider("28 Misc actions","Door radius",10,1,50,"Radius in metres for Unlock nearby doors. Only normal locked Door objects are changed.");
            nightVision.Value=thermalVision.Value=noFlash.Value=noVisor.Value=noClip.Value=false;
            nightVision.SettingChanged+=delegate{if(nightVision.Value)thermalVision.Value=false;};
            thermalVision.SettingChanged+=delegate{if(thermalVision.Value)nightVision.Value=false;};
            try
            {
                miscHarmony=new HarmonyLib.Harmony("local.spt.practicemenu.misc");
                PatchMisc(typeof(MovementContext),"ApplyGravity","NoClipGravityPrefix");
                PatchMisc(typeof(GrenadeFlashScreenEffect),"Explode","NoFlashPrefix");
                PatchMisc(typeof(EyeBurn),"Burn","NoEyeBurnPrefix");
                miscHooksAvailable=true;
            }
            catch(Exception error)
            {
                if(miscHarmony!=null)miscHarmony.UnpatchSelf();
                miscHooksAvailable=false;Logger.LogWarning("Misc hooks unavailable: "+error);
            }
        }
        private void PatchMisc(Type type,string method,string prefix)
        {
            var original=HarmonyLib.AccessTools.Method(type,method);
            if(original==null)throw new MissingMethodException(type.FullName,method);
            miscHarmony.Patch(original,new HarmonyLib.HarmonyMethod(HarmonyLib.AccessTools.Method(typeof(Plugin),prefix)));
        }
        private static bool NoClipGravityPrefix(MovementContext __instance,ref Vector3 __0)
        {
            var owner=practiceOwner;
            if(owner==null||owner.clipController==null||owner.clipPlayer==null||!ReferenceEquals(__instance,owner.clipPlayer.MovementContext))return true;
            __0=Vector3.zero;return false;
        }
        private static bool IsLocalFlash(Behaviour effect,System.Reflection.FieldInfo field)
        {
            var owner=practiceOwner;
            if(owner==null||owner.noFlash==null||!owner.noFlash.Value||!owner.PracticeReady()||!CameraManager.Exist)return false;
            var controller=CameraManager.Instance.EffectsController;
            return controller!=null&&field!=null&&ReferenceEquals(effect,field.GetValue(controller));
        }
        private static bool NoFlashPrefix(GrenadeFlashScreenEffect __instance,ref float __0)
        {if(IsLocalFlash(__instance,flashField))__0=0;return true;}
        private static bool NoEyeBurnPrefix(EyeBurn __instance)
        {return !IsLocalFlash(__instance,burnField);}
        private void TickMiscTools()
        {
            try{TickItemCatalog();}
            catch(Exception error)
            {
                if(spawnCatalogScan!=null){spawnCatalogScan.Dispose();spawnCatalogScan=null;}
                catalogComplete=true;spawnSearching=false;
                spawnMessage="Catalog indexing stopped; you can still paste a template ID. See log.";
                Logger.LogWarning("Item catalog: "+error);
            }
            if(!PracticeReady())
            {
                if(visionCaptured||hiddenVisor!=null||hiddenFlash!=null||hiddenBurn!=null||clipController!=null||
                    nightVision.Value||thermalVision.Value||noFlash.Value||noVisor.Value||noClip.Value)ResetMiscTools();
                return;
            }
            if(!MenuOpen()&&!Cursor.visible&&!local.IsInventoryOpened)
            {
                if(Down("ToggleNightVision"))nightVision.Value=!nightVision.Value;
                if(Down("ToggleThermalVision"))thermalVision.Value=!thermalVision.Value;
                if(Down("ToggleNoFlash"))noFlash.Value=!noFlash.Value;
                if(Down("ToggleNoVisor"))noVisor.Value=!noVisor.Value;
                if(Down("ToggleNoClip"))noClip.Value=!noClip.Value;
            }
            try{TickNoClip();}
            catch(Exception error){noClip.Value=false;RestoreNoClip();MiscFailure("No-clip",error);}
        }
        private void LateMiscTools()
        {
            if(!PracticeReady())return;
            try{TickVision();TickScreenEffects();}
            catch(Exception error)
            {
                nightVision.Value=thermalVision.Value=noFlash.Value=noVisor.Value=false;
                RestoreVision();RestoreScreenEffects();MiscFailure("Camera tools",error);
            }
            if(clipController!=null&&clipPlayer==local)
            {
                try{clipState.Suspend();local.Teleport(clipPosition,false);}
                catch(Exception error){noClip.Value=false;RestoreNoClip();MiscFailure("No-clip",error);}
            }
        }
        private void MiscFailure(string feature,Exception error)
        {miscMessage=feature+" failed; see BepInEx log.";Logger.LogWarning(feature+": "+error);}
        private void TickVision()
        {
            var manager=CameraManager.Exist?CameraManager.Instance:null;
            var nv=manager!=null?manager.NightVision:null;
            var tv=manager!=null?manager.ThermalVision:null;
            if(visionCaptured&&(nv!=forcedNight||tv!=forcedThermal))RestoreVision();
            if(!nightVision.Value&&!thermalVision.Value){RestoreVision();return;}
            if(nv==null||tv==null){miscMessage="Vision components are not ready on this camera.";return;}
            if(!visionCaptured)
            {
                forcedNight=nv;forcedThermal=tv;oldNight=nv.On;oldThermal=tv.On;
                oldNightEnabled=nv.enabled;oldThermalEnabled=tv.enabled;visionCaptured=true;
            }
            bool wantNight=nightVision.Value,wantThermal=thermalVision.Value;
            // Turn the previous effect off first: Switch also manages shared camera components.
            if(nv.On&&!wantNight)nv.Switch(false);
            if(tv.On&&!wantThermal)tv.Switch(false);
            if(wantNight&&!nv.On){nv.enabled=true;nv.Switch(true);}
            if(wantThermal&&!tv.On){tv.enabled=true;tv.Switch(true);}
            if(wantNight)nv.enabled=true;if(wantThermal)tv.enabled=true;
        }
        private void RestoreVision()
        {
            if(!visionCaptured)return;
            visionCaptured=false;
            try
            {
                if(forcedNight!=null&&forcedNight.On)forcedNight.Switch(false);
                if(forcedThermal!=null&&forcedThermal.On)forcedThermal.Switch(false);
                if(forcedNight!=null){if(oldNight)forcedNight.Switch(true);forcedNight.enabled=oldNightEnabled;}
                if(forcedThermal!=null){if(oldThermal)forcedThermal.Switch(true);forcedThermal.enabled=oldThermalEnabled;}
            }
            catch(Exception error){Logger.LogWarning("Vision restoration: "+error.Message);}
            forcedNight=null;forcedThermal=null;
        }
        private static readonly System.Reflection.FieldInfo flashField=HarmonyLib.AccessTools.Field(typeof(EffectsController),"_grenadeFlash");
        private static readonly System.Reflection.FieldInfo burnField=HarmonyLib.AccessTools.Field(typeof(EffectsController),"_eyeBurn");
        private void TickScreenEffects()
        {
            var manager=CameraManager.Exist?CameraManager.Instance:null;
            var visor=manager!=null?manager.VisorEffect:null;
            var effects=manager!=null?manager.EffectsController:null;
            var flash=effects!=null&&flashField!=null?flashField.GetValue(effects) as GrenadeFlashScreenEffect:null;
            var burn=effects!=null&&burnField!=null?burnField.GetValue(effects) as EyeBurn:null;
            if(hiddenVisor!=null&&(!noVisor.Value||hiddenVisor!=visor)){hiddenVisor.enabled=oldVisorEnabled;hiddenVisor=null;}
            if(noVisor.Value&&visor!=null)
            {if(hiddenVisor==null){hiddenVisor=visor;oldVisorEnabled=visor.enabled;}visor.enabled=false;}
            if(hiddenFlash!=null&&(!noFlash.Value||hiddenFlash!=flash)){hiddenFlash.enabled=oldFlashEnabled;hiddenFlash=null;}
            if(hiddenBurn!=null&&(!noFlash.Value||hiddenBurn!=burn)){hiddenBurn.enabled=oldBurnEnabled;hiddenBurn=null;}
            if(noFlash.Value)
            {
                if(flash!=null){if(hiddenFlash==null){hiddenFlash=flash;oldFlashEnabled=flash.enabled;flash.Explode(0);flash.EffectStrength=0;}flash.enabled=false;}
                if(burn!=null){if(hiddenBurn==null){hiddenBurn=burn;oldBurnEnabled=burn.enabled;burn.ClearEyeBurnRT();}burn.enabled=false;}
            }
        }
        private void RestoreScreenEffects()
        {
            if(hiddenVisor!=null)hiddenVisor.enabled=oldVisorEnabled;
            if(hiddenFlash!=null)hiddenFlash.enabled=oldFlashEnabled;
            if(hiddenBurn!=null)hiddenBurn.enabled=oldBurnEnabled;
            hiddenVisor=null;hiddenFlash=null;hiddenBurn=null;
        }
        private void TickNoClip()
        {
            if(!noClip.Value){RestoreNoClip();return;}
            if(!miscHooksAvailable||!compactAvailable){noClip.Value=false;miscMessage="No-clip input/gravity hooks unavailable. Check the log.";return;}
            if(clipController!=null&&!ReferenceEquals(clipController,local.CharacterController))RestoreNoClip();
            if(clipController==null)
            {
                var controller=local.CharacterController;
                if(controller==null)return;
                var state=new NoClipControllerState(controller);
                clipState=state;clipPlayer=local;clipPosition=local.Position;clipController=controller;
                local.EnableSprint(false);
            }
            clipState.Suspend();
            if(MenuOpen()||Cursor.visible||local.IsInventoryOpened||!Application.isFocused||Time.timeScale==0||camera==null)return;
            Vector3 direction=Vector3.zero;
            if(Held("NoClipForward"))direction+=camera.transform.forward;
            if(Held("NoClipBack"))direction-=camera.transform.forward;
            if(Held("NoClipRight"))direction+=camera.transform.right;
            if(Held("NoClipLeft"))direction-=camera.transform.right;
            if(Held("NoClipUp"))direction+=Vector3.up;
            if(Held("NoClipDown"))direction-=Vector3.up;
            if(direction.sqrMagnitude>1)direction.Normalize();
            float speed=clipSpeed.Value*(Held("NoClipBoost")?clipBoost.Value:1);
            clipPosition+=direction*speed*Mathf.Min(Time.deltaTime,0.05f);
        }
        private void RestoreNoClip()
        {
            if(clipController==null)return;
            try
            {
                if(clipPlayer!=null&&clipPlayer.MovementContext!=null)clipPlayer.MovementContext.ResetFlying();
            }
            catch(Exception error){Logger.LogWarning("No-clip restoration: "+error.Message);}
            if(clipState!=null)clipState.Restore(delegate(Exception error){Logger.LogWarning("No-clip controller restoration: "+error.Message);});
            clipController=null;clipPlayer=null;clipState=null;
        }
        private void ResetMiscTools()
        {
            RestoreNoClip();RestoreVision();RestoreScreenEffects();CancelItemSpawn();
            if(nightVision!=null)nightVision.Value=false;if(thermalVision!=null)thermalVision.Value=false;
            if(noFlash!=null)noFlash.Value=false;if(noVisor!=null)noVisor.Value=false;if(noClip!=null)noClip.Value=false;
            extractArmed=false;extractRequested=false;
        }
        private void ShutdownMiscTools()
        {
            ResetMiscTools();
            if(miscHarmony!=null){miscHarmony.UnpatchSelf();miscHarmony=null;}
            if(spawnCatalogScan!=null){spawnCatalogScan.Dispose();spawnCatalogScan=null;}
        }
        private void UnlockDoors(bool nearby)
        {
            if(!PracticeReady())return;
            try
            {
                doorSet.Clear();
                // A button-triggered scene query, never part of the frame loop.
                var doors=UnityEngine.Object.FindObjectsOfType<Door>();
                Door nearest=null;float best=nearby?doorRadius.Value:4f;
                foreach(var door in doors)
                {
                    if(door==null||door.DoorState!=EDoorState.Locked)continue;
                    float distance=Vector3.Distance(local.Position,door.transform.position);
                    if(distance>best)continue;
                    if(nearby)doorSet.Add(door);else{nearest=door;best=distance;}
                }
                if(!nearby&&nearest!=null)doorSet.Add(nearest);
                int count=0;
                foreach(var door in doorSet){door.SetDoorState(EDoorState.Shut,true);if(door.DoorState==EDoorState.Shut)count++;}
                miscMessage=count==0?"No locked doors found in range.":"Unlocked "+count+" door(s).";
            }
            catch(Exception error){MiscFailure("Unlock doors",error);}
            finally{doorSet.Clear();}
        }
        private void ExtractNow()
        {
            if(!PracticeReady()||extractRequested)return;
            try
            {
                var game=UnityEngine.Object.FindObjectOfType<LocalGame>();
                if(game==null||game.GameWorld!=world||game.ProfileId!=local.ProfileId||
                    (game.Status!=GameStatus.Started&&game.Status!=GameStatus.Runned&&game.Status!=GameStatus.Running))
                {miscMessage="No active local raid found; extraction was not requested.";return;}
                extractRequested=true;extractArmed=false;
                SetCompactOpen(false);
                ResetPracticeTools(); // Restore temporary health/camera/movement before normal raid saving.
                extractRequested=true;
                game.Stop(local.ProfileId,ExitStatus.Survived,"Field Toolkit",0f);
                miscMessage="Extraction requested through the normal local-raid end flow.";
            }
            catch(Exception error){extractRequested=false;MiscFailure("Instant extraction",error);}
        }
        private void DrawMiscActions()
        {
            DrawAirdropControls();
            MenuSection("DOORS");
            bool previous=GUI.enabled;GUI.enabled=PracticeReady();
            Rect one=MenuRow();if(MenuButton(one,"Unlock nearest door (4m)"))UnlockDoors(false);
            MenuHover(one,"Unlock the nearest locked normal door within four metres. Leaves it shut so you can open it normally.");
            Rect many=MenuRow();if(MenuButton(many,"Unlock nearby doors"))UnlockDoors(true);
            MenuHover(many,"Unlock locked normal doors within the configured Door radius. Does not enable scripted gates or power switches.");
            DrawMenuEntry(doorRadius);
            MenuSection("RAID");
            GUI.enabled=PracticeReady()&&exitsAvailable;DrawMenuEntry(alwaysExits);
            GUI.enabled=PracticeReady();
            if(!exitsAvailable)MenuNote("Extraction override hooks unavailable; check the BepInEx log.");
            if(extractArmed&&Time.unscaledTime>extractArmUntil)extractArmed=false;
            GUI.enabled=PracticeReady()&&!extractRequested;
            Rect extract=MenuRow();
            if(MenuButton(extract,extractArmed?"Confirm instant extraction":"Instant extraction"))
            {if(extractArmed)ExtractNow();else{extractArmed=true;extractArmUntil=Time.unscaledTime+5;}}
            MenuHover(extract,"End this raid immediately as Survived through the game's normal raid-end/save flow, from your current position. Click again within five seconds to confirm.");
            if(extractArmed)MenuNote("Click again within five seconds to finish this raid.");
            GUI.enabled=previous;
            MenuSection("STATUS");MenuNote(PracticeReady()?miscMessage:"Enter a raid with a living local player first.");
            MenuNote("No-clip: use the movement bindings in Keys / config.json. Leave walls and return near ground level before switching it off. Vision modes are mutually exclusive. Panic resets all new toggles.");
        }
    }
}
