using System;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private static Plugin practiceOwner;
        private ConfigEntry<bool> unlimitedStamina,preventMalfunctions;
        private Harmony practiceHarmony;
        private bool practiceMalfunctionAvailable;
        private string practiceMessage="Enter a raid to use practice tools.";
        private void BindPracticeTools()
        {
            unlimitedStamina=Setting("23 Player practice","Unlimited stamina",false,"Keep your body and arm stamina full while alive in a raid. Resets off between raids and on startup.");
            preventMalfunctions=Setting("23 Player practice","Prevent malfunctions",false,"Prevent new malfunctions in your currently held firearm. Does not affect bots or clear a malfunction that already happened. Resets off between raids.");
            unlimitedStamina.Value=preventMalfunctions.Value=false;
            practiceOwner=this;
            try
            {
                practiceHarmony=new Harmony("local.spt.practicemenu.playerpractice");
                var original=AccessTools.Method(typeof(Player.FirearmController),"GetMalfunctionState");
                var prefix=typeof(Plugin).GetMethod("PreventMalfunctionPrefix",BindingFlags.NonPublic|BindingFlags.Static);
                if(original==null||prefix==null)throw new MissingMethodException("Weapon malfunction method was not found.");
                practiceHarmony.Patch(original,new HarmonyMethod(prefix));
                practiceMalfunctionAvailable=true;
            }
            catch(Exception error)
            {
                practiceMalfunctionAvailable=false;
                if(practiceHarmony!=null)practiceHarmony.UnpatchSelf();
                Logger.LogWarning("Malfunction prevention unavailable: "+error.Message);
            }
        }
        private bool PracticeReady()
        {
            return world!=null&&local!=null&&local.IsYourPlayer&&local.ActiveHealthController!=null&&local.ActiveHealthController.IsAlive&&
                !string.IsNullOrEmpty(local.Location)&&!string.Equals(local.Location,"hideout",StringComparison.OrdinalIgnoreCase);
        }
        private void ResetPracticeTools()
        {
            ResetAdvancedPractice();
            ResetMiscTools();
            RestoreDemigodHealth();
            if(demigod!=null)demigod.Value=false;
            if(unlimitedStamina!=null)unlimitedStamina.Value=false;
            if(preventMalfunctions!=null)preventMalfunctions.Value=false;
            practiceMessage="Practice toggles reset. Enable them for this raid if needed.";
        }
        private void TickPracticeTools()
        {
            if(!PracticeReady())
            {
                if(unlimitedStamina.Value||preventMalfunctions.Value||(demigod!=null&&demigod.Value))ResetPracticeTools();
                return;
            }
            TickDemigodHealth();
            if(!unlimitedStamina.Value||local.Physical==null)return;
            try{FillPracticeStamina(local.Physical.Stamina);FillPracticeStamina(local.Physical.HandsStamina);}
            catch(Exception error){unlimitedStamina.Value=false;practiceMessage="Stamina refill failed; see log.";Logger.LogWarning(error);}
        }
        private static void FillPracticeStamina(Stamina stamina)
        {
            if(stamina==null||stamina.TotalCapacity==null)return;
            float maximum=stamina.TotalCapacity.Value;
            if(maximum<=0||float.IsNaN(maximum)||float.IsInfinity(maximum))return;
            stamina.Current=maximum;stamina.Overuse=0;
        }
        private static bool PreventMalfunctionPrefix(Player.FirearmController __instance,ref Weapon.EMalfunctionState __result,ref Weapon.EMalfunctionSource __6)
        {
            var plugin=practiceOwner;
            if(plugin==null||plugin.preventMalfunctions==null||!plugin.preventMalfunctions.Value||!plugin.PracticeReady()||
                !ReferenceEquals(__instance,plugin.local.HandsController))return true;
            __6=default(Weapon.EMalfunctionSource);__result=Weapon.EMalfunctionState.None;return false;
        }
        private void PracticeAction(Action action,string success)
        {
            if(!PracticeReady()){practiceMessage="Enter a raid with a living local player first.";return;}
            try{action();if(success!=null)practiceMessage=success;}
            catch(Exception error){practiceMessage="Action failed; see BepInEx log.";Logger.LogWarning(error);}
        }
        private void DrawPlayerRecovery()
        {
            MenuSection("ONE-SHOT ACTIONS");
            bool previous=GUI.enabled;GUI.enabled=PracticeReady();
            Rect heal=MenuRow();
            if(MenuButton(heal,"Restore health"))PracticeAction(delegate{local.ActiveHealthController.RestoreFullHealth();},"Health restoration requested.");
            MenuHover(heal,"Restore your local player's body-part health using the game's full-health restoration method. Does not revive a dead player or give ongoing damage immunity.");
            Rect stamina=MenuRow();
            if(MenuButton(stamina,"Refill stamina once"))PracticeAction(delegate{FillPracticeStamina(local.Physical.Stamina);FillPracticeStamina(local.Physical.HandsStamina);},"Body and arm stamina refilled.");
            MenuHover(stamina,"Refill body and arm stamina once without enabling the unlimited-stamina toggle.");
            Rect repair=MenuRow();if(MenuButton(repair,"Repair equipped gear"))RepairEquippedGear();
            MenuHover(repair,"Repair equipped weapons, armour, helmets and fitted armour plates. Ignores loose cargo in bags and rigs. Restore maximum durability also reverses permanent maximum-durability loss.");
            GUI.enabled=previous;
            MenuSection("STATUS");
            MenuNote(PracticeReady()?practiceMessage:"Enter a raid with a living player to use these controls.");
            if(PracticeReady())
            {
                var health=local.ActiveHealthController.GetBodyPartHealth(EBodyPart.Common,false);
                MenuNote("Health: "+health.Current.ToString("0.#")+" / "+health.Maximum.ToString("0.#")+(demigod!=null&&demigod.Value?" (Demigod)":""));
            }
            if(!practiceMalfunctionAvailable)MenuNote("Malfunction prevention could not be loaded. Check the BepInEx log.");
            if(!demigodAvailable)MenuNote("Demigod save normalization could not be loaded; health boosting is disabled.");
            if(!medicalAvailable||!weightAvailable||!rateAvailable)MenuNote("One or more equipment/supply hooks are unavailable. Their controls are disabled; see the BepInEx log.");
        }
        private void ShutdownPracticeTools()
        {
            ResetPracticeTools();
            if(practiceHarmony!=null){practiceHarmony.UnpatchSelf();practiceHarmony=null;}
            if(practiceOwner==this)practiceOwner=null;
        }
    }
}
