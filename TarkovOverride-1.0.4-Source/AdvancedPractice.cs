using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> infiniteMedical,noWeight,extendedInteraction,customFireRate,repairMaximum;
        private ConfigEntry<float> interactionDistance,fireRateRpm;
        private Harmony advancedHarmony;
        private bool medicalAvailable,weightAvailable,interactionAvailable,rateAvailable;
        private PhysicalBase weightPhysical;
        private bool weightWasEnabled;
        [ThreadStatic] private static int interactionScope;
        private static readonly FieldInfo shotInterval=AccessTools.Field(typeof(Player.FirearmController).GetNestedType("AutomaticFireOperation",BindingFlags.Public|BindingFlags.NonPublic),"_shotsTime");
        private static readonly FieldInfo operationWeapon=AccessTools.Field(typeof(Player.FirearmController).GetNestedType("FirearmOperation",BindingFlags.Public|BindingFlags.NonPublic),"Weapon");
        private readonly Dictionary<object,Weapon> adjustedRates=new Dictionary<object,Weapon>();

        private void BindAdvancedPractice()
        {
            infiniteMedical=Setting("29 Equipment & supplies","Infinite medical supplies",false,"Preserve charges and prevent consumption of medical items you use. Normal treatment time/effects remain. Does not refill already empty items. Resets between raids.");
            noWeight=Setting("29 Equipment & supplies","No weight penalties",false,"Calculate your physical weight penalties as if carrying zero weight. Inventory weight display and item weights stay accurate; equipment's own penalties remain.");
            customFireRate=Setting("29 Equipment & supplies","Adjustable fire rate",false,"Override the currently held local weapon's automatic cadence and semi-auto cooldown. Does not add fire modes or automatically pull the trigger.");
            fireRateRpm=Slider("29 Equipment & supplies","Fire rate RPM",900,60,2000,"Requested rounds per minute. Weapon animations, bolt cycling and malfunctions can still limit actual firing.");
            repairMaximum=Setting("29 Equipment & supplies","Restore maximum durability",true,"Repair equipped gear also restores lost maximum durability to its template maximum. Off repairs up to the item's existing maximum.");
            extendedInteraction=Setting("30 Interaction","Extended interaction range",false,"Extend local loot/door interaction rays while retaining the game's obstruction checks. Does not extend trader, vehicle or scripted interaction ranges.");
            interactionDistance=Slider("30 Interaction","Interaction distance",5,1,30,"Loot and door interaction distance in metres when Extended interaction range is enabled.");
            infiniteMedical.Value=noWeight.Value=customFireRate.Value=extendedInteraction.Value=false;
            advancedHarmony=new Harmony("local.spt.practicemenu.advanced");
            medicalAvailable=TryAdvancedHook("Infinite medical supplies",delegate
            {
                var type=typeof(EFT.HealthSystem.ActiveHealthController).GetNestedType("MedEffect",BindingFlags.Public|BindingFlags.NonPublic);
                foreach(string name in new[]{"RegularUpdate","Residue"})
                    advancedHarmony.Patch(RequireMethod(type,name),transpiler:AdvancedHook("MedicalResourceTranspiler"));
                advancedHarmony.Patch(RequireMethod(typeof(EFT.HealthSystem.MedEffectHelper),"RemoveItem"),prefix:AdvancedHook("PreserveMedicalItemPrefix"));
            });
            weightAvailable=TryAdvancedHook("No weight penalties",delegate
            {advancedHarmony.Patch(RequireMethod(typeof(Physical),"OnWeightUpdated"),transpiler:AdvancedHook("WeightTranspiler"));});
            interactionAvailable=TryAdvancedHook("Extended interaction range",delegate
            {
                advancedHarmony.Patch(RequireMethod(typeof(Player),"InteractionRaycast"),prefix:AdvancedHook("InteractionPrefix"),transpiler:AdvancedHook("InteractionTranspiler"),finalizer:AdvancedHook("InteractionFinalizer"));
                advancedHarmony.Patch(RequireMethod(typeof(GameWorld),"FindInteractable"),transpiler:AdvancedHook("InteractionTranspiler"));
            });
            rateAvailable=TryAdvancedHook("Adjustable fire rate",delegate
            {
                advancedHarmony.Patch(RequireMethod(typeof(Weapon),"get_FireRate"),postfix:AdvancedHook("FireRatePostfix"));
                advancedHarmony.Patch(RequireMethod(typeof(Weapon),"get_SingleFireRate"),postfix:AdvancedHook("FireRatePostfix"));
                if(shotInterval==null||operationWeapon==null)throw new MissingFieldException("Automatic fire interval");
                var automatic=shotInterval.DeclaringType;
                advancedHarmony.Patch(RequireMethod(automatic,"Update"),prefix:AdvancedHook("AutomaticRatePrefix"));
                advancedHarmony.Patch(RequireMethod(automatic,"Start"),postfix:AdvancedHook("AutomaticRatePrefix"));
            });
            BindRaidAwareness();
        }
        private static MethodInfo RequireMethod(Type type,string name)
        {var method=type==null?null:AccessTools.Method(type,name);if(method==null)throw new MissingMethodException(type==null?"Missing type":type.FullName,name);return method;}
        private static HarmonyMethod AdvancedHook(string name)
        {return new HarmonyMethod(AccessTools.Method(typeof(Plugin),name));}
        private bool TryAdvancedHook(string feature,Action install)
        {
            try{install();return true;}
            catch(Exception error){Logger.LogWarning(feature+" unavailable: "+error);return false;}
        }
        private static bool OwnMedical(Item item)
        {
            var owner=practiceOwner;
            return owner!=null&&owner.medicalAvailable&&owner.infiniteMedical!=null&&owner.infiniteMedical.Value&&owner.PracticeReady()&&
                item!=null&&ReferenceEquals(item.Owner,owner.local.InventoryController)&&item.GetItemComponent<MedKitComponent>()!=null;
        }
        private static void PreserveMedicalResource(MedKitComponent component,float value)
        {if(component!=null&&!OwnMedical(component.Item))component.HpResource=value;}
        private static bool PreserveMedicalItemPrefix(Item __0,ref bool __result)
        {if(!OwnMedical(__0))return true;__result=true;return false;}
        private static IEnumerable<CodeInstruction> MedicalResourceTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;var field=AccessTools.Field(typeof(MedKitComponent),"HpResource");
            var replacement=AccessTools.Method(typeof(Plugin),"PreserveMedicalResource");
            foreach(var code in instructions)
            {
                if(code.opcode==OpCodes.Stfld&&Equals(code.operand,field)){code.opcode=OpCodes.Call;code.operand=replacement;count++;}
                yield return code;
            }
            if(count==0)throw new InvalidOperationException("No medical resource writes found.");
        }
        private static float EffectivePhysicalWeight(float actual,PhysicalBase physical)
        {
            var owner=practiceOwner;
            return owner!=null&&owner.weightAvailable&&owner.noWeight!=null&&owner.noWeight.Value&&owner.PracticeReady()&&ReferenceEquals(owner.local.Physical,physical)?0:actual;
        }
        private static IEnumerable<CodeInstruction> WeightTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;
            foreach(var code in instructions)
            {
                yield return code;
                var method=code.operand as MethodInfo;
                if(code.opcode==OpCodes.Call&&method!=null&&method.Name=="op_Implicit"&&method.DeclaringType==typeof(Deferred<float>)&&method.ReturnType==typeof(float))
                {yield return new CodeInstruction(OpCodes.Ldarg_0);yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Plugin),"EffectivePhysicalWeight"));count++;}
            }
            if(count!=1)throw new InvalidOperationException("Physical weight input did not match.");
        }
        private static void InteractionPrefix(Player __instance,out int __state)
        {
            __state=interactionScope;var owner=practiceOwner;
            if(owner!=null&&owner.interactionAvailable&&owner.extendedInteraction.Value&&owner.PracticeReady()&&__instance==owner.local)interactionScope++;
        }
        private static Exception InteractionFinalizer(Exception __exception,int __state)
        {interactionScope=__state;return __exception;}
        private static float InteractionRayDistance(float original)
        {var owner=practiceOwner;return interactionScope>0&&owner!=null&&owner.interactionAvailable&&owner.extendedInteraction.Value?owner.interactionDistance.Value:original;}
        private static IEnumerable<CodeInstruction> InteractionTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            int count=0;
            foreach(var code in instructions)
            {
                yield return code;var field=code.operand as FieldInfo;
                if(code.opcode==OpCodes.Ldfld&&field!=null&&field.DeclaringType==typeof(EFTHardSettings)&&(field.Name=="LOOT_RAYCAST_DISTANCE"||field.Name=="DOOR_RAYCAST_DISTANCE"))
                {yield return new CodeInstruction(OpCodes.Call,AccessTools.Method(typeof(Plugin),"InteractionRayDistance"));count++;}
            }
            if(count==0)throw new InvalidOperationException("No interaction distance reads found.");
        }
        private bool IsHeldWeapon(Weapon weapon)
        {return PracticeReady()&&local.HandsController!=null&&ReferenceEquals(local.HandsController.Item,weapon);}
        private static void FireRatePostfix(Weapon __instance,ref int __result)
        {var owner=practiceOwner;if(owner!=null&&owner.rateAvailable&&owner.customFireRate.Value&&owner.IsHeldWeapon(__instance))__result=Mathf.RoundToInt(owner.fireRateRpm.Value);}
        private static void AutomaticRatePrefix(object __instance)
        {
            var owner=practiceOwner;if(owner==null||!owner.rateAvailable||shotInterval==null||operationWeapon==null)return;
            if(!owner.customFireRate.Value&&owner.adjustedRates.Count==0)return;
            var weapon=operationWeapon.GetValue(__instance) as Weapon;if(weapon==null)return;
            if(owner.customFireRate.Value&&owner.IsHeldWeapon(weapon))
            {
                owner.adjustedRates[__instance]=weapon;
                float heat=weapon.MalfState!=null?weapon.MalfState.OverheatFirerateMult:1;
                shotInterval.SetValue(__instance,AdvancedRules.ShotInterval(owner.fireRateRpm.Value,heat));
            }
            else if(owner.adjustedRates.Remove(__instance))
                shotInterval.SetValue(__instance,AdvancedRules.ShotInterval(weapon.Template.bFirerate,weapon.MalfState.OverheatFirerateMult));
        }
        private void TickAdvancedPractice()
        {
            if(!PracticeReady()){ResetAdvancedPractice();return;}
            try
            {
                if((noWeight.Value||weightWasEnabled||weightPhysical!=null)&&(weightPhysical!=local.Physical||weightWasEnabled!=noWeight.Value))
                {
                    if(weightPhysical!=null&&weightPhysical!=local.Physical)weightPhysical.OnWeightUpdated();
                    weightPhysical=local.Physical;weightWasEnabled=noWeight.Value;
                    if(weightPhysical!=null){weightPhysical.MarkWeightAsDirty();weightPhysical.OnWeightUpdated();}
                    if(!noWeight.Value)weightPhysical=null;
                }
            }
            catch(Exception error){noWeight.Value=false;weightPhysical=null;weightWasEnabled=false;weightAvailable=false;Logger.LogWarning("Weight override disabled: "+error);}
            TickRaidAwareness();
        }
        private void RepairEquippedGear()
        {
            PracticeAction(delegate
            {
                int repaired=0;var seen=new HashSet<string>();var pending=new Queue<Item>();
                var equipment=local.InventoryController.Inventory.Equipment;
                foreach(var name in new[]{EquipmentSlot.FirstPrimaryWeapon,EquipmentSlot.SecondPrimaryWeapon,EquipmentSlot.Holster,EquipmentSlot.ArmorVest,EquipmentSlot.TacticalVest,EquipmentSlot.Headwear,EquipmentSlot.FaceCover,EquipmentSlot.Eyewear,EquipmentSlot.Earpiece})
                {var slot=equipment.GetSlot(name);if(slot!=null&&slot.ContainedItem!=null)pending.Enqueue(slot.ContainedItem);}
                while(pending.Count>0)
                {
                    var item=pending.Dequeue();if(item==null||!seen.Add(item.Id))continue;
                    var repair=item.GetItemComponent<RepairableComponent>();
                    if(repair!=null)
                    {
                        float maximum=repairMaximum.Value?Mathf.Max(repair.MaxDurability,repair.TemplateDurability):repair.MaxDurability;
                        if(maximum>0&&(repair.Durability<maximum||repair.MaxDurability<maximum))
                        {repair.MaxDurability=maximum;repair.Durability=maximum;item.RaiseRefreshEvent(false,true);repaired++;}
                    }
                    // Traverse attachment/plate slots, never the cargo grids of a worn rig.
                    var compound=item as CompoundItem;
                    if(compound!=null&&compound.Slots!=null)foreach(var slot in compound.Slots)if(slot!=null&&slot.ContainedItem!=null)pending.Enqueue(slot.ContainedItem);
                }
                practiceMessage=repaired==0?"Equipped gear already repaired, or no repairable gear equipped.":"Repaired "+repaired+" equipped item(s), including fitted plates.";
            },null);
        }
        private void ResetAdvancedPractice()
        {
            if(infiniteMedical!=null)infiniteMedical.Value=false;if(noWeight!=null)noWeight.Value=false;
            if(customFireRate!=null)customFireRate.Value=false;if(extendedInteraction!=null)extendedInteraction.Value=false;
            if(weightPhysical!=null){try{weightPhysical.MarkWeightAsDirty();weightPhysical.OnWeightUpdated();}catch(Exception error){Logger.LogWarning("Weight restoration: "+error.Message);}weightPhysical=null;}
            weightWasEnabled=false;
            if(shotInterval!=null)foreach(var entry in adjustedRates)
                try{shotInterval.SetValue(entry.Key,AdvancedRules.ShotInterval(entry.Value.Template.bFirerate,entry.Value.MalfState.OverheatFirerateMult));}catch(Exception error){Logger.LogWarning("Fire-rate restoration: "+error.Message);}
            adjustedRates.Clear();ResetRaidAwareness();
        }
        private void ShutdownAdvancedPractice()
        {ResetAdvancedPractice();if(advancedHarmony!=null){advancedHarmony.UnpatchSelf();advancedHarmony=null;}ShutdownAwarenessAudio();}
    }
    public static class AdvancedRules
    {
        public static float ShotInterval(float rpm,float heat)
        {if(float.IsNaN(rpm)||float.IsInfinity(rpm)||rpm<=0)rpm=60;if(float.IsNaN(heat)||float.IsInfinity(heat)||heat<=0)heat=1;return 60f/(rpm*heat);}
    }
}
