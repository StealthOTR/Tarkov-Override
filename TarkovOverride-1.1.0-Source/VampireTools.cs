using System;
using BepInEx.Configuration;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> vampire;
        private ConfigEntry<float> vampirePercent,vampireKillBonus,vampireCap;
        private bool vampireAvailable;
        private float pendingVampire;
        [ThreadStatic] private static int vampireDepth;
        private struct VampireState {public bool Entered,Top;public float Before;public Player Victim;}
        private void BindNewCombatTools()
        {
            vampire=Setting("37 Vampire mode","Vampire mode",false,"Restore your own health from actual health damage you deal to living AI. Works with ordinary shots and Direct Hit. Does not revive destroyed parts or remove injuries. Resets off each raid.");
            vampirePercent=Slider("37 Vampire mode","Damage healing (%)",20,0,200,"Health restored as a percentage of actual enemy health lost. Overkill and absorbed armour damage do not count.");
            vampireKillBonus=Slider("37 Vampire mode","Kill healing bonus",10,0,500,"Additional healing when your damage kills an enemy. Set zero for damage-only healing.");
            vampireCap=Slider("37 Vampire mode","Maximum healing per hit",100,1,500,"Cap on damage-based healing plus kill bonus from one top-level damage event. Multi-projectile ammunition can have several events.");
            vampire.Value=false;
            vampireAvailable=TryAdvancedHook("Vampire Mode",delegate
            {
                combatHarmony.Patch(RequireMethod(typeof(ActiveHealthController),"ApplyDamage"),prefix:AdvancedHook("VampireDamagePrefix"),postfix:AdvancedHook("VampireDamagePostfix"),finalizer:AdvancedHook("VampireDamageFinalizer"));
            });
            BindDirectHit();
        }
        private static void VampireDamagePrefix(ActiveHealthController __instance,DamageInfo __2,out VampireState __state)
        {
            __state=default(VampireState);var owner=combatOwner;
            try
            {
            if(owner==null||!owner.vampireAvailable||!owner.vampire.Value||!owner.PracticeReady()||__2.Player==null||!ReferenceEquals(__2.Player.iPlayer,owner.local))return;
            var victim=__instance.Player;
            if(victim==null||victim==owner.local||!victim.IsAI||!__instance.IsAlive)return;
            __state.Entered=true;__state.Top=vampireDepth++==0;__state.Victim=victim;
            if(__state.Top)__state.Before=__instance.GetBodyPartHealth(EBodyPart.Common,false).Current;
            }
            catch(Exception error){VampireFault(owner,error);}
        }
        private static void VampireDamagePostfix(ActiveHealthController __instance,VampireState __state)
        {
            var owner=combatOwner;
            try
            {
            if(!__state.Entered||!__state.Top||owner==null||!owner.vampire.Value||!owner.PracticeReady())return;
            float after=__instance.GetBodyPartHealth(EBodyPart.Common,false).Current;
            float healing=NewCombatRules.Healing(__state.Before,after,owner.vampirePercent.Value,owner.vampireKillBonus.Value,!__instance.IsAlive,owner.vampireCap.Value);
            if(NewCombatRules.Finite(healing))owner.pendingVampire=Math.Min(10000,owner.pendingVampire+healing);
            }
            catch(Exception error){VampireFault(owner,error);}
        }
        private static void VampireFault(Plugin owner,Exception error)
        {if(owner==null)return;owner.vampire.Value=false;owner.pendingVampire=0;owner.Logger.LogWarning("Vampire Mode disabled: "+error);}
        private static Exception VampireDamageFinalizer(Exception __exception,VampireState __state)
        {if(__state.Entered)vampireDepth=Math.Max(0,vampireDepth-1);return __exception;}
        private void TickVampire()
        {
            if(vampire==null||!vampire.Value||!PracticeReady()){pendingVampire=0;return;}
            float budget=pendingVampire;pendingVampire=0;
            if(!NewCombatRules.Finite(budget)||budget<=0)return;
            try
            {
                for(int pass=0;pass<7&&budget>0.001f;pass++)
                {
                    EBodyPart selected=EBodyPart.Common;float worst=0,missing=0;
                    for(int i=0;i<7;i++)
                    {
                        var part=(EBodyPart)i;var health=local.ActiveHealthController.GetBodyPartHealth(part,false);
                        if(!NewCombatRules.Finite(health.Current)||!NewCombatRules.Finite(health.Maximum)||health.Current<=0||health.Maximum<=0)continue;
                        float gap=health.Maximum-health.Current,ratio=gap/health.Maximum;
                        if(ratio>worst){selected=part;worst=ratio;missing=gap;}
                    }
                    if(selected==EBodyPart.Common||missing<=0)break;
                    float amount=Math.Min(budget,missing);
                    local.ActiveHealthController.ChangeHealth(selected,amount,DamageHelper.Existence);budget-=amount;
                }
            }
            catch(Exception error){vampire.Value=false;Logger.LogWarning("Vampire healing disabled: "+error);}
        }
        private void ResetNewCombatTools()
        {
            if(vampire!=null)vampire.Value=false;if(directHit!=null)directHit.Value=false;
            pendingVampire=0;directColliders.Clear();
            for(int i=0;i<directCandidates.Length;i++)directCandidates[i]=default(DirectCandidate);
        }
    }
}
