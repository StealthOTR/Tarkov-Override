using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using EFT;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<EspDisplayPreset> espPreset;
        private ConfigEntry<EspBoxStyle> espBoxStyle;
        private ConfigEntry<bool> hiddenEspOnly,typeColours,emphasiseBosses,farEnemyMarkers,bodyTypeColours,lootIcons,lootDistanceDetail,expandLootClusters;
        private ConfigEntry<float> hiddenBrightness,farEnemyDistance,lootDetailDistance,lootFocus;
        private ConfigEntry<int> expandedLootNames;
        private readonly Dictionary<EspKind,ConfigEntry<bool>> enemyKindFilters=new Dictionary<EspKind,ConfigEntry<bool>>();
        private readonly Dictionary<EspKind,ConfigEntry<bool>> bodyKindFilters=new Dictionary<EspKind,ConfigEntry<bool>>();
        private readonly Dictionary<EspKind,ConfigEntry<Color>> enemyKindColours=new Dictionary<EspKind,ConfigEntry<Color>>();
        private readonly Dictionary<EspKind,ConfigEntry<Color>> bodyKindColours=new Dictionary<EspKind,ConfigEntry<Color>>();
        private readonly Dictionary<Player,EspRecord> espRecords=new Dictionary<Player,EspRecord>();
        private readonly Dictionary<string,EspKind> knownEnemyKinds=new Dictionary<string,EspKind>();
        private bool applyingEspPreset,updatingEspVisibility;
        private int visibilityIndex;
        private EspKind previewKind=EspKind.USEC;
        private bool previewVisible=true,previewBody;
        private sealed class EspRecord {public EspKind Kind;public bool Boss,Known,Visible;public float Checked=-100;}
        private void BindEspRevamp()
        {
            espPreset=Setting("16 ESP appearance","Display preset",EspDisplayPreset.Custom,"Simple: corner brackets and minimal details. Detailed: full boxes and skeleton. Custom: keep your own appearance settings. Presets do not change enemy/loot/body master switches, type filters or visibility filters.");
            espBoxStyle=Setting("16 ESP appearance","Box style",EspBoxStyle.CornerBrackets,"Choose a full box, corner brackets, or a small marker.");
            hiddenEspOnly=Setting("01 Enemy ESP","Hidden enemies only",false,"Only show enemies when no head/chest/pelvis sample has an unobstructed physics ray. This affects ESP only, not aimbot targeting. Disables Visible only.");
            typeColours=Setting("16 ESP appearance","Enemy type colours",true,"Use separate USEC, BEAR, scav, boss, guard, raider, rogue, cultist and same-group friendly colours.");
            hiddenBrightness=Slider("16 ESP appearance","Hidden colour brightness",0.5f,0.15f,1,"Brightness multiplier for occluded enemies; visible enemies use the full type colour.");
            emphasiseBosses=Setting("16 ESP appearance","Boss emphasis",true,"Draw an additional diamond above bosses and retain their names.");
            farEnemyMarkers=Setting("16 ESP appearance","Distant enemy markers",true,"Replace distant enemy boxes with a small marker and hide their health/skeleton to reduce clutter.");
            farEnemyDistance=Slider("16 ESP appearance","Enemy detail distance",150,10,1000,"Use reduced enemy detail beyond this distance, in metres.");
            bodyTypeColours=Setting("19 Body filters","Body type colours",true,"Use the independent body palette; turn off to use the original uniform corpse colour.");
            foreach(EspKind kind in Enum.GetValues(typeof(EspKind)))
            {
                enemyKindFilters[kind]=Setting("17 Enemy filters","Show "+kind,true,"Show living "+kind+" markers. Friendlies means a matching nonempty group ID, not every modded alliance.");
                bodyKindFilters[kind]=Setting("19 Body filters","Show "+kind+" bodies",true,"Show bodies of this type when known. Unknown body identities fall back to faction.");
                enemyKindColours[kind]=Setting("21 Enemy colours",kind+" colour",DefaultKindColour(kind),"Visible "+kind+" colour. Hidden markers use the brightness multiplier.");
                Color body=Color.Lerp(DefaultKindColour(kind),new Color(0.65f,0.65f,0.7f),0.5f);
                bodyKindColours[kind]=Setting("20 Body colours",kind+" body colour",body,"Body-only colour for "+kind+".");
            }
            lootIcons=Setting("18 Loot presentation","Category icons",true,"Draw small procedural ammo, key, medical, valuable, attachment and other category symbols.");
            lootDistanceDetail=Setting("18 Loot presentation","Distance-based loot detail",true,"Distant loose loot uses a small marker; names appear nearby or when looking toward it. Important items keep a compact tag.");
            lootDetailDistance=Slider("18 Loot presentation","Loot name distance",30,5,500,"Show full item names within this distance, in metres.");
            lootFocus=Slider("18 Loot presentation","Loot focus angle",5,0.5f,20,"Looking this close to an item/cluster reveals its details, in degrees from the crosshair.");
            expandLootClusters=Setting("18 Loot presentation","Look to expand clusters",true,"Show a compact item count, then reveal item names when looking toward a cluster.");
            expandedLootNames=Config.Bind("18 Loot presentation","Expanded names",6,new ConfigDescription("Maximum names shown in a focused cluster; remaining items appear as +N nearby.",new AcceptableValueRange<int>(1,12)));
            hiddenEspOnly.SettingChanged+=delegate
            {
                if(updatingEspVisibility)return; updatingEspVisibility=true;
                try {if(hiddenEspOnly.Value)visibleEsp.Value=false;} finally {updatingEspVisibility=false;}
            };
            visibleEsp.SettingChanged+=delegate
            {
                if(updatingEspVisibility)return; updatingEspVisibility=true;
                try {if(visibleEsp.Value)hiddenEspOnly.Value=false;} finally {updatingEspVisibility=false;}
            };
            if(hiddenEspOnly.Value)visibleEsp.Value=false;
            espPreset.SettingChanged+=delegate {if(!applyingEspPreset)ApplyEspPreset();};
            WatchEspAppearance(boxes);WatchEspAppearance(names);WatchEspAppearance(health);WatchEspAppearance(skeleton);WatchEspAppearance(espBoxStyle);
            WatchEspAppearance(clutter);WatchEspAppearance(lootIcons);
            WatchEspAppearance(lootDistanceDetail);WatchEspAppearance(expandLootClusters);WatchEspAppearance(lootDetailDistance);
            WatchEspAppearance(farEnemyMarkers);WatchEspAppearance(farEnemyDistance);WatchEspAppearance(typeColours);WatchEspAppearance(hiddenBrightness);
            WatchEspAppearance(emphasiseBosses);
            bodyTypeColours.SettingChanged+=InvalidateLoot;
            foreach(var entry in bodyKindFilters.Values)entry.SettingChanged+=InvalidateLoot;
            foreach(var entry in bodyKindColours.Values)entry.SettingChanged+=InvalidateLoot;
        }
        private void WatchEspAppearance<T>(ConfigEntry<T> entry)
        {entry.SettingChanged+=delegate {if(!applyingEspPreset)espPreset.Value=EspDisplayPreset.Custom;};}
        private void ApplyEspPreset()
        {
            if(espPreset.Value==EspDisplayPreset.Custom)return;
            bool detailed=espPreset.Value==EspDisplayPreset.Detailed,save=Config.SaveOnConfigSet;
            applyingEspPreset=true;Config.SaveOnConfigSet=false;
            try
            {
                boxes.Value=names.Value=health.Value=true;skeleton.Value=detailed;
                espBoxStyle.Value=detailed?EspBoxStyle.FullBox:EspBoxStyle.CornerBrackets;
                clutter.Value=lootIcons.Value=true;expandLootClusters.Value=!detailed;farEnemyMarkers.Value=!detailed;
                lootDistanceDetail.Value=!detailed;lootDetailDistance.Value=detailed?200:30;
                nextGroups=0;
            }
            finally {applyingEspPreset=false;Config.SaveOnConfigSet=save;if(save)Config.Save();}
        }
        private static Color DefaultKindColour(EspKind kind)
        {
            switch(kind)
            {
                case EspKind.USEC:return new Color(0.30f,0.65f,1);
                case EspKind.BEAR:return new Color(1,0.35f,0.30f);
                case EspKind.SCAV:return new Color(0.95f,0.95f,0.85f);
                case EspKind.Boss:return new Color(1,0.75f,0.15f);
                case EspKind.Guard:return new Color(1,0.50f,0.15f);
                case EspKind.Raider:return new Color(0.85f,0.45f,1);
                case EspKind.Rogue:return new Color(0.95f,0.35f,0.7f);
                case EspKind.Cultist:return new Color(0.60f,0.40f,0.9f);
                case EspKind.Bloodhound:return new Color(0.9f,0.25f,0.45f);
                case EspKind.Infected:return new Color(0.7f,0.85f,0.25f);
                case EspKind.Friendly:return new Color(0.25f,1,0.5f);
                default:return Color.cyan;
            }
        }
        private EspRecord EspInfo(Player p)
        {
            EspRecord record;
            if(!espRecords.TryGetValue(p,out record)) {record=new EspRecord();espRecords[p]=record;UpdateEspIdentity(p,record);}
            return record;
        }
        private void UpdateEspIdentity(Player p,EspRecord record)
        {
            string role=p.Profile.Info.Settings.Role.ToString();
            bool friendly=!string.IsNullOrEmpty(local.GroupId)&&local.GroupId==p.GroupId;
            record.Kind=EspRules.Kind(role,p.Side.ToString(),friendly);record.Boss=EspRules.BossRole(role);
            if(!string.IsNullOrEmpty(p.ProfileId))knownEnemyKinds[p.ProfileId]=record.Kind;
        }
        private void TickEspRevamp()
        {
            if(Time.unscaledTime>=nextLabels)foreach(var p in players)UpdateEspIdentity(p,EspInfo(p));
            if(!enemyEsp.Value||camera==null||players.Count==0)return;
            int checkedPlayers=0,visited=0;
            while(checkedPlayers<2&&visited++<players.Count)
            {
                if(visibilityIndex>=players.Count)visibilityIndex=0;
                var p=players[visibilityIndex++];var record=EspInfo(p);
                if(Time.unscaledTime-record.Checked<0.2f||!enemyKindFilters[record.Kind].Value||Vector3.Distance(local.Position,p.Position)>enemyRange.Value)continue;
                Vector2 projected;if(!ScreenPoint(BodyPoint(p,AimBone.Chest),out projected))continue;
                record.Visible=Visible(p,BodyPoint(p,AimBone.Head))||Visible(p,BodyPoint(p,AimBone.Chest))||Visible(p,BodyPoint(p,AimBone.Pelvis));
                record.Known=true;record.Checked=Time.unscaledTime;checkedPlayers++;
            }
        }
        private Color EspColour(EspKind kind,bool visible)
        {
            Color colour=typeColours.Value?enemyKindColours[kind].Value:enemyColour.Value;
            if(!visible){float shade=hiddenBrightness.Value;colour=new Color(colour.r*shade,colour.g*shade,colour.b*shade,colour.a);}
            return colour;
        }
        private void DrawEnemyRevamp()
        {
            foreach(var p in players)
            {
                if(!ValidPlayer(p))continue;var info=EspInfo(p);
                if(!enemyKindFilters[info.Kind].Value)continue;
                float distance=Vector3.Distance(local.Position,p.Position);if(distance>enemyRange.Value)continue;
                bool known=info.Known&&Time.unscaledTime-info.Checked<1;
                if(!EspRules.VisibilityAllowed(visibleEsp.Value,hiddenEspOnly.Value,known,info.Visible))continue;
                Vector2 top,bottom;
                if(!ScreenPoint(BodyPoint(p,AimBone.Head)+Vector3.up*0.18f,out top)||!ScreenPoint(p.Position,out bottom))continue;
                float h=Mathf.Max(12,bottom.y-top.y),w=h*0.45f;
                Rect bounds=new Rect(top.x-w*0.5f,top.y,w,h);Color colour=EspColour(info.Kind,known&&info.Visible);
                bool reduced=farEnemyMarkers.Value&&distance>farEnemyDistance.Value;
                if(boxes.Value)DrawEspShape(bounds,reduced?EspBoxStyle.Marker:espBoxStyle.Value,colour);
                if(health.Value&&!reduced)DrawEspHealth(bounds,TotalHealth(p,true));
                if(info.Boss&&emphasiseBosses.Value)DrawDiamond(new Vector2(top.x,top.y-30),6,colour);
                GUIContent label;
                if(names.Value&&enemyLabels.TryGetValue(p,out label))Label(new Vector2(top.x,top.y-14),label,colour);
                if(skeleton.Value&&!reduced)DrawSkeleton(p,colour);
            }
        }
        private static void DrawEspShape(Rect r,EspBoxStyle style,Color colour)
        {
            if(style==EspBoxStyle.FullBox){Outline(r,colour);return;}
            if(style==EspBoxStyle.Marker){DrawDiamond(new Vector2(r.center.x,r.y+4),4,colour);return;}
            float x=Mathf.Min(r.width*0.28f,20),y=Mathf.Min(r.height*0.20f,20);
            Paint(new Rect(r.x,r.y,x,1),colour);Paint(new Rect(r.x,r.y,1,y),colour);
            Paint(new Rect(r.xMax-x,r.y,x,1),colour);Paint(new Rect(r.xMax,r.y,1,y),colour);
            Paint(new Rect(r.x,r.yMax,x,1),colour);Paint(new Rect(r.x,r.yMax-y,1,y),colour);
            Paint(new Rect(r.xMax-x,r.yMax,x,1),colour);Paint(new Rect(r.xMax,r.yMax-y,1,y),colour);
        }
        private static void DrawDiamond(Vector2 point,float size,Color colour)
        {
            Line(point+new Vector2(0,-size),point+new Vector2(size,0),colour);Line(point+new Vector2(size,0),point+new Vector2(0,size),colour);
            Line(point+new Vector2(0,size),point+new Vector2(-size,0),colour);Line(point+new Vector2(-size,0),point+new Vector2(0,-size),colour);
        }
        private static void DrawEspHealth(Rect r,float fraction)
        {Paint(new Rect(r.x-5,r.y,3,r.height),Color.black);Paint(new Rect(r.x-5,r.y+r.height*(1-fraction),3,r.height*fraction),Color.Lerp(Color.red,Color.green,fraction));}
        private void DrawBodyMarker(LootMarker marker)
        {
            Vector2 screen;
            if(marker.Transform==null||!marker.Transform.gameObject.activeInHierarchy||!bodyKindFilters[marker.Kind].Value||Vector3.Distance(local.Position,marker.Transform.position)>corpseRange.Value||!ScreenPoint(marker.Transform.position,out screen))return;
            Color colour=bodyTypeColours.Value?bodyKindColours[marker.Kind].Value:corpseColour.Value;
            Vector2 symbol=screen+new Vector2(0,-20);DrawDiamond(symbol,6,colour);
            Line(symbol+new Vector2(-3,-3),symbol+new Vector2(3,3),colour);Line(symbol+new Vector2(-3,3),symbol+new Vector2(3,-3),colour);
            Label(screen,marker.Label,colour);
        }
        private void DrawEspPreview()
        {
            MenuSection("LIVE PREVIEW / SIMULATED");
            Rect controls=MenuRow(28);
            if(MenuButton(new Rect(controls.x,controls.y,controls.width*0.55f,27),previewKind.ToString()))previewKind=(EspKind)(((int)previewKind+1)%Enum.GetValues(typeof(EspKind)).Length);
            if(MenuButton(new Rect(controls.x+controls.width*0.58f,controls.y,controls.width*0.42f,27),previewVisible?"Visible":"Behind cover"))previewVisible=!previewVisible;
            if(MenuButton(MenuRow(27),previewBody?"Preview: Body ESP":"Preview: Enemy ESP"))previewBody=!previewBody;
            Rect panel=MenuRow(185);MenuBox(panel,new Color(0.075f,0.085f,0.10f));
            if(Event.current.type==EventType.Repaint)
            {
                Color old=GUI.color,colour=EspColour(previewKind,previewVisible);
                bool allowed=previewBody?bodyKindFilters[previewKind].Value:EspRules.VisibilityAllowed(visibleEsp.Value,hiddenEspOnly.Value,true,previewVisible)&&enemyKindFilters[previewKind].Value;
                if(allowed)
                {
                    Rect shape=new Rect(panel.center.x-30,panel.y+40,60,116);
                    if(previewBody)
                    {
                        colour=bodyTypeColours.Value?bodyKindColours[previewKind].Value:corpseColour.Value;
                        Vector2 point=shape.center;DrawDiamond(point,8,colour);
                        Line(point+new Vector2(-4,-4),point+new Vector2(4,4),colour);Line(point+new Vector2(-4,4),point+new Vector2(4,-4),colour);
                    }
                    else
                    {
                        if(boxes.Value)DrawEspShape(shape,espBoxStyle.Value,colour);if(health.Value)DrawEspHealth(shape,0.76f);
                        if(skeleton.Value)
                        {
                            Vector2 head=new Vector2(shape.center.x,shape.y+12),chest=head+new Vector2(0,28),pelvis=chest+new Vector2(0,35);
                            Line(head,chest,colour);Line(chest,pelvis,colour);Line(chest,chest+new Vector2(-22,23),colour);Line(chest,chest+new Vector2(22,23),colour);
                        }
                        if(previewKind==EspKind.Boss&&emphasiseBosses.Value)DrawDiamond(new Vector2(shape.center.x,shape.y-25),6,colour);
                    }
                    GUI.color=colour;if(names.Value||previewBody)GUI.Label(new Rect(panel.x+12,panel.y+12,panel.width-24,25),(previewBody?"[CORPSE] ":"")+(previewKind==EspKind.Boss?"Killa":previewKind.ToString())+" | 42m",menuText);
                    GUI.color=Color.white;GUI.Label(new Rect(panel.x+12,panel.yMax-22,panel.width-24,20),previewBody?"Simulated body marker":"Simulated enemy marker",menuSmall);
                }
                else GUI.Label(new Rect(panel.x+12,panel.y+55,panel.width-24,60),"This example is hidden by your filters.",menuNote);
                GUI.color=old;
            }
        }
    }
}
