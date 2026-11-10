using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using EFT;
using EFT.Interactive;
using Newtonsoft.Json;
using UnityEngine;

namespace SPTPracticeMenu
{

    [BepInPlugin("local.spt.practicemenu", "Tarkov Override", "1.1.0")]
    [BepInProcess("EscapeFromTarkov.exe")]
    [BepInDependency("com.SPT.core")]
    [BepInDependency("com.bepis.bepinex.configurationmanager", BepInDependency.DependencyFlags.SoftDependency)]
    public sealed partial class Plugin : BaseUnityPlugin
    {
        private ConfigEntry<bool> enemyEsp, boxes, names, health, skeleton, visibleEsp;
        private ConfigEntry<bool> lootEsp, containers, aim, visibility, adsOnly, sticky, circle, excludeGroup;
        private ConfigEntry<bool> pmcs, scavs, others, drawTarget;
        private ConfigEntry<float> enemyRange, lootRange, aimRange, fov, smoothing, maxTurn, refresh;
        private ConfigEntry<int> fontSize, lootLimit;
        private ConfigEntry<string> filter;
        private ConfigEntry<AimBone> bone;
        private ConfigEntry<AimPriority> priority;
        private ConfigEntry<AimActivation> activation;
        private ConfigEntry<bool> showAimStatus;
        private ConfigEntry<Color> enemyColour, lootColour;
        private GameWorld world;
        private Player local, locked;
        private Camera camera;
        private float nextLoot, nextLabels;
        private FileSystemWatcher keyWatcher;
        private int keysChanged;
        private float reloadAt;
        private string aimStatus = "Disabled";
        private GUIContent statusContent = new GUIContent();
        private bool scanningLoot;
        private int lootIndex;
        private IEnumerator<GameWorld.ItemOwnerWorldData> ownerScan;
        private readonly List<LootMarker> pendingLoot = new List<LootMarker>();
        private readonly Dictionary<string, string> itemNames = new Dictionary<string, string>();
        private string[] filterTerms = new string[0];
        private readonly Dictionary<Player, GUIContent> enemyLabels = new Dictionary<Player, GUIContent>();
        private readonly List<Player> players = new List<Player>();
        private readonly List<LootMarker> loot = new List<LootMarker>();
        private GUIStyle text;
        private bool suppressed = true, faulted, hasAimPoint;
        private Vector3 aimPoint;

        private Dictionary<string, KeyChord> bindings = new Dictionary<string, KeyChord>();
        private string keyPath;
        private DateTime keyStamp;
        private BaseUnityPlugin manager;
        private PropertyInfo managerVisible;
        private sealed class LootMarker { public Transform Transform; public string Name; public float Distance; public int Importance; public Color Colour; public EspKind Kind; public LootCategory Category; public GUIContent Label = new GUIContent(); public GUIContent Compact = new GUIContent(); }

        private ConfigEntry<T> Setting<T>(string section, string name, T value, string description)
        { return Config.Bind(section, name, value, AimDescription(section,name,description,null)); }
        private ConfigEntry<float> Slider(string section, string name, float value, float min, float max, string description)
        { return Config.Bind(section, name, value, AimDescription(section,name,description,new AcceptableValueRange<float>(min,max))); }

