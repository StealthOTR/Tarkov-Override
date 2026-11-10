using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using EFT;
using EFT.Ballistics;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<bool> directHit,directVisible;
        private ConfigEntry<DirectBodyPart> directPart;
        private ConfigEntry<float> directRange,directFov;
        private bool directAvailable;
        [ThreadStatic] private static int directShotDepth;
        private string directIgnoredText;
        private bool directHasIgnored;
        private string[] directIgnored=new string[0];
        private static readonly FieldInfo playerHitboxes=AccessTools.Field(typeof(Player),"_hitColliders");
        private static readonly FieldInfo hitColliderId=AccessTools.Field(typeof(RaycastHit),"m_Collider");
        private sealed class HitboxCache {public object Body;public BodyPartCollider[] Parts;public int Category;}
        private readonly Dictionary<Player,HitboxCache> directColliders=new Dictionary<Player,HitboxCache>();
        private struct DirectCandidate {public Player Player;public BodyPartCollider Part;public Vector3 Point;public float Score;}
        private readonly DirectCandidate[] directCandidates=new DirectCandidate[4];
        private void BindDirectHit()
        {
            directHit=Setting("36 Direct hit","Direct hit mode",false,"Apply your own firearm projectiles directly to the chosen body part of an eligible AI. Independent of the aim master; does not move the view. Uses Rage target-type/group/exclusion filters plus its own FOV/range/visibility. No target means an ordinary shot. Resets off each raid.");
            directPart=Setting("36 Direct hit","Direct hit body part",DirectBodyPart.Head,"Body part for native hit/damage application. Hidden when Direct hit mode is off; the selection is remembered. Uses ammunition damage and the native body-hit route without physical travel/interception.");
            directRange=Slider("36 Direct hit","Direct hit range",300,5,1500,"Maximum enemy distance in metres.");
            directFov=Slider("36 Direct hit","Direct hit FOV",30,1,180,"Angular radius around your camera crosshair. Up to four nearest candidates are considered per projectile.");
            directVisible=Setting("36 Direct hit","Direct hit visible only",true,"Require a clear view to the selected body part. Disable to apply direct hits through cover. Does not disable target filters.");
            directHit.Value=false;
            foreach(ConfigEntryBase entry in new ConfigEntryBase[]{directHit,directPart,directRange,directFov,directVisible})profileBuckets[entry]=7;
            directAvailable=TryAdvancedHook("Direct Hit Mode",delegate
            {
                if(playerHitboxes==null||hitColliderId==null||hitColliderId.FieldType!=typeof(int))throw new MissingFieldException("Native hitbox/shot collider fields unavailable.");
                combatHarmony.Patch(RequireMethod(typeof(Player.FirearmController),"InitiateShot"),prefix:AdvancedHook("DirectShotScopePrefix"),finalizer:AdvancedHook("DirectShotScopeFinalizer"));
                combatHarmony.Patch(RequireMethod(typeof(BallisticsCalculator),"CreateShot"),postfix:AdvancedHook("DirectShotPostfix"));
            });
        }
        private static void DirectShotScopePrefix(Player.FirearmController __instance,out bool __state)
        {
            var owner=combatOwner;
            __state=owner!=null&&owner.directAvailable&&owner.directHit.Value&&owner.PracticeReady()&&ReferenceEquals(owner.local.HandsController,__instance);
            if(__state)directShotDepth++;
        }
        private static Exception DirectShotScopeFinalizer(Exception __exception,bool __state)
        {if(__state)directShotDepth=Math.Max(0,directShotDepth-1);return __exception;}
        private HitboxCache DirectHitboxes(Player player)
        {
            HitboxCache cache;
            if(!directColliders.TryGetValue(player,out cache)||!ReferenceEquals(cache.Body,player.PlayerBody))
            {
                cache=new HitboxCache{Body=player.PlayerBody,Parts=playerHitboxes.GetValue(player) as BodyPartCollider[],Category=Category(player)};directColliders[player]=cache;
            }
            return cache;
        }
        private BodyPartCollider DirectCollider(Player player,EBodyPart selected)
        {
            var cache=DirectHitboxes(player);
            if(cache.Parts==null)return null;
            BodyPartCollider found=null;
            foreach(var part in cache.Parts)
            {
                if(part==null||part is ArmorPlateCollider||part.BodyPartType!=selected||part.Collider==null)continue;
                if(found==null)found=part;
                if((selected==EBodyPart.Head&&part.BodyPartColliderType==EBodyPartColliderType.HeadCommon)||
                    (selected==EBodyPart.Chest&&part.BodyPartColliderType==EBodyPartColliderType.RibcageUp))return part;
            }
            return found;
        }
        private Vector3 DirectPoint(BodyPartCollider part)
        {
            var collider=part.Collider;var capsule=collider as CapsuleCollider;var sphere=collider as SphereCollider;var box=collider as BoxCollider;
            if(capsule!=null)return collider.transform.TransformPoint(capsule.center);
            if(sphere!=null)return collider.transform.TransformPoint(sphere.center);
            if(box!=null)return collider.transform.TransformPoint(box.center);
            return collider.transform.position;
        }
        private bool SelectDirectTarget(out DirectCandidate selected)
        {
            selected=default(DirectCandidate);int count=0;
            var rage=aimProfiles[AimMode.Rage];
            bool allowPmcs=((ConfigEntry<bool>)rage["pmcs"]).Value,allowScavs=((ConfigEntry<bool>)rage["scavs"]).Value,allowOther=((ConfigEntry<bool>)rage["others"]).Value;
            bool group=((ConfigEntry<bool>)rage["excludeGroup"]).Value;
            string exclusions=((ConfigEntry<string>)rage["ignoredTypes"]).Value;
            if(exclusions!=directIgnoredText){directIgnoredText=exclusions;directIgnored=exclusions.Split(',');directHasIgnored=!string.IsNullOrWhiteSpace(exclusions);}
            var part=(EBodyPart)NewCombatRules.BodyIndex(directPart.Value);
            foreach(var player in world.AllAlivePlayersList)
            {
                if(!ValidPlayer(player)||ignoredIds.Contains(player.ProfileId)||(group&&!string.IsNullOrEmpty(local.GroupId)&&local.GroupId==player.GroupId))continue;
                int type=DirectHitboxes(player).Category;
                if((type==0&&!allowPmcs)||(type==1&&!allowScavs)||(type==2&&!allowOther)||(directHasIgnored&&FeatureRules.MatchesTokens(directIgnored,EnemyType(player),player.Profile.Info.Settings.Role.ToString(),player.ProfileId)))continue;
                var collider=DirectCollider(player,part);if(collider==null)continue;
                Vector3 point=DirectPoint(collider),delta=point-camera.transform.position;
                if(!HeadPointResolver.Finite(point)||delta.sqrMagnitude<0.25f||delta.sqrMagnitude>directRange.Value*directRange.Value)continue;
                float angle=Vector3.Angle(camera.transform.forward,delta);if(angle>directFov.Value)continue;
                int index=count;while(index>0&&directCandidates[index-1].Score>angle)index--;
                if(index>=directCandidates.Length)continue;
                int last=Math.Min(count,directCandidates.Length-1);
                for(int move=last;move>index;move--)directCandidates[move]=directCandidates[move-1];
                directCandidates[index]=new DirectCandidate{Player=player,Part=collider,Point=point,Score=angle};count=Math.Min(count+1,directCandidates.Length);
            }
            for(int i=0;i<count;i++)if(!directVisible.Value||Visible(directCandidates[i].Player,directCandidates[i].Point)){selected=directCandidates[i];return true;}
            return false;
        }
        private static void DirectShotPostfix(Shot __result)
        {
            var owner=combatOwner;var shot=__result;
            if(directShotDepth<=0||owner==null||!owner.directAvailable||!owner.directHit.Value||!owner.PracticeReady()||owner.MenuOpen()||owner.local.IsInventoryOpened||owner.camera==null||shot==null||shot.Parent!=null||shot.Player==null||shot.PlayerProfileID!=owner.local.ProfileId||!ReferenceEquals(shot.Player.iPlayer,owner.local)||owner.local.HandsController==null||!ReferenceEquals(shot.Weapon,owner.local.HandsController.Item))return;
            var ammo=shot.Ammo as Ammo;if(ammo==null)return;
            try
            {
                if((ammo.ammoType!="bullet"&&ammo.ammoType!="buckshot")||ammo.GetTemplate<AmmoTemplate>().HasGrenaderComponent||ammo.GetItemComponent<ExplosiveAmmoComponent>()!=null)return;
                DirectCandidate target;if(!owner.SelectDirectTarget(out target))return;
                Vector3 direction=(target.Point-shot.StartPosition).normalized;
                var hit=new RaycastHit{point=target.Point,normal=-direction,distance=Vector3.Distance(shot.StartPosition,target.Point)};
                object boxed=hit;hitColliderId.SetValue(boxed,target.Part.Collider.GetInstanceID());hit=(RaycastHit)boxed;
                shot._hit=hit;
                // The native calculator owns this shot and still handles release/pooling.
                // Mark its collision processed before entering callbacks, then use the real
                // world hit delegate for damage, attribution, armour and client hit records.
                shot.Direction=direction;shot.IsForwardHit=true;
                shot.HittedBallisticCollider=target.Part;shot.ShotProcessed=true;shot._bulletState=Shot.EBulletState.StopHit;
                try{owner.world.ShotDelegate(shot);}
                finally{shot.AvoidAdditionalDamage=true;shot.TimeSinceShot=Math.Max(1,shot.AmmoLifeTime+1);}
            }
            catch(Exception error){owner.directHit.Value=false;LoggerWarning(owner,"Direct Hit disabled",error);}
            finally{for(int i=0;i<owner.directCandidates.Length;i++)owner.directCandidates[i]=default(DirectCandidate);}
        }
        private static void LoggerWarning(Plugin owner,string message,Exception error){owner.Logger.LogWarning(message+": "+error);}
    }
}
