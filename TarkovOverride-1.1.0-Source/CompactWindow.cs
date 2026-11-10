using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx.Configuration;
using EFT;
using EFT.InputSystem;
using HarmonyLib;
using Newtonsoft.Json;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private static Plugin windowOwner;
        private Harmony windowHarmony;
        private bool compactOpen, compactAvailable, cursorCaptured, oldCursorVisible, oldAutoSave;
        private CursorLockMode oldCursorLock;
        private int inputReleaseFrame = -1;
        private ConfigEntry<float> windowScale;
        private Rect compactRect = new Rect(-1,-1,980,620);
        private int compactTab = 2;
        private readonly Vector2[] menuScroll = new Vector2[2];
        private readonly float[] menuHeights = { 450,450 };
        private float menuY,menuWidth,hoverSince;
        private Vector2 tooltipMouse,tooltipOrigin;
        private Rect tooltipViewport;
        private string expandedEntry,hoverHelp,lastHelp,menuMessage = "Hover a feature name for help.",keyError;
        private GUIStyle menuWindowStyle,menuTitle,menuText,menuSmall,menuNote,menuButton,menuField,menuTip,menuSlider,menuThumb;
        private Texture2D menuRound,menuCircle,menuTrack,menuDarkField;
        private readonly List<Texture2D> menuTextures = new List<Texture2D>();
        private readonly List<ConfigEntryBase>[] menuEntries = new List<ConfigEntryBase>[16];
        private readonly Dictionary<string,GUIContent> menuContents = new Dictionary<string,GUIContent>();
        private readonly Dictionary<string,string> menuEdits = new Dictionary<string,string>();
        private readonly Dictionary<string,string> keyDraft = new Dictionary<string,string>();
        private readonly string[] menuTabs = { "ESP","Loot","Legit Aim","Rage Aim","Player","Misc","Spawning","Keys" };
        private readonly string[] keyActions = { "OpenSettings","AimHold","ToggleEnemyESP","ToggleLootESP","ToggleAimbot","Panic","ReloadConfig","HeadAimHold","IgnoreTarget","ClearIgnoredTargets","ToggleNightVision","ToggleThermalVision","ToggleNoFlash","ToggleNoVisor","ToggleNoClip","NoClipForward","NoClipBack","NoClipLeft","NoClipRight","NoClipUp","NoClipDown","NoClipBoost","ToggleInfiniteMedical","ToggleNoWeight","ToggleInteractionRange","ToggleFireRate","ToggleEnemyAlerts","ToggleAvailableExtractions","RepairEquippedGear" };
        private static readonly Color Accent = new Color(0.25f,0.72f,0.96f);
        private static readonly Color Ink = new Color(0.91f,0.93f,0.95f);
        private static readonly Color Muted = new Color(0.61f,0.67f,0.72f);

        private void BindWindow()
        {
            windowScale=Config.Bind("10 Compact window","Scale",1f,new ConfigDescription("Window scale; automatically reduced to fit your screen.",new AcceptableValueRange<float>(0.75f,1.5f)));
            for(int i=0;i<menuEntries.Length;i++) menuEntries[i]=new List<ConfigEntryBase>();
            foreach(var pair in Config)
            {
                var entry=pair.Value; int bucket=MenuBucket(entry);
                if(bucket>=0) menuEntries[bucket].Add(entry);
            }
            menuEntries[6].Add(aim); // Same master switch, presented as an exclusive per-mode toggle.
            foreach(var list in menuEntries) list.Sort(delegate(ConfigEntryBase a,ConfigEntryBase b) { return MenuOrder(a).CompareTo(MenuOrder(b)); });
            foreach(int bucket in new[]{8,10,11})menuEntries[bucket].Sort(delegate(ConfigEntryBase a,ConfigEntryBase b)
            {int section=string.CompareOrdinal(a.Definition.Section,b.Definition.Section);return section!=0?section:MenuOrder(a).CompareTo(MenuOrder(b));});
            for(int column=0;column<2;column++)menuEntries[column].Sort(delegate(ConfigEntryBase a,ConfigEntryBase b)
            {int section=EspSectionRank(a.Definition.Section).CompareTo(EspSectionRank(b.Definition.Section));return section!=0?section:MenuOrder(a).CompareTo(MenuOrder(b));});
            windowOwner=this;
            try { windowHarmony=new Harmony("local.spt.practicemenu.compactwindow"); windowHarmony.PatchAll(typeof(Plugin).Assembly); compactAvailable=true; }
            catch(Exception error)
            {
                if(windowHarmony!=null) windowHarmony.UnpatchSelf();
                compactAvailable=false; Logger.LogWarning("Compact window input hooks unavailable; OpenSettings will use F12 settings. "+error.Message);
            }
        }
        private int MenuBucket(ConfigEntryBase entry)
        {
            string section=entry.Definition.Section,key=entry.Definition.Key;
            int profileBucket;
            if(profileBuckets.TryGetValue(entry,out profileBucket)) return profileBucket;
            if(section.StartsWith("01 ")||section.StartsWith("04 ")) return 0;
            if(section.StartsWith("02 ")) return key=="Colour"?3:2;
            if(section.StartsWith("06 ")) return key=="Preset"||key.StartsWith("Category - ")?2:3;
            if(section.StartsWith("03 ")) return key=="Enabled"?4:-1;
            if(section.StartsWith("09 ")) return -1;
            if(section.StartsWith("07 ")||section.StartsWith("19 ")||section.StartsWith("20 ")) return 1;
            if(section.StartsWith("10 ")||section.StartsWith("11 ")) return 15;
            if(section.StartsWith("23 "))return 8;
            if(section.StartsWith("26 ")||section.StartsWith("27 "))return 10;
            if(section.StartsWith("29 "))return 8;
            if(section.StartsWith("37 "))return 8;
            if(section.StartsWith("30 "))return 10;
            if(section.StartsWith("31 "))return -1; // Drawn beside the extraction action.
            if(section.StartsWith("32 "))return 0;
            if(section.StartsWith("16 ")||section.StartsWith("17 ")||section.StartsWith("21 "))return 0;
            if(section.StartsWith("18 "))return 3;
            return -1; // Informational F12 strings are replaced by the Keys page.
        }
        private int MenuOrder(ConfigEntryBase entry)
        {
            if(entry==directHit)return 800;if(entry==directPart)return 801;if(entry==directRange)return 802;if(entry==directFov)return 803;if(entry==directVisible)return 804;
            if(entry==vampire)return 0;if(entry==vampirePercent)return 1;if(entry==vampireKillBonus)return 2;if(entry==vampireCap)return 3;
            if(entry==rageLead)return 900;if(entry==rageDrop)return 901;if(entry==rageLeadStrength)return 902;if(entry==ragePredictionLimit)return 903;
            if(entry==rageNoAdsSway)return 950;
            ConfigEntryBase[][] advancedOrder={new ConfigEntryBase[]{infiniteMedical,noWeight,customFireRate,fireRateRpm,repairMaximum},new ConfigEntryBase[]{nightVision,thermalVision,noFlash,noVisor},new ConfigEntryBase[]{noClip,clipSpeed,clipBoost},new ConfigEntryBase[]{extendedInteraction,interactionDistance},new ConfigEntryBase[]{nearbyAlerts,alertRadius,alertSound,alertCooldown,alertVolume}};
            foreach(var group in advancedOrder){int position=Array.IndexOf(group,entry);if(position>=0)return position;}
            int recoilOrder;if(rcsOrder.TryGetValue(entry,out recoilOrder))return 1000+recoilOrder;
            if(entry==rageSnap) return -1;
            if(entry==rageHeadOnly)return 3;
            if(entry==espPreset)return -2;
            string[] order={"Enabled","Activation","Target bone","Target priority","Preset","Name filter","FOV radius degrees","Maximum distance","Visibility check","Aim point fallback","Require right mouse held","Exclude same group","Target PMCs","Target ordinary scavs","Target bosses and other AI","Smoothing seconds","Maximum turn degrees per second","Target switch delay","Target switch seconds","Limit aim correction","Maximum correction degrees","Keep current target","Ignore enemies","Bounding boxes","Names and distance","Health bars","Upper body skeleton","Visible only","Refresh seconds","Maximum labels","Container markers","Wishlist highlighting","Quest highlighting","Important items bypass filters","Wishlist colour","Quest colour","Colour","Group nearby labels","Grouping pixels","Names per group","Weapon","Armour","Helmet","Show FOV circle","Show selected target","Show aim status","Text size","Scale"};
            int index=Array.IndexOf(order,entry.Definition.Key); return index<0?100:index;
        }
        private void UpdateWindowInput()
        {
            if(compactOpen&&ManagerOpen())
            { managerVisible.SetValue(manager,false,null); SetCompactOpen(false); managerVisible.SetValue(manager,true,null); }
            if(Down("OpenSettings"))
            {
                if(compactAvailable) SetCompactOpen(!compactOpen);
                else if(manager!=null&&managerVisible!=null) managerVisible.SetValue(manager,!ManagerOpen(),null);
            }
            if(compactOpen&&(Input.GetKeyDown(KeyCode.Escape)||!Application.isFocused)) SetCompactOpen(false);
        }
        private void SetCompactOpen(bool value)
        {
            if(value==compactOpen) return;
            if(value)
            {
                if(ManagerOpen()) managerVisible.SetValue(manager,false,null);
                oldCursorLock=Cursor.lockState; oldCursorVisible=Cursor.visible; cursorCaptured=true;
                oldAutoSave=Config.SaveOnConfigSet; Config.SaveOnConfigSet=false; compactOpen=true;
                locked=null; hasAimPoint=false; switchAt=-1;
                if(local!=null)
                {
                    try
                    {
                        var firearm=local.HandsController as Player.FirearmController;
                        if(firearm!=null) { firearm.SetTriggerPressed(false); firearm.SetAim(false); }
                        local.EnableSprint(false);
                    }
                    catch(Exception error) {Logger.LogWarning("Could not reset a held action while opening the window: "+error.Message);}
                }
                RefreshKeyDraft(); MaintainWindowCursor();
                lastHelp=null;hoverHelp=null;
            }
            else
            {
                compactOpen=false; inputReleaseFrame=Time.frameCount; expandedEntry=null;
                GUIUtility.hotControl=0; GUIUtility.keyboardControl=0;
                if(cursorCaptured) { Cursor.lockState=oldCursorLock; Cursor.visible=oldCursorVisible; cursorCaptured=false; }
                Config.SaveOnConfigSet=oldAutoSave;
                try { Config.Save(); } catch(Exception error) { Logger.LogWarning("Could not save settings: "+error.Message); }
            }
        }
        private void MaintainWindowCursor()
        { if(compactOpen) { Cursor.lockState=CursorLockMode.None; Cursor.visible=true; } }
        private void ShutdownWindow()
        {
            if(compactOpen) SetCompactOpen(false);
            if(windowHarmony!=null) { windowHarmony.UnpatchSelf(); windowHarmony=null; }
            if(windowOwner==this) windowOwner=null;
        }
        private void OnDestroy()
        { ShutdownWindow(); foreach(var texture in menuTextures) if(texture!=null) UnityEngine.Object.Destroy(texture); menuTextures.Clear(); }
        private static bool BlockWindowInput(GamePlayerOwner owner)
        { return windowOwner!=null&&(windowOwner.compactOpen||Time.frameCount==windowOwner.inputReleaseFrame)&&owner.Player!=null&&owner.Player.IsYourPlayer; }
        [HarmonyPatch(typeof(GamePlayerOwner),"CanTranslateCommand")]
        private static class CompactCommandPatch
        {
            private static bool Prefix(GamePlayerOwner __instance,ECommand __0,ref bool __result)
            {
                if(windowOwner!=null&&windowOwner.clipController!=null&&__instance.Player==windowOwner.local)
                {
                    switch(__0)
                    {
                        case ECommand.Jump:case ECommand.ToggleSprinting:case ECommand.ToggleProne:
                        case ECommand.NextWalkPose:case ECommand.PreviousWalkPose:case ECommand.ToggleStepLeft:
                        case ECommand.ToggleStepRight:case ECommand.ToggleWalk:case ECommand.RestorePose:
                            __result=false;return false;
                    }
                }
                if(!BlockWindowInput(__instance)) return true;
                // Let releases through so held actions cannot remain stuck after closing.
                switch(__0)
                {
                    case ECommand.EndShooting:case ECommand.EndAlternativeShooting:case ECommand.EndSprinting:
                    case ECommand.EndBreathing:case ECommand.EndWalk:case ECommand.EndLeanLeft:case ECommand.EndLeanRight:
                    case ECommand.EndAnimBlindFireAbove:case ECommand.EndAnimBlindFireRight:case ECommand.BlindShootEnd:
                    case ECommand.EndInteracting:case ECommand.EndSpecialInteracting:case ECommand.ReleaseTactical:case ECommand.StopTalk:return true;
                }
                __result=false; return false;
            }
        }
        [HarmonyPatch(typeof(GamePlayerOwner),"TranslateAxes")]
        private static class CompactAxesPatch
        {
            private static bool Prefix(GamePlayerOwner __instance,ref float[] __0)
            {
                if(BlockWindowInput(__instance)){if(__0!=null)Array.Clear(__0,0,__0.Length);return false;}
                if(windowOwner!=null&&windowOwner.clipController!=null&&__instance.Player==windowOwner.local&&__0!=null)
                {if(__0.Length>0)__0[0]=0;if(__0.Length>1)__0[1]=0;}
                return true;
            }
        }
        private Texture2D MenuTexture(int size,float radius,Color? fill=null)
        {
            var texture=new Texture2D(size,size,TextureFormat.RGBA32,false); texture.hideFlags=HideFlags.HideAndDontSave;
            var pixels=new Color[size*size]; float half=(size-1)*0.5f;
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float dx=Mathf.Max(Mathf.Abs(x-half)-(half-radius),0),dy=Mathf.Max(Mathf.Abs(y-half)-(half-radius),0);
                Color colour=fill??Color.white;
                pixels[y*size+x]=new Color(colour.r,colour.g,colour.b,colour.a*Mathf.Clamp01(radius-Mathf.Sqrt(dx*dx+dy*dy)+0.5f));
            }
            texture.SetPixels(pixels); texture.Apply(false,true); menuTextures.Add(texture); return texture;
        }
        private GUIStyle MenuLabelStyle(int size,Color colour,bool bold=false)
        {
            var style=new GUIStyle(GUI.skin.label) {fontSize=size,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,richText=false,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(0,0,0,0)};
            style.normal.textColor=colour; return style;
        }
        private void CreateMenuStyles()
        {
            if(menuWindowStyle!=null) return;
            menuRound=MenuTexture(24,7); menuCircle=MenuTexture(16,8); menuTrack=MenuTexture(8,3);
            menuDarkField=MenuTexture(24,7,new Color(0.055f,0.065f,0.08f));
            menuWindowStyle=new GUIStyle {border=new RectOffset(7,7,7,7)}; menuWindowStyle.normal.background=menuRound;
            menuTitle=MenuLabelStyle(21,Ink,true); menuText=MenuLabelStyle(14,Ink); menuSmall=MenuLabelStyle(12,Muted);
            menuNote=MenuLabelStyle(13,Muted); menuNote.wordWrap=true;
            menuButton=new GUIStyle(GUI.skin.button) {fontSize=14,richText=false,border=new RectOffset(7,7,7,7)};
            foreach(var state in new[]{menuButton.normal,menuButton.hover,menuButton.active,menuButton.focused,menuButton.onNormal,menuButton.onHover,menuButton.onActive,menuButton.onFocused})
            {state.background=menuRound; state.textColor=Ink;}
            menuField=new GUIStyle(GUI.skin.textField) {fontSize=13,richText=false,border=new RectOffset(7,7,7,7),padding=new RectOffset(7,7,4,4),alignment=TextAnchor.MiddleLeft};
            foreach(var state in new[]{menuField.normal,menuField.hover,menuField.active,menuField.focused,menuField.onNormal,menuField.onHover,menuField.onActive,menuField.onFocused})
            {state.background=menuDarkField; state.textColor=Ink;}
            menuTip=MenuLabelStyle(13,Ink); menuTip.wordWrap=true; menuTip.padding=new RectOffset(12,12,9,9); menuTip.border=new RectOffset(7,7,7,7); menuTip.normal.background=menuRound;
            menuSlider=new GUIStyle(GUI.skin.horizontalSlider) {fixedHeight=5,border=new RectOffset(3,3,2,2)}; menuSlider.normal.background=menuTrack;
            menuThumb=new GUIStyle(GUI.skin.horizontalSliderThumb) {fixedWidth=13,fixedHeight=13,margin=new RectOffset(0,0,-4,0)};
            menuThumb.normal.background=menuCircle; menuThumb.hover.background=menuCircle; menuThumb.active.background=menuCircle;
        }
        private void MenuBox(Rect rect,Color colour,Texture2D texture=null)
        {
            if(Event.current.type!=EventType.Repaint) return;
            Color old=GUI.color,background=GUI.backgroundColor; GUI.color=colour; GUI.backgroundColor=Color.white;
            if(texture==null) menuWindowStyle.Draw(rect,GUIContent.none,false,false,false,false);
            else GUI.DrawTexture(rect,texture);
            GUI.color=old; GUI.backgroundColor=background;
        }
        private bool MenuButton(Rect rect,string label)
        {
            Color old=GUI.backgroundColor; GUI.backgroundColor=new Color(0.22f,0.25f,0.29f);
            bool clicked=GUI.Button(rect,label,menuButton); GUI.backgroundColor=old; return clicked;
        }
        private void DrawCompactWindow()
        {
            if(!compactOpen) return;
            Matrix4x4 matrix=GUI.matrix; Color colour=GUI.color,background=GUI.backgroundColor; int depth=GUI.depth;
            try
            {
                CreateMenuStyles(); float scale=Mathf.Max(0.2f,Mathf.Min(windowScale.Value,Mathf.Min(Screen.width/1012f,Screen.height/652f)));
                float width=Screen.width/scale,height=Screen.height/scale;
                if(compactRect.x<0) {compactRect.x=(width-compactRect.width)/2; compactRect.y=(height-compactRect.height)/2;}
                compactRect.x=Mathf.Clamp(compactRect.x,0,Mathf.Max(0,width-compactRect.width)); compactRect.y=Mathf.Clamp(compactRect.y,0,Mathf.Max(0,height-compactRect.height));
                GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,Vector3.one*scale); GUI.depth=-10000;
                if(Event.current.type==EventType.Repaint)hoverHelp=null;
                tooltipMouse=new Vector2(Input.mousePosition.x/scale,(Screen.height-Input.mousePosition.y)/scale);
                GUI.backgroundColor=new Color(0.105f,0.12f,0.14f,0.98f); GUI.color=Color.white;
                compactRect=GUI.Window(GetInstanceID()+9137,compactRect,DrawCompactContents,"",menuWindowStyle);
                // Do not replace explicit hover hits with GUI.tooltip: Unity may clear that value
                // after GUI.Window returns. Only repaint events advance the tooltip state.
            }
            catch(Exception error) {Logger.LogError("Compact window disabled after UI error; F12 remains available: "+error); SetCompactOpen(false); compactAvailable=false;}
            finally {GUI.matrix=matrix; GUI.color=colour; GUI.backgroundColor=background; GUI.depth=depth;}
        }
        private void DrawCompactContents(int id)
        {
            for(int x=0;x<2;x++) for(int y=0;y<2;y++) MenuBox(new Rect(22+x*11,21+y*11,8,8),Accent,Texture2D.whiteTexture);
            GUI.Label(new Rect(54,7,420,31),"Tarkov Override",menuTitle);
            GUI.Label(new Rect(54,38,420,20),"Author: StealthOTR",menuSmall);
            GUI.Label(new Rect(761,18,145,26),"1.1.0 / COMPACT",menuSmall);
            if(MenuButton(new Rect(926,15,32,30),"×")) SetCompactOpen(false);
            for(int i=0;i<menuTabs.Length;i++)
            {
                float step=936f/menuTabs.Length;
                Rect tab=new Rect(22+i*step,68,step-8,35);
                if(MenuButton(tab,menuTabs[i])) {compactTab=i; menuScroll[0]=menuScroll[1]=Vector2.zero; expandedEntry=null; GUIUtility.keyboardControl=0;}
                if(compactTab==i) MenuBox(new Rect(tab.x+15,105,tab.width-30,3),Accent,Texture2D.whiteTexture);
            }
            DrawMenuColumn(new Rect(20,120,462,435),0); DrawMenuColumn(new Rect(498,120,462,435),1);
            MenuBox(new Rect(20,571,940,1),new Color(0.28f,0.31f,0.35f),Texture2D.whiteTexture);
            GUI.Label(new Rect(24,579,510,28),"SPT 4.1.6 | Aim: "+(aim.Value?activeAimMode.Value.ToString():"Off")+" | "+menuMessage,menuSmall);
            GUI.Label(new Rect(741,580,219,28),KeyText("OpenSettings")+" / Esc  Close",menuSmall);
            GUI.DragWindow(new Rect(0,0,914,60));
            // Render last inside this window's layer, after scroll clips have been closed.
            if(Event.current.type==EventType.Repaint)DrawMenuTooltip(compactRect.width,compactRect.height);
        }
        private string MenuColumnTitle(int column)
        {
            string[] titles={"Enemy ESP","Body ESP & preview","Loot filters","Highlights & clutter","Legit targeting","Gentle response","Rage targeting","Fast response","Practice toggles","Recovery actions","Vision & movement","Doors & extraction","Item catalog","Spawn controls","Hotkeys","Movement keys & window"};
            return titles[compactTab*2+column];
        }
        private void DrawMenuColumn(Rect rect,int column)
        {
            MenuBox(rect,new Color(0.13f,0.145f,0.165f)); GUI.Label(new Rect(rect.x+16,rect.y+8,rect.width-32,28),MenuColumnTitle(column),menuTitle);
            Rect viewport=new Rect(rect.x+4,rect.y+44,rect.width-8,rect.height-50);
            menuScroll[column]=GUI.BeginScrollView(viewport,menuScroll[column],new Rect(0,0,viewport.width-18,Mathf.Max(menuHeights[column],viewport.height)),false,false);
            tooltipViewport=new Rect(compactRect.x+viewport.x,compactRect.y+viewport.y,viewport.width,viewport.height);
            tooltipOrigin=compactRect.position+viewport.position-menuScroll[column];
            menuY=4; menuWidth=viewport.width-38;
            try
            {
                if(compactTab==7) DrawKeysColumn(column);
                if(compactTab==6)DrawItemSpawning(column);
                if(compactTab==5&&column==1)DrawMiscActions();
                if(compactTab==4&&column==1)DrawPlayerRecovery();
                if(compactTab==0&&column==1)DrawEspPreview();
                string lastSection=null;bool rcsHeader=false,predictionHeader=false;
                foreach(var entry in menuEntries[compactTab*2+column])
                {
                    if(entry==directHit)MenuSection("DIRECT HIT");
                    if(!directHit.Value&&(entry==directPart||entry==directRange||entry==directFov||entry==directVisible))continue;
                    if(!vampire.Value&&(entry==vampirePercent||entry==vampireKillBonus||entry==vampireCap))continue;
                    if(entry==rageLead&&!predictionHeader){MenuSection("MOVEMENT & BULLET DROP");predictionHeader=true;}
                    if(entry==rageNoAdsSway)MenuSection("WEAPON STABILITY");
                    if(AimTab&&rcsOrder.ContainsKey(entry)&&!rcsHeader){MenuSection("RCS / RECOIL CONTROL");rcsHeader=true;}
                    if(entry.Definition.Section.StartsWith("21 ")&&!typeColours.Value)continue;
                    if(entry.Definition.Section.StartsWith("20 ")&&!bodyTypeColours.Value)continue;
                    if(entry==enemyColour&&typeColours.Value)continue;
                    if(entry==corpseColour&&bodyTypeColours.Value)continue;
                    if(compactTab==0&&entry.Definition.Section!=lastSection){lastSection=entry.Definition.Section;MenuSection(EspSectionTitle(lastSection));}
                    if((compactTab==4||compactTab==5)&&entry.Definition.Section!=lastSection){lastSection=entry.Definition.Section;MenuSection(lastSection.Substring(3).ToUpperInvariant());}
                    if(entry.Definition.Key.StartsWith("Category - ")&&lootPreset.Value!=LootPreset.CustomCategories) continue;
                    if(ProfileField(entry,"adsOnly")&&WindowAimValue<AimActivation>("activation")!=AimActivation.HoldJsonHotkey) continue;
                    if(ProfileField(entry,"switchSeconds")&&!WindowAimValue<bool>("switchDelay")) continue;
                    if(ProfileField(entry,"correctionAngle")&&!WindowAimValue<bool>("correction")) continue;
                    if(compactTab==3&&rageSnap.Value&&(ProfileField(entry,"smoothing")||ProfileField(entry,"maxTurn"))) continue;
                    bool previousEnabled=GUI.enabled;
                    if(compactTab==3&&rageHeadOnly.Value&&(ProfileField(entry,"bone")||ProfileField(entry,"fallback")))GUI.enabled=false;
                    if(compactTab==4&&!PracticeReady())GUI.enabled=false;
                    if(compactTab==5&&!PracticeReady())GUI.enabled=false;
                    if(entry.Definition.Section.StartsWith("32 ")&&!PracticeReady())GUI.enabled=false;
                    if(entry==preventMalfunctions&&!practiceMalfunctionAvailable)GUI.enabled=false;
                    if(entry==demigod&&!demigodAvailable)GUI.enabled=false;
                    if(rcsOrder.ContainsKey(entry)&&!rcsAvailable)GUI.enabled=false;
                    if(entry==rageNoAdsSway&&!adsSwayAvailable)GUI.enabled=false;
                    if(entry==vampire&&!vampireAvailable)GUI.enabled=false;
                    if(entry.Definition.Section.StartsWith("36 ")&&(!directAvailable||!PracticeReady()))GUI.enabled=false;
                    if((entry==noClip||entry==noFlash)&&!miscHooksAvailable)GUI.enabled=false;
                    if(entry==infiniteMedical&&!medicalAvailable)GUI.enabled=false;
                    if(entry==noWeight&&!weightAvailable)GUI.enabled=false;
                    if(entry==extendedInteraction&&!interactionAvailable)GUI.enabled=false;
                    if(entry==customFireRate&&!rateAvailable)GUI.enabled=false;
                    if(entry==alwaysExits&&!exitsAvailable)GUI.enabled=false;
                    try{DrawMenuEntry(entry);}finally{GUI.enabled=previousEnabled;}
                }
                if(column==1)
                {
                    if(compactTab==0) MenuNote("Body ESP is independently toggleable. Friendlies are identified by matching group IDs. Visibility filters apply only to living-enemy ESP, not aiming or bodies.");
                    if(compactTab==1) MenuNote("Quest highlights identify incomplete find/handover template matches, not a guarantee that every objective restriction is satisfied.");
                    if(AimTab) MenuNote((compactTab==2?"Legit Aim defaults to a small cone, slow smooth tracking, a correction limit and switch delay.":"Rage Aim defaults to instant lock, a wide cone and longer range. Disable Instant lock to use its turn speed and smoothing.")+"\n\nEnable a mode in its own tab to activate it. Only one mode runs. Browsing another tab does not switch modes. Visibility and exclusions still apply. No automatic firing.");
                    if(compactTab==3)MenuNote("Strict Head forces Head regardless of the saved Target bone and does not use body fallback. Prediction/drop are optional, default off, and analytic estimates. The selected-target marker stays on the current anatomical point, even when aim compensation leads ahead. Firing is never blocked by these controls.");
                    if(compactTab==4)MenuNote("Practice toggles apply only to your living local player in a raid. They start off and reset when the player/world changes or the raid ends. Panic disables them. Prevent malfunctions stops new malfunctions; it does not clear an existing jam.");
                    if(AimTab)MenuNote("RCS uses this mode's sliders when this is the active enabled aim mode. It does not need a target lock. Zero leaves recoil unchanged; 100 cancels new impulses on the selected component. Camera roll and unrelated sway remain.");
                    if(AimTab&&!rcsAvailable)MenuNote("RCS hooks could not be loaded. Check the BepInEx log.");
                }
            }
            finally {menuHeights[column]=menuY+12; GUI.EndScrollView();}
        }
        private Rect MenuRow(float height=31)
        {Rect rect=new Rect(12,menuY,menuWidth,height); menuY+=height+5; return rect;}
        private void MenuSection(string title)
        {
            Rect row=MenuRow(29);GUI.Label(row,title,menuSmall);
            MenuBox(new Rect(row.x,row.yMax-1,row.width,1),new Color(0.24f,0.29f,0.34f),Texture2D.whiteTexture);
        }
        private static int EspSectionRank(string section)
        {
            if(section.StartsWith("16 "))return 0;if(section.StartsWith("01 "))return 1;if(section.StartsWith("04 "))return 2;
            if(section.StartsWith("17 "))return 3;if(section.StartsWith("21 "))return 4;
            if(section.StartsWith("32 "))return 5;
            if(section.StartsWith("07 "))return 0;if(section.StartsWith("19 "))return 1;if(section.StartsWith("20 "))return 2;return 10;
        }
        private static string EspSectionTitle(string section)
        {
            if(section.StartsWith("16 "))return "DISPLAY STYLE & PRESET";if(section.StartsWith("01 "))return "ENEMY MARKERS & VISIBILITY";
            if(section.StartsWith("04 "))return "TEXT";if(section.StartsWith("17 "))return "ENEMY TYPE FILTERS";
            if(section.StartsWith("21 "))return "ENEMY COLOURS";if(section.StartsWith("07 "))return "BODY ESP";
            if(section.StartsWith("19 "))return "BODY TYPE FILTERS";if(section.StartsWith("20 "))return "BODY COLOURS";if(section.StartsWith("32 "))return "NEARBY ENEMY ALERTS";return section;
        }
        private void MenuHover(Rect rect,string help)
        {
            if(Event.current.type!=EventType.Repaint)return;
            if(HoverRules.Hit(tooltipMouse.x,tooltipMouse.y,rect.x+tooltipOrigin.x,rect.y+tooltipOrigin.y,rect.width,rect.height,
                tooltipViewport.x,tooltipViewport.y,tooltipViewport.width,tooltipViewport.height))hoverHelp=help;
        }
        private void MenuName(Rect rect,string name,string help)
        {
            string id=name+"\n"+help; GUIContent content;
            if(!menuContents.TryGetValue(id,out content)) {content=new GUIContent(name,help); menuContents[id]=content;}
            GUI.Label(rect,content,menuText); MenuHover(rect,help);
        }
        private static string MenuNameFor(string value)
        {
            if(value=="LeftArm")return "Left Arm";if(value=="RightArm")return "Right Arm";
            if(value=="LeftLeg")return "Left Leg";if(value=="RightLeg")return "Right Leg";
            switch(value)
            {
                case "Visibility check":return "Visible Targets Only"; case "Names and distance":return "Type & distance";
                case "Upper body skeleton":return "Upper-body skeleton"; case "FOV radius degrees":return "FOV radius (°)";
                case "Maximum turn degrees per second":return "Max turn speed"; case "Target switch seconds":return "Switch delay (s)";
                case "Maximum correction degrees":return "Correction angle"; case "Smoothing seconds":return "Smoothing (s)";
                case "Target bosses and other AI":return "Bosses & other AI"; case "Target ordinary scavs":return "Target scavs";
                case "Important items bypass filters":return "Important-item bypass"; case "NearestToCrosshair":return "Nearest to crosshair";
                case "Maximum distance":return "Maximum distance (m)";
                case "RightMouseHeld":return "Right mouse held"; case "HoldJsonHotkey":return "JSON hotkey";
                case "CustomCategories":return "Custom categories"; case "QuestRun":return "Quest run";
                case "Maximum labels":return "Label limit";
                case "FullBox":return "Full box";case "CornerBrackets":return "Corner brackets";
                case "NearCrosshair":return "Near crosshair";case "WithinDistance":return "Within distance";
                case "NearCrosshairOrDistance":return "Crosshair or nearby";
                case "Distance-based loot detail":return "Distance detail";case "Hidden colour brightness":return "Hidden brightness";
                case "Enemy type colours":return "Colours by type";case "Enemy detail distance":return "Enemy detail range (m)";
                case "Equipment detail distance":return "Detail range (m)";case "Equipment focus angle":return "Detail focus (°)";
                case "Loot name distance":return "Loot name range (m)";
                default:return value.Replace("Category - ","");
            }
        }
        private void DrawMenuEntry(ConfigEntryBase entry)
        {
            Rect row=MenuRow(),control=new Rect(row.x+178,row.y+2,row.width-178,row.height-4);
            string key=entry.Definition.Key,id=entry.Definition.Section+"/"+key;
            string help=MenuHelp(entry);
            if(entry.SettingType==typeof(Color)) help+=" Enter #RRGGBB or #RRGGBBAA.";
            MenuName(new Rect(row.x,row.y,174,row.height),entry==corpseEsp?"Body ESP":entry==enemyEsp?"Enemy ESP":MenuNameFor(key),help); MenuHover(control,help);
            Type type=entry.SettingType;
            if(type==typeof(bool))
            {
                bool modeSwitch=entry==aim&&AimTab;
                bool value=(bool)entry.BoxedValue&&(!modeSwitch||activeAimMode.Value==WindowAimMode); Rect hit=new Rect(control.xMax-46,control.y+3,42,21);
                if(GUI.Button(hit,GUIContent.none,GUIStyle.none)) {value=!value; if(modeSwitch) EnableWindowAim(value); else entry.BoxedValue=value;}
                MenuBox(hit,value?Accent:new Color(0.30f,0.33f,0.37f)); MenuBox(new Rect(hit.x+(value?23:3),hit.y+3,15,15),Color.white,menuCircle);
            }
            else if(type.IsEnum)
            {
                if(MenuButton(control,MenuNameFor(entry.BoxedValue.ToString())+"  ▾")) expandedEntry=expandedEntry==id?null:id;
                if(expandedEntry==id) foreach(var value in Enum.GetValues(type))
                {
                    Rect option=MenuRow(27);
                    if(MenuButton(new Rect(option.x+178,option.y,option.width-178,27),MenuNameFor(value.ToString()))) {entry.BoxedValue=value; expandedEntry=null;}
                }
            }
            else if(type==typeof(float)||type==typeof(int)) DrawMenuNumber(entry,id,control);
            else if(type==typeof(Color))
            {
                MenuBox(new Rect(control.x,control.y+3,25,21),(Color)entry.BoxedValue);
                bool edited;
                string value=MenuEdit(id,"#"+ColorUtility.ToHtmlStringRGBA((Color)entry.BoxedValue),new Rect(control.x+34,control.y,control.width-34,27),9,out edited);
                Color parsed; if(edited&&(value.Length==7||value.Length==9)&&ColorUtility.TryParseHtmlString(value,out parsed)) entry.BoxedValue=parsed;
            }
            else if(type==typeof(string))
            {string value=DarkTextField(control,(string)entry.BoxedValue,512); if(value!=(string)entry.BoxedValue) entry.BoxedValue=value;}
        }
        private string MenuHelp(ConfigEntryBase entry)
        {
            if(ProfileField(entry,"visibility")) return "Aimbot only aims at visible targets";
            if(entry==aim&&AimTab) return "Enable this aim mode. It becomes the only active mode; the other mode stops. Toggle off to disable aiming.";
            if(entry==enemyRange) return "Only show living enemy markers within this distance, in metres.";
            if(entry==lootRange) return "Only show loot markers within this distance, in metres.";
            if(ProfileField(entry,"aimRange")) return "Aimbot only considers targets within this distance, in metres.";
            if(entry==corpseRange) return "Only show corpse markers within this distance, in metres.";
            if(ProfileField(entry,"bone")) return "Choose Head, Chest, Pelvis, or the eligible body point nearest your crosshair.";
            if(entry==refresh) return "Time in seconds between loot scans. Scans are spread across frames; lower intervals use more CPU.";
            if(entry==lootLimit) return "Maximum number of loose-loot labels. Quest items come first, then wishlist items, then other items; nearest first within each group.";
            return entry.Description.Description;
        }
        private string MenuEdit(string id,string current,Rect rect,int limit,out bool edited)
        {
            string value; if(!menuEdits.TryGetValue(id,out value)||GUI.GetNameOfFocusedControl()!=id) value=current;
            GUI.SetNextControlName(id); string next=DarkTextField(rect,value,limit); edited=next!=value;
            menuEdits[id]=next; return next;
        }
        private string DarkTextField(Rect rect,string value,int limit)
        {
            Color background=GUI.backgroundColor,colour=GUI.color,content=GUI.contentColor;
            Color selection=GUI.skin.settings.selectionColor,cursor=GUI.skin.settings.cursorColor;
            try
            {
                GUI.backgroundColor=Color.white; GUI.color=Color.white; GUI.contentColor=Color.white;
                GUI.skin.settings.selectionColor=new Color(0.10f,0.27f,0.40f); GUI.skin.settings.cursorColor=Ink;
                return GUI.TextField(rect,value,limit,menuField);
            }
            finally
            {
                GUI.backgroundColor=background; GUI.color=colour; GUI.contentColor=content;
                GUI.skin.settings.selectionColor=selection; GUI.skin.settings.cursorColor=cursor;
            }
        }
        private void DrawMenuNumber(ConfigEntryBase entry,string id,Rect rect)
        {
            float min=0,max=100,current=Convert.ToSingle(entry.BoxedValue,CultureInfo.InvariantCulture);
            var floats=entry.Description.AcceptableValues as AcceptableValueRange<float>;
            var ints=entry.Description.AcceptableValues as AcceptableValueRange<int>;
            if(floats!=null) {min=floats.MinValue; max=floats.MaxValue;} if(ints!=null) {min=ints.MinValue; max=ints.MaxValue;}
            float next=GUI.HorizontalSlider(new Rect(rect.x,rect.y+10,rect.width-70,18),current,min,max,menuSlider,menuThumb);
            if(next!=current) entry.BoxedValue=entry.SettingType==typeof(int)?(object)Mathf.RoundToInt(next):next;
            bool edited;
            string value=MenuEdit(id,Convert.ToSingle(entry.BoxedValue,CultureInfo.InvariantCulture).ToString("0.##",CultureInfo.InvariantCulture),new Rect(rect.xMax-62,rect.y,62,27),10,out edited);
            float parsed; if(edited&&float.TryParse(value,NumberStyles.Float,CultureInfo.InvariantCulture,out parsed)&&!float.IsNaN(parsed)&&!float.IsInfinity(parsed))
            {parsed=Mathf.Clamp(parsed,min,max); entry.BoxedValue=entry.SettingType==typeof(int)?(object)Mathf.RoundToInt(parsed):parsed;}
        }
        private void MenuNote(string message)
        {float height=menuNote.CalcHeight(new GUIContent(message),menuWidth); GUI.Label(MenuRow(Mathf.Max(40,height+8)),message,menuNote);}
        private void DrawMenuTooltip(float width,float height)
        {
            if(string.IsNullOrEmpty(hoverHelp)) {lastHelp=null; return;}
            if(lastHelp!=hoverHelp) {lastHelp=hoverHelp; hoverSince=Time.realtimeSinceStartup;}
            if(Time.realtimeSinceStartup-hoverSince<0.3f) return;
            float tipWidth=340,tipHeight=menuTip.CalcHeight(new GUIContent(hoverHelp),tipWidth);
            Vector2 mouse=tooltipMouse-compactRect.position;
            Rect tip=new Rect(Mathf.Clamp(mouse.x+17,4,width-tipWidth-4),Mathf.Clamp(mouse.y+21,4,height-tipHeight-4),tipWidth,tipHeight);
            Color old=GUI.backgroundColor,colour=GUI.color,content=GUI.contentColor;
            GUI.backgroundColor=new Color(0.06f,0.075f,0.095f);GUI.color=GUI.contentColor=Color.white;
            GUI.Box(tip,hoverHelp,menuTip);
            GUI.backgroundColor=old;GUI.color=colour;GUI.contentColor=content;
        }
        private string KeyText(string action)
        {KeyChord chord; if(!bindings.TryGetValue(action,out chord)) return "None"; string value=""; foreach(var modifier in chord.Modifiers) value+=modifier+"+"; return value+chord.Main;}
        private void RefreshKeyDraft()
        {keyDraft.Clear(); foreach(var field in typeof(HotkeyConfig).GetFields()) keyDraft[field.Name]=KeyText(field.Name);}
        private void DrawKeysColumn(int column)
        {
            int start=column==0?0:15,end=column==0?15:keyActions.Length;
            for(int i=start;i<end;i++)
            {
                string action=keyActions[i]; Rect row=MenuRow(); string help=KeyHelp(action);
                MenuName(new Rect(row.x,row.y,174,row.height),KeyLabel(action),help); Rect control=new Rect(row.x+178,row.y+2,row.width-178,row.height-4);
                keyDraft[action]=DarkTextField(control,keyDraft[action],80); MenuHover(control,help);
            }
            if(column==0)
            {
                if(MenuButton(MenuRow(),"Save hotkeys to JSON")) SaveKeysDraft();
                if(MenuButton(MenuRow(),"Reload saved hotkeys")) {LoadKeys(false); RefreshKeyDraft(); keyError=null;}
            }
            else
            {
                MenuNote("Type key names, such as Mouse3, Home, or LeftControl+F6. None disables an action.\n\nSaved hotkeys use the same config.json. Gameplay settings apply immediately and save when this window closes.");
                if(!string.IsNullOrEmpty(keyError)) MenuNote("Hotkey error: "+keyError);
            }
        }
        private static string KeyLabel(string action)
        {
            switch(action){case "ToggleInfiniteMedical":return "Toggle infinite meds";case "ToggleNoWeight":return "Toggle weight bypass";case "ToggleInteractionRange":return "Toggle interaction range";case "ToggleFireRate":return "Toggle fire rate";case "ToggleEnemyAlerts":return "Toggle nearby alerts";case "ToggleAvailableExtractions":return "Toggle available exits";case "RepairEquippedGear":return "Repair equipped gear";}
            switch(action) {case "OpenSettings":return "Open / close window";case "AimHold":return "Hold to aim";case "ToggleEnemyESP":return "Toggle enemy ESP";case "ToggleLootESP":return "Toggle loot ESP";case "ToggleAimbot":return "Toggle aimbot";case "Panic":return "Disable all features";case "ReloadConfig":return "Reload JSON";case "HeadAimHold":return "Head aim override";case "IgnoreTarget":return "Ignore selected enemy";case "ClearIgnoredTargets":return "Clear raid exclusions";case "ToggleNightVision":return "Toggle night vision";case "ToggleThermalVision":return "Toggle thermal vision";case "ToggleNoFlash":return "Toggle no flash";case "ToggleNoVisor":return "Toggle no visor";case "ToggleNoClip":return "Toggle no-clip";default:return action.Replace("NoClip","Flight ");}
        }
        private static string KeyHelp(string action)
        {
            if(action=="RepairEquippedGear")return "Run the one-shot equipped-gear repair action during a raid, using your Restore maximum durability preference.";
            if(action.StartsWith("NoClip"))return "Held movement binding while No-clip is enabled. Supports standalone modifier keys such as LeftShift. Stored in config.json; no key capture is used.";
            switch(action) {case "OpenSettings":return "Open or close this compact window. F12 still opens BepInEx settings.";case "AimHold":return "Used only in JSON hotkey activation mode.";case "HeadAimHold":return "Temporarily prefer the head while held; normal visibility and fallback rules still apply.";case "IgnoreTarget":return "Exclude the tracked enemy, or the enemy nearest the crosshair within five degrees, for this raid.";case "ClearIgnoredTargets":return "Restore individually ignored enemies. Does not clear the Ignore enemies text.";case "Panic":return "Disable ESP, aimbot, Player and Misc toggles, and cancel pending item spawns, even while a menu is open.";case "ReloadConfig":return "Reload the saved hotkey JSON file.";default:return "Enable or disable the named feature. This hotkey remains stored in config.json.";}
        }
        private void SaveKeysDraft()
        {
            try
            {
                var candidate=new HotkeyConfig(); foreach(var field in typeof(HotkeyConfig).GetFields()) field.SetValue(candidate,keyDraft[field.Name]);
                var parsed=candidate.Parse(); File.WriteAllText(keyPath,JsonConvert.SerializeObject(candidate,Formatting.Indented)); bindings=parsed;
                keyError=null; menuMessage="Hotkeys saved to config.json.";
            }
            catch(Exception error) {keyError=error.Message; menuMessage="Hotkeys not saved. See Keys.";}
        }
    }
}







