using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private sealed class HeadAnchor
        {public object Skeleton;public Transform Bone;public bool Model;public float Retry;}
        private readonly Dictionary<Player,HeadAnchor> headAnchors=new Dictionary<Player,HeadAnchor>();
        private ConfigEntry<bool> rageLead,rageDrop;
        private ConfigEntry<float> rageLeadStrength,ragePredictionLimit;
        private Weapon predictionWeapon;
        private AmmoTemplate predictionAmmo;
        private float predictionSpeed,nextPredictionSpeed;
        private bool headFromModel;
        private bool StrictRageHead {get{return activeAimMode.Value==AimMode.Rage&&rageHeadOnly.Value;}}
        private void BindRageTracking()
        {
            rageLead=Setting("34 Rage ballistics","Movement prediction",false,"Aim ahead of the selected body point using target velocity and estimated bullet flight time. Optional and independent of strict head selection.");
            rageDrop=Setting("34 Rage ballistics","Bullet drop compensation",false,"Raise the aim solution for gravity during estimated bullet flight. Uses current ammunition and weapon speed. Analytic estimate: air drag, spread and zeroing are not fully modelled.");
            rageLeadStrength=Slider("34 Rage ballistics","Prediction strength",100,0,100,"Percentage of target velocity included in movement prediction.");
            ragePredictionLimit=Slider("34 Rage ballistics","Maximum flight prediction seconds",2,0.1f,5,"Maximum estimated flight time. If no valid bounded solution exists, aim at the current selected point without compensation.");
            foreach(ConfigEntryBase entry in new ConfigEntryBase[]{rageLead,rageDrop,rageLeadStrength,ragePredictionLimit})profileBuckets[entry]=7;
        }
        private Vector3 LiveHeadPoint(Player player)
        {
            var body=player.PlayerBody;
            var skeleton=body!=null?body.SkeletonRootJoint:null;
            HeadAnchor anchor;
            if(!headAnchors.TryGetValue(player,out anchor)||!ReferenceEquals(anchor.Skeleton,skeleton)||
                (anchor.Bone==null&&Time.unscaledTime>=anchor.Retry))
            {
                anchor=new HeadAnchor{Skeleton=skeleton,Retry=Time.unscaledTime+1};
                if(skeleton!=null&&skeleton.Bones!=null)
                    foreach(var pair in skeleton.Bones)
                        if(pair.Value!=null&&AimPointRules.IsHeadName(pair.Key))
                        {anchor.Bone=pair.Value;anchor.Model=true;break;}
                if(anchor.Bone==null&&player.PlayerBones!=null&&player.PlayerBones.Head!=null)
                    anchor.Bone=player.PlayerBones.Head.Original;
                headAnchors[player]=anchor;
            }
            headFromModel=anchor.Model;
            if(anchor.Bone==null||!anchor.Bone.gameObject.activeInHierarchy)return new Vector3(float.NaN,float.NaN,float.NaN);
            // Keep one anatomical source. Never alternate to a physics/collider centre mid-lock.
            return anchor.Bone.position;
        }
        private Vector3 RageAimSolution(Vector3 anatomical,out string status)
        {
            status="";
            if(activeAimMode.Value!=AimMode.Rage||(!rageLead.Value&&!rageDrop.Value)||locked==null)return anatomical;
            var firearm=local.HandsController as Player.FirearmController;
            var weapon=firearm!=null?firearm.Item:null;
            var ammo=weapon!=null?weapon.CurrentAmmoTemplate:null;
            if(weapon==null||ammo==null){status=" | prediction unavailable";return anatomical;}
            if(!ReferenceEquals(weapon,predictionWeapon)||!ReferenceEquals(ammo,predictionAmmo)||Time.unscaledTime>=nextPredictionSpeed)
            {
                predictionWeapon=weapon;predictionAmmo=ammo;nextPredictionSpeed=Time.unscaledTime+0.5f;
                predictionSpeed=weapon.TotalVelocity;
            }
            Vector3 origin=firearm.FireportPosition,velocity=rageLead.Value?locked.Velocity*(rageLeadStrength.Value/100f):Vector3.zero;
            Vector3 delta=anatomical-origin;
            float x,y,z,time;
            if(!PredictionMath.TryOffset(delta.x,delta.y,delta.z,velocity.x,velocity.y,velocity.z,predictionSpeed,
                rageDrop.Value?Mathf.Abs(Physics.gravity.y):0,ragePredictionLimit.Value,out x,out y,out z,out time))
            {status=" | prediction unavailable";return anatomical;}
            status=" | "+(rageLead.Value?"lead":"")+(rageLead.Value&&rageDrop.Value?" + ":"")+(rageDrop.Value?"drop":"")+" "+time.ToString("0.00")+"s";
            return anatomical+new Vector3(x,y,z);
        }
        private void ResetRageTracking()
        {headAnchors.Clear();predictionWeapon=null;predictionAmmo=null;predictionSpeed=nextPredictionSpeed=0;}
    }
}

