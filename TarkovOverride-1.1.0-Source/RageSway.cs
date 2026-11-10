using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using EFT;
using EFT.Animations;
using HarmonyLib;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> rageNoAdsSway;
        private bool adsSwayAvailable;
        private void BindRageSway()
        {
            rageNoAdsSway=Setting("35 Rage weapon handling","No ADS movement sway",true,"Suppress your held weapon's procedural movement/turn sway and walking bob while actually aiming down sights with Rage Aim enabled. Works without a target lock. Does not affect bots, hip-fire or Legit Aim. Recoil is controlled separately by RCS.");
            profileBuckets[rageNoAdsSway]=7;
            try
            {
                var rewrite=new HarmonyMethod(AccessTools.Method(typeof(Plugin),"AdsSwayTranspiler"));
                combatHarmony.Patch(AccessTools.Method(typeof(MotionEffector),"Process"),transpiler:rewrite);
                combatHarmony.Patch(AccessTools.Method(typeof(WalkEffector),"Process"),transpiler:rewrite);
                adsSwayAvailable=true;
            }
            catch(Exception error){adsSwayAvailable=false;Logger.LogWarning("Rage ADS movement sway control unavailable: "+error);}
        }
        private static bool OwnAdsSwayEffector(object effector)
        {
            var owner=combatOwner;
            if(owner==null||!owner.adsSwayAvailable||owner.rageNoAdsSway==null||!owner.rageNoAdsSway.Value||
                !owner.aim.Value||owner.activeAimMode.Value!=AimMode.Rage||!owner.PracticeReady()||owner.MenuOpen()||owner.local.IsInventoryOpened)return false;
            var firearm=owner.local.HandsController as Player.FirearmController;
            if(firearm==null||!firearm.IsAiming)return false;
            var animation=owner.local.ProceduralWeaponAnimation;
            return animation!=null&&animation.IsAiming&&(ReferenceEquals(effector,animation.MotionReact)||ReferenceEquals(effector,animation.Walk));
        }
        private static float AdsSwayScalar(float original,object effector)
        {return OwnAdsSwayEffector(effector)?0:original;}
        private static Vector3 AdsSwayVector(Vector3 original,object effector)
        {return OwnAdsSwayEffector(effector)?Vector3.zero:original;}
        private static IEnumerable<CodeInstruction> AdsSwayTranspiler(IEnumerable<CodeInstruction> instructions,MethodBase original)
        {
            var codes=new List<CodeInstruction>(instructions);int scalars=0,vectors=0;
            bool motion=original.DeclaringType==typeof(MotionEffector);
            for(int i=0;i<codes.Count;i++)
            {
                var code=codes[i];yield return code;
                var field=code.operand as FieldInfo;
                if(code.opcode!=OpCodes.Ldfld||field==null)continue;
                bool scalar=(motion&&field.DeclaringType==typeof(MotionEffector)&&field.Name=="Intensity")||
                    (!motion&&field.DeclaringType==typeof(WalkEffector)&&field.Name=="Intensity");
                // Keep the overweight condition and animation clocks intact; filter only
                // the amplitude passed into ProcessRaw, not the stored weight or phase.
                var next=i+1<codes.Count?codes[i+1].operand as MethodInfo:null;
                scalar=scalar||(!motion&&field.DeclaringType==typeof(WalkEffector)&&field.Name=="Overweight"&&next!=null&&next.Name=="ProcessRaw");
                bool vector=motion&&field.DeclaringType==typeof(MotionEffector)&&field.Name=="SwayFactors";
                if(!scalar&&!vector)continue;
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Plugin),scalar?"AdsSwayScalar":"AdsSwayVector"));
                if(scalar)scalars++;else vectors++;
            }
            if(motion?(scalars!=1||vectors!=1):(scalars!=2||vectors!=0))throw new InvalidOperationException("ADS sway amplitude pattern did not match "+original.DeclaringType.Name);
        }
    }
}
