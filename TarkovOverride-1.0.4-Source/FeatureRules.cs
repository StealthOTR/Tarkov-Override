using System;

namespace SPTPracticeMenu
{
    public enum LootCategory { Ammo, Valuables, Keys, Medical, Attachments, Weapons, Equipment, Provisions, Barter, Other }
    public enum LootPreset { All, CustomCategories, QuestRun, Wishlist, Ammo, Valuables, Medical, Attachments, Keys }
    public static class FeatureRules
    {
        // These taxonomy roots were checked against the installed SPT 4.1.6 database.
        public static LootCategory? CategoryRoot(string id)
        {
            switch (id)
            {
                case "5485a8684bdc2da71d8b4567": case "543be5cb4bdc2deb348b4568": return LootCategory.Ammo;
                case "57864a3d24597754843f8721": case "543be5dd4bdc2deb348b4569": return LootCategory.Valuables;
                case "543be5e94bdc2df1348b4568": return LootCategory.Keys;
                case "543be5664bdc2dd4348b4569": case "57864c8c245977548867e7f1": return LootCategory.Medical;
                case "5448fe124bdc2da5018b4567": return LootCategory.Attachments;
                case "5422acb9af1c889c16000029": return LootCategory.Weapons;
                case "543be5f84bdc2dd4348b456a": case "5448e54d4bdc2dcc718b4568": case "57bef4c42459772e8d35a53b": case "644120aa86ffbe10ee032b6f": return LootCategory.Equipment;
                case "543be6674bdc2df1348b4569": return LootCategory.Provisions;
                case "5448eb774bdc2d0a728b4567": return LootCategory.Barter;
                default: return null;
            }
        }
        public static bool AllowPreset(LootPreset preset, LootCategory category, bool quest, bool wish, bool custom)
        {
            switch (preset)
            {
                case LootPreset.All: return true;
                case LootPreset.CustomCategories: return custom;
                case LootPreset.QuestRun: return quest || wish || category == LootCategory.Keys || category == LootCategory.Medical;
                case LootPreset.Wishlist: return wish;
                case LootPreset.Ammo: return category == LootCategory.Ammo;
                case LootPreset.Valuables: return category == LootCategory.Valuables;
                case LootPreset.Medical: return category == LootCategory.Medical;
                case LootPreset.Attachments: return category == LootCategory.Attachments;
                case LootPreset.Keys: return category == LootCategory.Keys;
                default: return false;
            }
        }
        public static bool MatchesTokens(string tokens, string type, string role, string profileId)
        { return MatchesTokens(string.IsNullOrWhiteSpace(tokens) ? new string[0] : tokens.Split(','),type,role,profileId); }
        public static bool MatchesTokens(string[] tokens, string type, string role, string profileId)
        {
            foreach (string part in tokens)
            {
                string term = part.Trim();
                if (term.Length > 0 && (string.Equals(term, type, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(term, role, StringComparison.OrdinalIgnoreCase) || string.Equals(term, profileId, StringComparison.OrdinalIgnoreCase))) return true;
            }
            return false;
        }
        public static bool IncludeItem(LootPreset preset,LootCategory category,bool quest,bool wish,bool custom,
            bool nameMatches,bool highlightQuest,bool highlightWish,bool bypass)
        {
            bool important=(quest&&highlightQuest)||(wish&&highlightWish);
            return (bypass && important && preset!=LootPreset.Wishlist) ||
                (AllowPreset(preset,category,quest,wish,custom) && nameMatches);
        }
    }
}
