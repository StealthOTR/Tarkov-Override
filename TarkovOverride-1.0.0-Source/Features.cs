using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using EFT;
using EFT.InventoryLogic;
using EFT.Interactive;
using EFT.Quests;
using UnityEngine;

namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private ConfigEntry<LootPreset> lootPreset;
        private readonly Dictionary<LootCategory, ConfigEntry<bool>> categories = new Dictionary<LootCategory, ConfigEntry<bool>>();
        private ConfigEntry<bool> wishHighlight, questHighlight, importantBypass, clutter, corpseEsp;
        private ConfigEntry<bool> switchDelay, fallback, correction;
        private ConfigEntry<float> switchSeconds, correctionAngle, corpseRange, corpseRefresh, clusterSize;
        private ConfigEntry<int> clusterNames;
        private ConfigEntry<Color> wishColour, questColour, corpseColour;
        private ConfigEntry<string> ignoredTypes;
        private string[] ignoredTerms = new string[0];
        private readonly HashSet<string> wishlistIds = new HashSet<string>();
        private readonly HashSet<string> questIds = new HashSet<string>();
        private readonly HashSet<string> ignoredIds = new HashSet<string>();
        private readonly Dictionary<string, LootCategory> categoryCache = new Dictionary<string, LootCategory>();
        private readonly Dictionary<string, string> knownEnemies = new Dictionary<string, string>();
        private float nextNeeds, switchAt = -1, nextGroups;
        private bool collectingNeeds;
        private int questIndex;
        private readonly HashSet<string> pendingQuestIds = new HashSet<string>();
        private readonly List<LootMarker> corpses = new List<LootMarker>();
        private readonly List<LootMarker> pendingCorpses = new List<LootMarker>();
        private readonly Dictionary<long, LootGroup> groupMap = new Dictionary<long, LootGroup>();
        private readonly List<LootGroup> groups = new List<LootGroup>();
        private readonly List<LootGroup> groupPool = new List<LootGroup>();
        private readonly List<Rect> occupied = new List<Rect>();
        private sealed class LootGroup
        {
            public Transform Anchor; public Color Colour; public int Count;
            public readonly StringBuilder Builder = new StringBuilder();
            public readonly GUIContent Content = new GUIContent();
            public readonly List<LootMarker> Rows = new List<LootMarker>();
            public LootCategory Category;
        }
        private void BindFeatures()
        {
            lootPreset = Setting("06 Loot filters and highlights", "Preset", LootPreset.All, "All, category presets, CustomCategories, QuestRun (quest/wishlist plus keys and meds), or Wishlist only.");
            foreach (LootCategory category in Enum.GetValues(typeof(LootCategory)))
                categories[category] = Setting("06 Loot filters and highlights", "Category - " + category, true, "Used by the CustomCategories preset.");
            wishHighlight = Setting("06 Loot filters and highlights", "Wishlist highlighting", true, "Tag loose items on your in-game wishlist, including automatic wishlist groups.");
            questHighlight = Setting("06 Loot filters and highlights", "Quest highlighting", true, "Tag item-template matches for incomplete find/handover objectives on started quests. Does not certify durability or other objective restrictions.");
            importantBypass = Setting("06 Loot filters and highlights", "Important items bypass filters", true, "Highlighted wishlist/quest items bypass category and name filters, except in Wishlist-only preset.");
            wishColour = Setting("06 Loot filters and highlights", "Wishlist colour", Color.cyan, "Wishlist label colour.");
            questColour = Setting("06 Loot filters and highlights", "Quest colour", new Color(0.5f,1,0.3f), "Quest labels take colour precedence over wishlist labels.");
            clutter = Setting("06 Loot filters and highlights", "Group nearby labels", true, "Combine nearby screen-space loot labels and suppress overlapping groups.");
            clusterSize = Slider("06 Loot filters and highlights", "Grouping pixels", 90, 30, 200, "Screen-space grouping cell size.");
            clusterNames = Config.Bind("06 Loot filters and highlights", "Names per group", 2, new ConfigDescription("Additional items appear as +N nearby.", new AcceptableValueRange<int>(1,5)));
            corpseEsp = Setting("07 Corpse markers", "Enabled", false, "Show bodies independently of loot ESP. Bodies are never aimbot targets.");
            corpseRange = Slider("07 Corpse markers", "Maximum distance", 150, 5, 500, "Metres.");
            corpseRefresh = Slider("07 Corpse markers", "Refresh seconds", 2, 0.5f, 10, "Refresh interval after each bounded registry scan.");
            corpseColour = Setting("07 Corpse markers", "Colour", new Color(0.65f,0.65f,0.7f), "Body label colour.");
            switchDelay = Setting("09 Advanced aim", "Target switch delay", false, "Wait before changing away from an acquired target, including after a kill.");
            switchSeconds = Slider("09 Advanced aim", "Target switch seconds", 0.3f, 0, 3, "Delay before switching; initial acquisition is immediate.");
            fallback = Setting("09 Advanced aim", "Aim point fallback", false, "With Visibility check enabled, try visible chest/pelvis/head alternatives when the chosen point is blocked. All points must pass FOV/range/correction limits.");
            correction = Setting("09 Advanced aim", "Limit aim correction", false, "Only assist when a body point is already within Maximum correction degrees of the crosshair.");
            correctionAngle = Slider("09 Advanced aim", "Maximum correction degrees", 3, 0.1f, 45, "Angular acquisition limit; smaller of this and the main FOV is used.");
            ignoredTypes = Setting("09 Advanced aim", "Ignore enemies", "", "Comma-separated exact type labels, boss names, raw roles or profile IDs. Examples: SCAV,ROGUE,Killa. Never matches ordinary random nicknames.");
            ignoredTerms = ignoredTypes.Value.Split(',');
            ignoredTypes.SettingChanged += delegate { ignoredTerms = ignoredTypes.Value.Split(','); };
            lootPreset.SettingChanged += InvalidateLoot;
            wishHighlight.SettingChanged += InvalidateLoot; questHighlight.SettingChanged += InvalidateLoot;
            importantBypass.SettingChanged += InvalidateLoot; wishColour.SettingChanged += InvalidateLoot;
            questColour.SettingChanged += InvalidateLoot; corpseEsp.SettingChanged += InvalidateLoot;
            corpseRange.SettingChanged += InvalidateLoot; corpseColour.SettingChanged += InvalidateLoot;
            foreach (var category in categories.Values) category.SettingChanged += InvalidateLoot;
        }
        private void InvalidateLoot(object sender,EventArgs args)
        { StopLootScan(); nextLoot = 0; nextGroups = 0; }
        private void ResetFeatures()
        {
            ignoredIds.Clear(); knownEnemies.Clear(); categoryCache.Clear(); wishlistIds.Clear(); questIds.Clear();
            corpses.Clear(); pendingCorpses.Clear(); groups.Clear(); groupMap.Clear();
            nextNeeds = nextGroups = 0; switchAt = -1;
            collectingNeeds = false; questIndex = 0; pendingQuestIds.Clear();
            espRecords.Clear();knownEnemyKinds.Clear();visibilityIndex=0;
            ResetPracticeTools();
        }
        private void TickFeatures()
        {
            if (Time.unscaledTime >= nextLabels)
                foreach (var p in players) if (!string.IsNullOrEmpty(p.ProfileId)) knownEnemies[p.ProfileId] = EnemyType(p);
            if (Down("ClearIgnoredTargets")) { ignoredIds.Clear(); locked = null; switchAt = -1; }
            if (Down("IgnoreTarget"))
            {
                Player selected = hasAimPoint && locked != null ? locked : null;
                float nearest = 5;
                if (selected == null && camera != null)
                    foreach (var p in players)
                    {
                        float angle = Vector3.Angle(camera.transform.forward, BodyPoint(p,AimBone.Chest)-camera.transform.position);
                        if (angle < nearest && Vector3.Distance(local.Position,p.Position) <= aimRange.Value) { nearest = angle; selected = p; }
                    }
                if (selected != null && !string.IsNullOrEmpty(selected.ProfileId))
                { ignoredIds.Add(selected.ProfileId); if (locked == selected) locked = null; switchAt = -1; aimStatus = "Ignored " + EnemyType(selected) + " for this raid"; }
            }
            if (lootEsp.Value && (collectingNeeds || Time.unscaledTime >= nextNeeds)) RefreshNeeds();
        }
        private string EnemyCaption(Player p)
        {
            return names.Value ? (EspInfo(p).Kind==EspKind.Friendly?"FRIENDLY / ":"")+EnemyType(p)+" | "+Vector3.Distance(local.Position,p.Position).ToString("0")+"m" : "";
        }
        private void RefreshNeeds()
        {
            if (!collectingNeeds)
            {
                wishlistIds.Clear(); pendingQuestIds.Clear(); questIndex = 0; collectingNeeds = true;
                if (local.Profile.WishlistManager != null)
                {
                    var wishlist = local.Profile.WishlistManager.GetWishlist();
                    if (wishlist != null) foreach (var entry in wishlist) wishlistIds.Add(entry.Key.ToString());
                }
            }
            int budget = 2;
            var dataList = local.Profile.QuestsData;
            while (dataList != null && questIndex < dataList.Count && budget-- > 0)
            {
                var data = dataList[questIndex++];
                if (data.Status != EQuestStatus.Started) continue;
                var template = data.Template;
                if (template == null && local.QuestController != null && local.QuestController.Quests != null)
                { var runtime = local.QuestController.Quests.GetConditional(data.Id); if (runtime != null) template = runtime.Template; }
                if (template == null || template.Conditions == null) continue;
                ConditionCollection finish;
                if (!template.Conditions.TryGetValue(EQuestStatus.AvailableForFinish,out finish)) continue;
                foreach (var condition in finish) AddQuestCondition(condition,data.CompletedConditions,0);
            }
            if (dataList == null || questIndex >= dataList.Count)
            { questIds.Clear(); questIds.UnionWith(pendingQuestIds); collectingNeeds = false; nextNeeds = Time.unscaledTime + 5; nextLoot = 0; }
        }
        private void AddQuestCondition(Condition condition, HashSet<MongoID> complete, int depth)
        {
            if (depth > 16 || condition == null || (complete != null && complete.Contains(condition.id))) return;
            if (condition is ConditionFindItem || condition is ConditionHandoverItem)
            {
                var item = (ConditionMultipleTargets)condition;
                if (item.target != null) foreach (var id in item.target) if (!string.IsNullOrEmpty(id)) pendingQuestIds.Add(id);
            }
            if (condition.ChildConditions != null)
                foreach (var child in condition.ChildConditions) AddQuestCondition(child,complete,depth+1);
        }
        private LootCategory ItemCategory(Item item)
        {
            string id = item.StringTemplateId;
            LootCategory category;
            if (categoryCache.TryGetValue(id,out category)) return category;
            category = LootCategory.Other;
            // Traverse the game's actual template ancestry, so modded children inherit their category.
            int depth = 0;
            for (var node = item.Template; node != null && depth++ < 64; node = node.Parent)
            {
                var root = FeatureRules.CategoryRoot(node.StringId);
                if (root.HasValue) { category = root.Value; break; }
            }
            categoryCache[id] = category;
            return category;
        }
        private bool NeededForQuest(Item item)
        {
            int depth = 0;
            for (var node = item.Template; node != null && depth++ < 64; node = node.Parent)
                if (questIds.Contains(node.StringId)) return true;
            return false;
        }
        private void AddItemLoot(LootItem item, string name, string template)
        {
            bool wish = wishlistIds.Contains(item.Item.StringTemplateId), quest = NeededForQuest(item.Item);
            bool highlightedWish = wish && wishHighlight.Value, highlightedQuest = quest && questHighlight.Value;
            var category = ItemCategory(item.Item);
            if (!FeatureRules.IncludeItem(lootPreset.Value,category,quest,wish,categories[category].Value,
                Matches(name,template),questHighlight.Value,wishHighlight.Value,importantBypass.Value)) return;
            string prefix = (highlightedQuest ? "[QUEST] " : "") + (highlightedWish ? "[WISH] " : "");
            int importance = highlightedQuest ? 2 : highlightedWish ? 1 : 0;
            var marker=AddLoot(item.transform,prefix+name,importance,highlightedQuest ? questColour.Value : highlightedWish ? wishColour.Value : lootColour.Value);
            if(marker!=null){marker.Category=category;RefreshLootLabel(marker);}
        }
        private void AddCorpse(Corpse corpse)
        {
            if (!corpseEsp.Value || (corpse.transform.position-local.Position).sqrMagnitude > corpseRange.Value*corpseRange.Value || pendingCorpses.Count >= lootLimit.Value) return;
            string type;
            if (string.IsNullOrEmpty(corpse.PlayerProfileID) || !knownEnemies.TryGetValue(corpse.PlayerProfileID,out type))
                type = EnemyLabel.Get("",corpse.Side.ToString(),"");
            EspKind kind;
            if(string.IsNullOrEmpty(corpse.PlayerProfileID)||!knownEnemyKinds.TryGetValue(corpse.PlayerProfileID,out kind))kind=EspRules.Kind("",corpse.Side.ToString(),false);
            if(!bodyKindFilters[kind].Value)return;
            var marker = new LootMarker { Transform = corpse.TrackableTransform != null ? corpse.TrackableTransform : corpse.transform, Name = "[CORPSE] " + (kind==EspKind.Friendly?"FRIENDLY / ":"")+type, Colour = corpseColour.Value,Kind=kind };
            marker.Label.text = marker.Name + " | " + Vector3.Distance(local.Position,marker.Transform.position).ToString("0") + "m";
            pendingCorpses.Add(marker);
        }
        private void DrawMarker(LootMarker marker,float range)
        {
            Vector2 screen;
            if (marker.Transform != null && marker.Transform.gameObject.activeInHierarchy && Vector3.Distance(local.Position,marker.Transform.position)<=range && ScreenPoint(marker.Transform.position,out screen)) Label(screen,marker.Label,marker.Colour);
        }
    }
}
