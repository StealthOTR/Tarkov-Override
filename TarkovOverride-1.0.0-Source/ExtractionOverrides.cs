using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using CommonAssets.Scripts.Game;
using EFT;
using EFT.Interactive;
using HarmonyLib;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> alwaysExits;
        private bool exitsAvailable;
        private bool exitRestoring;
        private float nextExitRefresh;
        private Player exitPlayer;
        private ExfiltrationController exitController;
        private readonly Dictionary<ExfiltrationPoint,ExitSnapshot> exitSnapshots=new Dictionary<ExfiltrationPoint,ExitSnapshot>();
        private static readonly ExfiltrationPoint[] EmptyExitPoints=new ExfiltrationPoint[0];
        private static readonly ExfiltrationRequirement[] EmptyExitRequirements=new ExfiltrationRequirement[0];
        private ExfiltrationPoint[] availableExitPoints=EmptyExitPoints;
        private sealed class ExitSnapshot
        {
            public EExfiltrationStatus Status;public EExfiltrationType Type;
            public bool Active,RootActive;public GameObject Root;
            public float StartTime;
        }
        private void BindExtractionOverrides()
        {
            alwaysExits=Setting("31 Extraction overrides","Always-available extractions",false,"Make normal exits for your faction available regardless of spawn, payment, backpack, power or activation requirements. Keeps their normal extraction time. Secret/transit/scripted zones are excluded. Resets between raids.");
            alwaysExits.Value=false;
            exitsAvailable=TryAdvancedHook("Always-available extractions",delegate
            {
                advancedHarmony.Patch(AccessTools.Method(typeof(ExfiltrationPoint),"UnmetRequirements",new[]{typeof(Player)}),prefix:AdvancedHook("ExitRequirementsPrefix"));
                advancedHarmony.Patch(AccessTools.Method(typeof(ExfiltrationController),"EligiblePoints",new[]{typeof(Profile)}),prefix:AdvancedHook("EligibleExitPointsPrefix"));
                advancedHarmony.Patch(RequireMethod(typeof(ExfiltrationPoint),"set_Status"),prefix:AdvancedHook("AvailableExitStatusPrefix"));
                advancedHarmony.Patch(RequireMethod(typeof(ExfiltrationPoint),"InfiltrationMatch"),prefix:AdvancedHook("ExitInfiltrationPrefix"));
                advancedHarmony.Patch(RequireMethod(typeof(ScavExfiltrationPoint),"InfiltrationMatch"),prefix:AdvancedHook("ExitInfiltrationPrefix"));
                advancedHarmony.Patch(RequireMethod(typeof(ExfiltrationPoint),"OnPlayerEnter"),prefix:AdvancedHook("ExitRequirementEventPrefix"));
                advancedHarmony.Patch(RequireMethod(typeof(ExfiltrationPoint),"OnPlayerExit"),prefix:AdvancedHook("ExitRequirementEventPrefix"));
            });
        }
        private static bool ExitRequirementsPrefix(ExfiltrationPoint __instance,Player __0,ref IEnumerable<ExfiltrationRequirement> __result)
        {
            var owner=practiceOwner;
            if(owner==null||!owner.ExitOverrideActive(__instance)||__0!=owner.local)return true;
            __result=EmptyExitRequirements;return false;
        }
        private static bool ExitInfiltrationPrefix(ExfiltrationPoint __instance,Player __0,ref bool __result)
        {var owner=practiceOwner;if(owner==null||!owner.ExitOverrideActive(__instance)||__0!=owner.local)return true;__result=true;return false;}
        private static bool ExitRequirementEventPrefix(ExfiltrationPoint __instance,Player __0)
        {var owner=practiceOwner;return owner==null||(__0!=owner.local&&__0!=owner.exitPlayer)||(!owner.ExitOverrideActive(__instance)&&!(owner.exitRestoring&&owner.exitSnapshots.ContainsKey(__instance)));}
        private static bool EligibleExitPointsPrefix(Profile __0,ref ExfiltrationPoint[] __result)
        {
            var owner=practiceOwner;
            if(owner==null||!owner.exitsAvailable||owner.alwaysExits==null||!owner.alwaysExits.Value||!owner.PracticeReady()||__0==null||__0.Id!=owner.local.ProfileId||owner.availableExitPoints.Length==0)return true;
            __result=owner.availableExitPoints;return false;
        }
        private static void AvailableExitStatusPrefix(ExfiltrationPoint __instance,ref EExfiltrationStatus __0)
        {
            var owner=practiceOwner;
            if(owner!=null&&owner.ExitOverrideActive(__instance)&&__0!=EExfiltrationStatus.Countdown)__0=EExfiltrationStatus.RegularMode;
        }
        private bool ExitOverrideActive(ExfiltrationPoint point)
        {return exitsAvailable&&alwaysExits!=null&&alwaysExits.Value&&PracticeReady()&&point!=null&&exitSnapshots.ContainsKey(point);}
        private void TickExtractionOverrides()
        {
            if(!alwaysExits.Value||!exitsAvailable){if(exitSnapshots.Count>0)ResetExtractionOverrides();return;}
            if(Time.unscaledTime<nextExitRefresh)return;
            nextExitRefresh=Time.unscaledTime+0.5f;
            var controller=world.ExfiltrationController;if(controller==null)return;
            exitPlayer=local;exitController=controller;
            var points=new List<ExfiltrationPoint>();
            if(local.Profile.Side==EPlayerSide.Savage)
            {if(controller.ScavExfiltrationPoints!=null)points.AddRange(controller.ScavExfiltrationPoints);}
            else if(controller.ExfiltrationPoints!=null)points.AddRange(controller.ExfiltrationPoints);
            foreach(var point in points)
            {
                if(point==null||point.Settings==null)continue;
                if(!exitSnapshots.ContainsKey(point))
                {
                    var root=point._root;
                    exitSnapshots[point]=new ExitSnapshot{Status=point.Status,Type=point.Settings.ExfiltrationType,Active=point.gameObject.activeSelf,Root=root,RootActive=root!=null&&root.activeSelf,StartTime=point.ExfiltrationStartTime};
                }
                point.Settings.ExfiltrationType=EExfiltrationType.Individual;
                if(!point.gameObject.activeSelf||(point._root!=null&&!point._root.activeSelf))point.Enable();
                if(point.Status!=EExfiltrationStatus.RegularMode&&point.Status!=EExfiltrationStatus.Countdown)point.SetStatusLogged(EExfiltrationStatus.RegularMode,"Field Toolkit override");
                if(point.Entered.Contains(local)&&!point.HasMetRequirements(local.ProfileId))point.Proceed(local,false);
            }
            availableExitPoints=points.FindAll(delegate(ExfiltrationPoint point){return point!=null&&exitSnapshots.ContainsKey(point);}).ToArray();
        }
        private void ResetExtractionOverrides()
        {
            exitRestoring=true;
            if(alwaysExits!=null)alwaysExits.Value=false;
            if(exitSnapshots.Count>0)
            {
                try{if(exitController!=null&&exitPlayer!=null)exitController.CancelExtractionForPlayer(exitPlayer);}
                catch(Exception error){Logger.LogWarning("Extraction cancellation: "+error.Message);}
                foreach(var entry in exitSnapshots)
                {
                    var point=entry.Key;var snapshot=entry.Value;if(point==null)continue;
                    try
                    {
                        point.Settings.ExfiltrationType=snapshot.Type;point.SetStatusLogged(snapshot.Status,"Field Toolkit restore");
                        point.ExfiltrationStartTime=snapshot.StartTime;
                        if(snapshot.Root!=null)snapshot.Root.SetActive(snapshot.RootActive);
                        point.gameObject.SetActive(snapshot.Active);
                    }
                    catch(Exception error){Logger.LogWarning("Exit restoration: "+error.Message);}
                }
                exitSnapshots.Clear();
            }
            exitRestoring=false;availableExitPoints=EmptyExitPoints;nextExitRefresh=0;exitPlayer=null;exitController=null;
        }
    }
}