        private void Awake()
        {
            var plugins = BepInEx.Bootstrap.Chainloader.PluginInfos;
            BepInEx.PluginInfo spt;
            if (!plugins.TryGetValue("com.SPT.core", out spt) || spt.Metadata.Version != new System.Version(4, 1, 6))
            { Logger.LogError("This build requires SPT 4.1.6. Disabled."); enabled = false; return; }
            foreach (var p in plugins.Values)
                if (p.Metadata.GUID.IndexOf("fika", StringComparison.OrdinalIgnoreCase) >= 0)
                { Logger.LogError("Solo SPT only: Fika detected, toolkit disabled."); enabled = false; return; }
            BepInEx.PluginInfo cm;
            if (plugins.TryGetValue("com.bepis.bepinex.configurationmanager", out cm))
            { manager = cm.Instance; managerVisible = manager.GetType().GetProperty("DisplayingWindow"); }
            enemyEsp = Setting("01 Enemy ESP", "Enabled", false, "Show living AI players, excluding your own player.");
            enemyRange = Slider("01 Enemy ESP", "Maximum distance", 300, 10, 1500, "Metres.");
            boxes = Setting("01 Enemy ESP", "Bounding boxes", true, "Screen-space boxes using head and feet.");
            names = Setting("01 Enemy ESP", "Names and distance", true, "Enemy type and distance. Bosses retain their names.");
            health = Setting("01 Enemy ESP", "Health bars", true, "Total current health as a fraction of maximum.");
            skeleton = Setting("01 Enemy ESP", "Upper body skeleton", false, "Head, spine, shoulders and arms.");
            visibleEsp = Setting("01 Enemy ESP", "Visible only", false, "Hide AI behind walls. Adds physics raycast cost.");
            enemyColour = Setting("01 Enemy ESP", "Colour", new Color(1f, 0.3f, 0.25f), "Enemy overlay colour.");
            lootEsp = Setting("02 Loot ESP", "Enabled", false, "Show spawned loose items. Container contents are not scanned.");
            lootRange = Slider("02 Loot ESP", "Maximum distance", 80, 5, 500, "Metres.");
            containers = Setting("02 Loot ESP", "Container markers", false, "Show searchable container positions, without contents.");
            filter = Setting("02 Loot ESP", "Name filter", "", "Comma-separated name fragments or template IDs. Empty shows all. Example: bitcoin,M995,razor");
            refresh = Slider("02 Loot ESP", "Refresh seconds", 2, 0.5f, 10, "Lower values use more CPU.");
            lootLimit = Config.Bind("02 Loot ESP", "Maximum labels", 80, new ConfigDescription("Nearest matches first.", new AcceptableValueRange<int>(10, 300)));
            lootColour = Setting("02 Loot ESP", "Colour", new Color(1f, 0.75f, 0.25f), "Loot overlay colour.");
            aim = Setting("03 Aimbot", "Enabled", false, "Track living AI using the selected Activation mode. Does not fire automatically.");
            bone = Setting("03 Aimbot", "Target bone", AimBone.Head, "Body point to aim toward.");
            priority = Setting("03 Aimbot", "Target priority", AimPriority.Crosshair, "Candidate ranking inside the aim cone.");
            activation = Setting("03 Aimbot", "Activation", AimActivation.RightMouseHeld, "RightMouseHeld: hold right click only. HoldJsonHotkey: hold AimHold from JSON. Always: track while enabled.");
            showAimStatus = Setting("03 Aimbot", "Show aim status", true, "Show input state, available AI and target acquisition on screen.");
            fov = Slider("03 Aimbot", "FOV radius degrees", 8, 1, 45, "Angle from camera centre; radius, not full cone width.");
            aimRange = Slider("03 Aimbot", "Maximum distance", 200, 5, 1000, "Metres.");
            smoothing = Slider("03 Aimbot", "Smoothing seconds", 0.15f, 0, 1, "Response time. Zero removes smoothing; turn speed still applies.");
            maxTurn = Slider("03 Aimbot", "Maximum turn degrees per second", 180, 10, 1440, "Maximum rotation speed.");
            visibility = Setting("03 Aimbot", "Visibility check", true, "Require unobstructed physics ray to selected body point.");
            adsOnly = Setting("03 Aimbot", "Require right mouse held", true, "In HoldJsonHotkey mode only: also require right mouse. Other activation modes ignore this setting.");
            sticky = Setting("03 Aimbot", "Keep current target", true, "Keep target while held if range, cone and visibility constraints still pass.");
            excludeGroup = Setting("03 Aimbot", "Exclude same group", true, "Skip AI sharing your nonempty group ID. Does not infer all modded alliances.");
            pmcs = Setting("03 Aimbot", "Target PMCs", true, "PMC roles and sides.");
            scavs = Setting("03 Aimbot", "Target ordinary scavs", true, "Assault and marksman roles.");
            others = Setting("03 Aimbot", "Target bosses and other AI", true, "Bosses, guards, raiders and other roles.");
            circle = Setting("03 Aimbot", "Show FOV circle", true, "Draw acquisition cone while enabled.");
            drawTarget = Setting("03 Aimbot", "Show selected target", true, "Mark currently tracked point.");
            fontSize = Config.Bind("04 Display", "Text size", 13, new ConfigDescription("Screen pixels.", new AcceptableValueRange<int>(10, 24)));
            Setting("05 Hotkeys", "Instructions", "Edit BepInEx/config/SPTFieldToolkit/config.json", "JSON-only hotkeys reload automatically. No BepInEx key picker.");
            Setting("05 Hotkeys", "Optional actions", "HeadAimHold, IgnoreTarget, ClearIgnoredTargets", "Add these JSON properties with your chosen key names. Missing properties default to None. IgnoreTarget excludes the tracked enemy, or one within 5 degrees of your crosshair, for this raid.");
            BindFeatures();
            BindEspRevamp();
            BindAimProfiles();
            BindPracticeTools();
            BindCombatTools();
            BindMiscTools();
            BindAdvancedPractice();
            BindWindow();
            keyPath = Path.Combine(BepInEx.Paths.ConfigPath, "SPTFieldToolkit", "config.json");
            bindings = new HotkeyConfig().Parse();
            LoadKeys(true);
            filterTerms = filter.Value.Split(',');
            filter.SettingChanged += delegate { filterTerms = filter.Value.Split(','); StopLootScan(); nextLoot = 0; };
            StartKeyWatcher();
            Logger.LogInfo("Tarkov Override 1.1.0 ready. Author: StealthOTR. Direct Hit and Vampire Mode available.");
        }
        private void LoadKeys(bool create)
        {
            try
            {
                if (!File.Exists(keyPath))
                {
                    if (!create) return;
                    Directory.CreateDirectory(Path.GetDirectoryName(keyPath));
                    File.WriteAllText(keyPath, JsonConvert.SerializeObject(new HotkeyConfig(), Formatting.Indented));
                }
                keyStamp = File.GetLastWriteTimeUtc(keyPath);
                var candidate = JsonConvert.DeserializeObject<HotkeyConfig>(File.ReadAllText(keyPath),
                    new JsonSerializerSettings { MissingMemberHandling = MissingMemberHandling.Error });
                if (candidate == null) throw new FormatException("JSON must contain an object.");
                bindings = candidate.Parse();
                Logger.LogInfo("Hotkeys loaded: " + keyPath);
            }
            catch (Exception error) { Logger.LogWarning("Hotkey config rejected; previous bindings retained: " + error.Message); }
        }
        private bool Down(string name) { KeyChord c; return bindings.TryGetValue(name, out c) && c.Down(); }
        private bool Held(string name) { KeyChord c; return bindings.TryGetValue(name, out c) && c.Held(); }
        private void StartKeyWatcher()
        {
            try
            {
                keyWatcher = new FileSystemWatcher(Path.GetDirectoryName(keyPath), "config.json");
                keyWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                keyWatcher.Changed += delegate { Interlocked.Exchange(ref keysChanged, 1); };
                keyWatcher.Created += delegate { Interlocked.Exchange(ref keysChanged, 1); };
                keyWatcher.Renamed += delegate { Interlocked.Exchange(ref keysChanged, 1); };
                keyWatcher.EnableRaisingEvents = true;
            }
            catch (Exception error) { Logger.LogWarning("Automatic JSON watching unavailable; use ReloadConfig key: " + error.Message); }
        }
        private bool ManagerOpen()
        { return manager != null && managerVisible != null && (bool)managerVisible.GetValue(manager, null); }
        private bool MenuOpen() { return compactOpen || ManagerOpen(); }
        private bool Ready()
        {
            return !faulted && world != null && local != null && local.IsYourPlayer && local.HealthController != null &&
                local.HealthController.IsAlive && !string.Equals(local.Location, "hideout", StringComparison.OrdinalIgnoreCase);
        }
        private void Update()
        {
            try
            {
                if (Interlocked.Exchange(ref keysChanged, 0) != 0) reloadAt = Time.unscaledTime + 0.3f;
                if (reloadAt > 0 && Time.unscaledTime >= reloadAt) { reloadAt = 0; LoadKeys(false); }
                if (Down("ReloadConfig")) LoadKeys(false);
                UpdateWindowInput();
                if (Down("Panic"))
                { enemyEsp.Value = lootEsp.Value = aim.Value = corpseEsp.Value = false; ResetPracticeTools(); locked = null; hasAimPoint = false; suppressed = true; return; }
                if (!MenuOpen() && !Cursor.visible)
                {
                    if (Down("ToggleEnemyESP")) enemyEsp.Value = !enemyEsp.Value;
                    if (Down("ToggleLootESP")) lootEsp.Value = !lootEsp.Value;
                    if (Down("ToggleAimbot")) aim.Value = !aim.Value;
                }
                var found = Comfort.Common.Singleton<GameWorld>.Instantiated ? Comfort.Common.Singleton<GameWorld>.Instance : null;
                var player = found != null ? found.MainPlayer : null;
                if (found != world || player != local)
                {
                    world = found; local = player; loot.Clear(); players.Clear(); enemyLabels.Clear(); itemNames.Clear();
                    StopLootScan(); locked = null; faulted = false; nextLoot = 0;
                    ResetFeatures();
                }
                camera = EFT.CameraControl.CameraManager.Exist ? EFT.CameraControl.CameraManager.Instance.Camera : null;
                TickPracticeTools();
                TickMiscTools();
                TickAdvancedPractice();
                suppressed = !Ready() || MenuOpen() || local.IsInventoryOpened || Cursor.visible || Time.timeScale == 0;
                if (suppressed) { locked = null; hasAimPoint = false; return; }
                players.Clear();
                if (enemyEsp.Value || aim.Value || corpseEsp.Value)
                    foreach (var p in world.AllAlivePlayersList) if (ValidPlayer(p)) players.Add(p);
                TickEspRevamp();
                TickFeatures();
                if (lootEsp.Value || corpseEsp.Value) TickLootScan();
                else if (loot.Count != 0 || corpses.Count != 0 || scanningLoot) { loot.Clear(); corpses.Clear(); StopLootScan(); }
                if (Time.unscaledTime >= nextLabels)
                {
                    nextLabels = Time.unscaledTime + 0.25f;
                    enemyLabels.Clear();
                    if (enemyEsp.Value && names.Value)
                        foreach (var p in players)
                            enemyLabels[p] = new GUIContent(EnemyCaption(p));
                    foreach (var marker in loot)
                        if (marker.Transform != null) RefreshLootLabel(marker);
                    foreach (var marker in corpses)
                        if (marker.Transform != null) marker.Label.text = marker.Name + " | " + Vector3.Distance(local.Position, marker.Transform.position).ToString("0") + "m";
                }
            }
            catch (Exception error) { Fail(error); }
        }
        private bool ValidPlayer(Player p)
        { return p != null && p != local && p.IsAI && p.HealthController != null && p.HealthController.IsAlive; }
        private void Fail(Exception error)
        {
            if (!faulted) Logger.LogError("Toolkit suspended until next raid after error: " + error);
            faulted = true; suppressed = true; locked = null; hasAimPoint = false; loot.Clear(); players.Clear();
        }
        private Vector3 BodyPoint(Player p, AimBone which)
        {
            var b = p.PlayerBones;
            if(which==AimBone.Head)return LiveHeadPoint(p);
            if (b != null)
            {
                if (which == AimBone.Chest && b.Spine3 != null) return b.Spine3.position;
                if (which == AimBone.Pelvis && b.Pelvis != null) return b.Pelvis.position;
            }
            return p.Position + Vector3.up * (which == AimBone.Head ? 1.6f : which == AimBone.Chest ? 1.2f : 0.8f);
        }
        private bool Visible(Player target, Vector3 point)
        {
            RaycastHit hit;
            Vector3 origin = camera.transform.position, delta = point - origin;
            float offset = Mathf.Min(0.2f, delta.magnitude * 0.1f);
            origin += delta.normalized * offset;
            if (!Physics.Raycast(origin, delta.normalized, out hit, delta.magnitude - offset,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            if(hit.collider==null)return false;
            IPlayer owner;
            if(world!=null&&world.PlayersColliders!=null&&world.PlayersColliders.TryGetValue(hit.collider,out owner)&&ReferenceEquals(owner,target))return true;
            var part=hit.collider.GetComponentInParent<BodyPartCollider>();
            if(part!=null&&ReferenceEquals(part.Player,target))return true;
            return hit.collider.GetComponentInParent<Player>() == target;
        }
        private int Category(Player p)
        {
            string role = p.Profile.Info.Settings.Role.ToString();
            if (p.Side == EPlayerSide.Bear || p.Side == EPlayerSide.Usec || role == "pmcBEAR" || role == "pmcUSEC") return 0;
            return role == "assault" || role == "marksman" || role == "assaultGroup" || role == "cursedAssault" ? 1 : 2;
        }
        private static string EnemyType(Player p)
        { return EnemyLabel.Get(p.Profile.Info.Settings.Role.ToString(), p.Side.ToString(), p.Profile.Nickname); }
        private bool Candidate(Player p, out Vector3 point, out float score)
        {AimBone chosen;return Candidate(p,out point,out score,out chosen);}
        private bool Candidate(Player p, out Vector3 point, out float score,out AimBone chosen)
        {
            point = Vector3.zero; score = float.MaxValue;chosen=AimBone.Head;
            if (!ValidPlayer(p)) return false;
            if (excludeGroup.Value && !string.IsNullOrEmpty(local.GroupId) && local.GroupId == p.GroupId) return false;
            if (ignoredIds.Contains(p.ProfileId) || (!string.IsNullOrWhiteSpace(ignoredTypes.Value) &&
                FeatureRules.MatchesTokens(ignoredTerms,EnemyType(p),p.Profile.Info.Settings.Role.ToString(),p.ProfileId))) return false;
            int category = Category(p);
            if ((category == 0 && !pmcs.Value) || (category == 1 && !scavs.Value) || (category == 2 && !others.Value)) return false;
            AimBone selected = AimPointRules.Selected(Held("HeadAimHold") ? AimBone.Head : bone.Value,StrictRageHead);
            bool found = false; float nearest = float.MaxValue;
            int count=AimPointRules.Count(selected,fallback.Value,visibility.Value,activeAimMode.Value==AimMode.Rage&&rageHeadOnly.Value);
            for (int i = 0; i < count; i++)
            {
                AimBone test = AimPointRules.At(selected,i);
                Vector3 candidate = BodyPoint(p,test);
                if (PointAllowed(p,candidate))
                {
                    float candidateAngle = Vector3.Angle(camera.transform.forward,candidate-camera.transform.position);
                    if (candidateAngle < nearest) { point=candidate; nearest=candidateAngle; found=true;chosen=test; }
                    if (selected != AimBone.NearestToCrosshair) break;
                }
            }
            if (!found) return false;
            Vector3 delta = point - camera.transform.position;
            float distance = delta.magnitude, angle = Vector3.Angle(camera.transform.forward, delta);
            score = priority.Value == AimPriority.Distance ? distance : priority.Value == AimPriority.LowestHealth ? TotalHealth(p, false) : angle;
            return true;
        }
        private bool PointAllowed(Player p,Vector3 point)
        {
            if(!HeadPointResolver.Finite(point))return false;
            Vector3 delta=point-camera.transform.position;
            return AimLogic.InCone(delta.sqrMagnitude,Vector3.Angle(camera.transform.forward,delta),aimRange.Value,
                fov.Value,correction.Value,correctionAngle.Value) && (!visibility.Value || Visible(p,point));
        }
        private void LateUpdate()
        {
            MaintainWindowCursor();
            LateMiscTools();
            hasAimPoint = false;
            try
            {
                if (suppressed || !Ready() || camera == null || !camera.isActiveAndEnabled || !aim.Value || MenuOpen() || Cursor.visible)
                { locked = null; switchAt = -1; aimStatus = "Paused"; return; }
                bool active = AimLogic.Active(activation.Value, Input.GetKey(KeyCode.Mouse1), Held("AimHold"), adsOnly.Value);
                if (!active)
                {
                    locked = null;
                    switchAt = -1;
                    aimStatus = activation.Value == AimActivation.RightMouseHeld ? "Hold right mouse to aim" : "Hold JSON AimHold key" + (adsOnly.Value ? " + right mouse" : "");
                    return;
                }
                Vector3 point; float score;
                Player desired = locked;
                if (!(sticky.Value && locked != null && Candidate(locked, out point, out score)))
                {
                    desired = null; float best = float.MaxValue;
                    foreach (var p in players)
                        if (Candidate(p, out point, out score) && score < best) { best = score; desired = p; }
                }
                if (AimLogic.WaitForSwitch(locked != null,desired != locked,switchDelay.Value,switchSeconds.Value,
                    Time.unscaledTime,ref switchAt)) { aimStatus = "Waiting before target switch"; return; }
                locked = desired; switchAt = -1;
                AimBone actualBone;
                if (locked == null || !Candidate(locked, out point, out score,out actualBone))
                { aimStatus = "Input active | AI: " + players.Count + " | No eligible target in cone"; return; }
                aimPoint = point; hasAimPoint = true;
                AimBone requested=AimPointRules.Selected(Held("HeadAimHold")?AimBone.Head:bone.Value,StrictRageHead);
                aimStatus = "Tracking: " + EnemyType(locked)+" | "+actualBone+
                    (requested!=AimBone.NearestToCrosshair&&actualBone!=requested?" (fallback from "+requested+")":"")+
                    (actualBone==AimBone.Head?(headFromModel?" [model head]":" [head bone]"):"");
                if(StrictRageHead&&actualBone!=AimBone.Head){hasAimPoint=false;locked=null;aimStatus="Strict Head: invalid target point rejected";return;}
                string predictionStatus;
                Vector3 solution=RageAimSolution(point,out predictionStatus);
                aimStatus+=predictionStatus;
                Vector3 direction = (solution - camera.transform.position).normalized, forward = camera.transform.forward;
                float yaw, pitch;
                bool snap=activeAimMode.Value==AimMode.Rage && rageSnap.Value;
                if(snap&&local.MovementContext!=null)forward=local.MovementContext.LookDirection;
                AimLogic.Turn(forward.x, forward.y, forward.z, direction.x, direction.y, direction.z,
                    snap?0.05f:Time.deltaTime, snap?0:smoothing.Value, snap?7200:maxTurn.Value, out yaw, out pitch);
                // EFT's input path updates CachedRotation as well as Rotation. Setting only Rotation
                // lets the following movement update restore the old cached view direction.
                local.Rotate(new Vector2(yaw, pitch), snap);
            }
            catch (Exception error) { Fail(error); }
        }
        private static float TotalHealth(Player p, bool ratio)
        {
            var value = p.HealthController.GetBodyPartHealth(EBodyPart.Common, false);
            return ratio ? (value.Maximum > 0 ? Mathf.Clamp01(value.Current / value.Maximum) : 0) : value.Current;
        }
        private bool Matches(string name, string template)
        {
            if (string.IsNullOrWhiteSpace(filter.Value)) return true;
            foreach (string part in filterTerms)
            {
                string term = part.Trim();
                if (term.Length > 0 && (name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 || template.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)) return true;
            }
            return false;
        }
        private void StopLootScan()
        {
            if (ownerScan != null) { ownerScan.Dispose(); ownerScan = null; }
            scanningLoot = false; pendingLoot.Clear(); pendingCorpses.Clear(); lootIndex = 0;
        }
        private string ItemName(string template, string key)
        {
            string name;
            if (!itemNames.TryGetValue(template, out name))
            { name = EFT.LocalizationExtensions.Localized(key ?? template, (string)null) ?? key ?? template; itemNames[template] = name; }
            return name;
        }
        private LootMarker AddLoot(Transform transform, string name)
        { return AddLoot(transform,name,0,lootColour.Value); }
        private LootMarker AddLoot(Transform transform, string name, int importance, Color colour)
        {
            float distance = (transform.position - local.Position).sqrMagnitude;
            // Keep a bounded nearest-first list instead of sorting every item in the level.
            int at = pendingLoot.Count;
            while (at > 0 && (pendingLoot[at-1].Importance < importance ||
                (pendingLoot[at-1].Importance == importance && pendingLoot[at - 1].Distance > distance))) at--;
            if (at >= lootLimit.Value) return null;
            var marker = new LootMarker { Transform = transform, Name = name, Distance = distance, Importance = importance, Colour = colour,Category=LootCategory.Other };
            marker.Label.text = name + " | " + Mathf.Sqrt(distance).ToString("0") + "m";
            pendingLoot.Insert(at, marker);
            if (pendingLoot.Count > lootLimit.Value) pendingLoot.RemoveAt(pendingLoot.Count - 1);
            return marker;
        }
        private void TickLootScan()
        {
            if (!scanningLoot)
            {
                if (Time.unscaledTime < nextLoot) return;
                scanningLoot = true; lootIndex = 0; pendingLoot.Clear(); pendingCorpses.Clear();
            }
            float squared = lootRange.Value * lootRange.Value;
            int budget = 32;
            // The registry is already maintained by EFT; no Unity scene traversal or large array allocation.
            while (world.LootItems != null && lootIndex < world.LootItems.Count && budget-- > 0)
            {
                var item = world.LootItems.GetByIndex(lootIndex++);
                var corpse = item as Corpse;
                if (corpse != null) { AddCorpse(corpse); continue; }
                if (!lootEsp.Value) continue;
                if (item == null || item.Item == null || !item.gameObject.activeInHierarchy ||
                    (item.transform.position - local.Position).sqrMagnitude > squared) continue;
                string template = item.TemplateId ?? item.Item.ShortName ?? "";
                string name = ItemName(template, item.Item.ShortName);
                AddItemLoot(item,name,template);
            }
            if (world.LootItems != null && lootIndex < world.LootItems.Count) return;
            if (lootEsp.Value && containers.Value && world.ItemOwners != null)
            {
                if (ownerScan == null) ownerScan = world.ItemOwners.Values.GetEnumerator();
                try
                {
                    while (budget-- > 0)
                    {
                        if (!ownerScan.MoveNext()) { CompleteLootScan(); return; }
                        var data = ownerScan.Current;
                        if (data.Transform == null || (data.Transform.position - local.Position).sqrMagnitude > squared) continue;
                        var c = data.Transform.GetComponent<LootableContainer>();
                        if (c == null || !c.IsInitialized || !c.gameObject.activeInHierarchy) continue;
                        string name = ItemName("container:" + c.TypeKey, c.TypeKey);
                        if (Matches(name, c.Template ?? "")) AddLoot(c.transform, "[Container] " + name);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Registry changed during a multi-frame pass. Keep the loose-item results and
                    // try containers again on the next refresh rather than stalling the main thread.
                    CompleteLootScan();
                }
                return;
            }
            CompleteLootScan();
        }
        private void CompleteLootScan()
        {
            loot.Clear(); loot.AddRange(pendingLoot);
            corpses.Clear(); corpses.AddRange(pendingCorpses); nextGroups = 0;
            StopLootScan();
            nextLoot = Time.unscaledTime + (corpseEsp.Value ? (lootEsp.Value ? Mathf.Min(refresh.Value,corpseRefresh.Value) : corpseRefresh.Value) : refresh.Value);
        }
        private bool ScreenPoint(Vector3 point, out Vector2 screen)
        {
            // Camera pixel dimensions may follow a render texture rather than the display.
            Vector3 p=camera.WorldToViewportPoint(point);Rect viewport=camera.rect;
            screen=new Vector2((viewport.x+p.x*viewport.width)*Screen.width,(1-viewport.y-p.y*viewport.height)*Screen.height);
            return p.z>0&&p.x>=0&&p.x<=1&&p.y>=0&&p.y<=1;
        }
        private void OnGUI()
        {
            DrawCompactWindow();
            if (compactOpen) return;
            if (Event.current.type != EventType.Repaint || suppressed || !Ready() || camera == null) return;
            Color old = GUI.color;
            try
            {
                if (text == null) text = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, richText = false };
                text.fontSize = fontSize.Value;
                DrawRaidAwareness();
                if (enemyEsp.Value) DrawEnemyRevamp();
                if (lootEsp.Value) DrawLootGroups();
                if (corpseEsp.Value) foreach (var marker in corpses) DrawBodyMarker(marker);
                if (aim.Value && circle.Value && fov.Value>=89)
                    Outline(new Rect(4,4,Screen.width-8,Screen.height-8),new Color(1,1,1,0.2f));
                if (aim.Value && circle.Value && fov.Value<89)
                {
                    float radius = Screen.height * 0.5f * Mathf.Tan(fov.Value * Mathf.Deg2Rad) / Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                    Vector2 centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), previous = centre + new Vector2(radius, 0);
                    for (int i = 1; i <= 96; i++)
                    {
                        float a = i * Mathf.PI * 2 / 96;
                        Vector2 next = centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
                        Line(previous, next, new Color(1, 1, 1, 0.45f)); previous = next;
                    }
                }
                Vector2 target;
                if (aim.Value && drawTarget.Value && hasAimPoint && ScreenPoint(aimPoint, out target)) Outline(new Rect(target.x - 5, target.y - 5, 10, 10), Color.cyan);
                if (aim.Value && showAimStatus.Value)
                {
                    statusContent.text = aimStatus;
                    Label(new Vector2(Screen.width * 0.5f, Screen.height - 60), statusContent, Color.cyan);
                }
            }
            catch (Exception error) { Fail(error); }
            finally { GUI.color = old; }
        }
        private void DrawSkeleton(Player p, Color c)
        {
            var b = p.PlayerBones; if (b == null) return;
            BoneLine(b.Head, b.Spine3, c); BoneLine(b.Spine3, b.Pelvis, c);
            BoneLine(b.Spine3, b.LeftShoulder, c); BoneLine(b.Spine3, b.RightShoulder, c);
            if (b.Forearms != null && b.Forearms.Length >= 2)
            {
                if (b.LeftShoulder != null && b.Forearms[0] != null) WorldLine(b.LeftShoulder.position, b.Forearms[0].position, c);
                if (b.RightShoulder != null && b.Forearms[1] != null) WorldLine(b.RightShoulder.position, b.Forearms[1].position, c);
                if (b.Forearms[0] != null && b.LeftPalm != null) WorldLine(b.Forearms[0].position, b.LeftPalm.position, c);
                if (b.Forearms[1] != null && b.RightPalm != null) WorldLine(b.Forearms[1].position, b.RightPalm.position, c);
            }
        }
        private void BoneLine(BifacialTransform a, BifacialTransform b, Color c) { if (a != null && b != null) WorldLine(a.position, b.position, c); }
        private void WorldLine(Vector3 a, Vector3 b, Color c) { Vector2 sa, sb; if (ScreenPoint(a, out sa) && ScreenPoint(b, out sb)) Line(sa, sb, c); }
        private static void Paint(Rect r, Color c) { GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); }
        private static void Outline(Rect r, Color c)
        {
            Paint(new Rect(r.x, r.y, r.width, 1), c); Paint(new Rect(r.x, r.yMax, r.width, 1), c);
            Paint(new Rect(r.x, r.y, 1, r.height), c); Paint(new Rect(r.xMax, r.y, 1, r.height), c);
        }
        private static void Line(Vector2 a, Vector2 b, Color c)
        {
            Matrix4x4 matrix = GUI.matrix;
            try { GUIUtility.RotateAroundPivot(Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg, a); Paint(new Rect(a.x, a.y, Vector2.Distance(a, b), 1), c); }
            finally { GUI.matrix = matrix; }
        }
        private void Label(Vector2 centre, GUIContent content, Color c)
        {
            float w = Mathf.Min(Screen.width, text.CalcSize(content).x + 8);
            Rect r = new Rect(centre.x - w * 0.5f, centre.y - 10, w, text.CalcSize(content).y + 8);
            GUI.color = Color.black; GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), content, text);
            GUI.color = c; GUI.Label(r, content, text);
        }
        private void OnDisable()
        {
            ShutdownAdvancedPractice();
            ShutdownMiscTools();
            ShutdownCombatTools();
            ShutdownPracticeTools();
            ShutdownWindow();
            if (keyWatcher != null) { keyWatcher.Dispose(); keyWatcher = null; }
            StopLootScan(); locked = null; loot.Clear(); players.Clear(); suppressed = true; hasAimPoint = false;
        }
    }
}









