using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.Animations;
using EFT.HealthSystem;
using HarmonyLib;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private static Plugin combatOwner;
        private Harmony combatHarmony;
        private bool rcsAvailable,demigodAvailable,restoringHealth;
        private ConfigEntry<bool> demigod;
        private ConfigEntry<float> demigodMultiplier;
        private ActiveHealthController boostedController;
        private float appliedHealthMultiplier=1;
        private sealed class HealthSnapshot {public EBodyPart Part;public HealthValue Health;public float Maximum,Minimum;}
        private readonly List<HealthSnapshot> healthSnapshots=new List<HealthSnapshot>();
        private sealed class RcsProfile
        {public ConfigEntry<bool> Enabled,RightMouse;public ConfigEntry<float> Vertical,Horizontal,Kickback;}
        private readonly Dictionary<AimMode,RcsProfile> rcsProfiles=new Dictionary<AimMode,RcsProfile>();
        private readonly Dictionary<ConfigEntryBase,int> rcsOrder=new Dictionary<ConfigEntryBase,int>();
        private void BindCombatTools()
        {
            combatOwner=this;
            foreach(AimMode mode in Enum.GetValues(typeof(AimMode)))
            {
                string section=mode==AimMode.Legit?"24 Legit Aim RCS":"25 Rage Aim RCS";
                var profile=new RcsProfile();rcsProfiles[mode]=profile;
                profile.Enabled=Setting(section,"RCS enabled",false,"Reduce recoil for the selected aim mode while that mode's master enable is on. Works without a target lock and affects only your local weapon.");
                profile.Vertical=Slider(section,"Vertical reduction (%)",mode==AimMode.Legit?40:100,0,100,"Reduction of pitch/vertical recoil impulses. 0 leaves normal recoil; 100 cancels this component's new impulses.");
                profile.Horizontal=Slider(section,"Horizontal reduction (%)",mode==AimMode.Legit?25:100,0,100,"Reduction of yaw/horizontal recoil impulses. 0 leaves normal recoil; 100 cancels this component's new impulses.");
                profile.Kickback=Slider(section,"Kickback reduction (%)",mode==AimMode.Legit?15:100,0,100,"Reduction of rearward weapon-position recoil. Does not remove unrelated sway, animations or camera roll.");
                profile.RightMouse=Setting(section,"RCS right mouse only",mode==AimMode.Legit,"Apply RCS only while right mouse is held. This is independent of target acquisition.");
                ConfigEntryBase[] entries={profile.Enabled,profile.Vertical,profile.Horizontal,profile.Kickback,profile.RightMouse};
                for(int i=0;i<entries.Length;i++){rcsOrder[entries[i]]=i;profileBuckets[entries[i]]=mode==AimMode.Legit?5:7;}
            }
            demigod=Setting("23 Player practice","Demigod",false,"Increase current and maximum body-part health by the chosen multiplier, preserving health percentage. No continuous healing or invulnerability. Normal maxima are restored when disabled, between raids or by Panic.");
            demigodMultiplier=Slider("23 Player practice","Health multiplier",5,1,100,"Demigod health multiplier. 5 means five times each body's original maximum. Changing this does not compound the boost or repair destroyed limbs.");
            demigod.Value=false;
            demigod.SettingChanged+=delegate{if(!demigod.Value)RestoreDemigodHealth();};
            combatHarmony=new Harmony("local.spt.practicemenu.combattools");
            try
            {
                var prefix=new HarmonyMethod(typeof(Plugin).GetMethod("RcsImpulsePrefix",BindingFlags.NonPublic|BindingFlags.Static));
                combatHarmony.Patch(AccessTools.DeclaredMethod(typeof(RecoilProcessBase),"AddAccelerationLimitless"),prefix);
                combatHarmony.Patch(AccessTools.DeclaredMethod(typeof(NewRotationRecoilProcess),"AddAccelerationLimitless"),prefix);
                rcsAvailable=true;
            }
            catch(Exception error){Logger.LogWarning("RCS hooks unavailable: "+error.Message);}
            try
            {
                // Both ActiveHealthController Store overloads call this base implementation.
                var store=AccessTools.Method(typeof(ActiveHealthController).BaseType,"Store",new[]{typeof(Profile.HealthInfo)});
                if(store==null)throw new MissingMethodException("Health serialization hook not found.");
                combatHarmony.Patch(store,null,new HarmonyMethod(typeof(Plugin).GetMethod("NormalizeStoredHealth",BindingFlags.NonPublic|BindingFlags.Static)));
                demigodAvailable=true;
            }
            catch(Exception error){Logger.LogWarning("Demigod disabled because save normalization could not be installed: "+error.Message);}
            BindRageSway();
        }
        private static void RcsImpulsePrefix(RecoilProcessBase __instance,int __0,ref float __1)
        {
            var plugin=combatOwner;
            if(plugin==null||!plugin.rcsAvailable||!plugin.aim.Value||!plugin.PracticeReady()||plugin.MenuOpen()||Cursor.visible)return;
            RcsProfile profile=plugin.rcsProfiles[plugin.activeAimMode.Value];
            if(!profile.Enabled.Value||(profile.RightMouse.Value&&!Input.GetKey(KeyCode.Mouse1)))return;
            var animation=plugin.local.ProceduralWeaponAnimation;
            if(animation==null||animation.Shootingg==null)return;
            var effect=animation.Shootingg.CurrentRecoilEffect;if(effect==null)return;
            float amount;
            if(ReferenceEquals(__instance,effect.HandRotationRecoilEffect)||ReferenceEquals(__instance,effect.CameraRotationRecoilEffect))
            {if(__0==0)amount=profile.Vertical.Value;else if(__0==1)amount=profile.Horizontal.Value;else return;}
            else if(ReferenceEquals(__instance,effect.HandPositionRecoilEffect))
            {if(__0==0)amount=profile.Horizontal.Value;else if(__0==1)amount=profile.Vertical.Value;else if(__0==2)amount=profile.Kickback.Value;else return;}
            else return;
            __1=CombatMath.Reduce(__1,amount);
        }
        private void TickDemigodHealth()
        {
            try{UpdateDemigodHealth();}
            catch(Exception error)
            {
                if(demigod!=null)demigod.Value=false;RestoreDemigodHealth();
                practiceMessage="Demigod disabled after an error; see log.";Logger.LogWarning(error);
            }
        }
        private void UpdateDemigodHealth()
        {
            if(demigod==null)return;
            if(!demigod.Value||!demigodAvailable){if(healthSnapshots.Count>0)RestoreDemigodHealth();return;}
            if(!PracticeReady())return;
            var controller=local.ActiveHealthController;
            if(!ReferenceEquals(controller,boostedController))
            {
                RestoreDemigodHealth();boostedController=controller;
                foreach(var part in controller.BodyState)
                    if(part.Key!=EBodyPart.Common&&part.Value.Health!=null)
                        healthSnapshots.Add(new HealthSnapshot{Part=part.Key,Health=part.Value.Health,Maximum=part.Value.Health.Maximum,Minimum=part.Value.Health.Minimum});
                appliedHealthMultiplier=1;
            }
            float multiplier=demigodMultiplier.Value;
            if(float.IsNaN(multiplier)||float.IsInfinity(multiplier)||multiplier<1)throw new InvalidOperationException("Invalid Demigod health multiplier.");
            if(Math.Abs(multiplier-appliedHealthMultiplier)<0.0001f)return;
            foreach(var snapshot in healthSnapshots)SetBoostedHealth(snapshot,snapshot.Maximum*multiplier,true);
            appliedHealthMultiplier=multiplier;
            practiceMessage="Demigod: "+multiplier.ToString("0.##")+"x body-part health.";
        }
        private void SetBoostedHealth(HealthSnapshot snapshot,float maximum,bool notify)
        {
            // Full health restoration replaces HealthValue objects; always resolve the current one.
            if(boostedController!=null&&boostedController.BodyState.ContainsKey(snapshot.Part))
                snapshot.Health=boostedController.BodyState[snapshot.Part].Health;
            var value=snapshot.Health.Value;
            float target=CombatMath.RescaleHealth(value.Current,value.Maximum,maximum,snapshot.Minimum);
            float previous=value.Current;value.Maximum=maximum;value.Minimum=snapshot.Minimum;snapshot.Health.Value=value;
            if(notify&&boostedController!=null&&boostedController.IsAlive&&!boostedController.IsBodyPartDestroyed(snapshot.Part))
            {
                try{boostedController.ChangeHealth(snapshot.Part,target-previous,DamageHelper.Existence);}
                catch(Exception error){value.Current=target;snapshot.Health.Value=value;Logger.LogWarning("Health UI notification failed; health value was updated directly: "+error.Message);}
            }
            else {value.Current=target;snapshot.Health.Value=value;}
        }
        private void RestoreDemigodHealth()
        {
            if(restoringHealth||healthSnapshots.Count==0)return;
            restoringHealth=true;
            try
            {
                bool notify=local!=null&&ReferenceEquals(local.ActiveHealthController,boostedController)&&boostedController.IsAlive;
                foreach(var snapshot in healthSnapshots)SetBoostedHealth(snapshot,snapshot.Maximum,notify);
            }
            catch(Exception error){Logger.LogWarning("Could not fully restore Demigod health: "+error.Message);}
            finally{healthSnapshots.Clear();boostedController=null;appliedHealthMultiplier=1;restoringHealth=false;}
        }
        private static void NormalizeStoredHealth(object __instance,Profile.HealthInfo __result)
        {
            var plugin=combatOwner;
            if(plugin==null||__result==null||__result.BodyParts==null||!ReferenceEquals(__instance,plugin.boostedController))return;
            foreach(var snapshot in plugin.healthSnapshots)
            {
                Profile.HealthInfo.BodyPartInfo part;
                if(!__result.BodyParts.TryGetValue(snapshot.Part,out part)||part.Health==null)continue;
                var value=part.Health;
                value.Current=CombatMath.RescaleHealth(value.Current,value.Maximum,snapshot.Maximum,snapshot.Minimum);
                value.Maximum=snapshot.Maximum;value.Minimum=snapshot.Minimum;
            }
        }
        private void ShutdownCombatTools()
        {
            RestoreDemigodHealth();
            if(combatHarmony!=null){combatHarmony.UnpatchSelf();combatHarmony=null;}
            if(combatOwner==this)combatOwner=null;
        }
    }
}
