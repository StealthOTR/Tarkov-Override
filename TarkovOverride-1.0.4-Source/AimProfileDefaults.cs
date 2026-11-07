namespace SPTPracticeMenu
{
    public enum AimMode { Legit, Rage }
    public enum AimBone { Head, Chest, Pelvis, NearestToCrosshair }
    public enum AimPriority { Crosshair, Distance, LowestHealth }
    public static class AimProfileDefaults
    {
        public static object Value(AimMode mode,string field,object existing)
        {
            bool rage=mode==AimMode.Rage;
            switch(field)
            {
                case "bone":return AimBone.Head;
                case "priority":return AimPriority.Crosshair;
                case "fov":return rage?90f:4f;
                case "aimRange":return rage?500f:150f;
                case "smoothing":return rage?0f:0.25f;
                case "maxTurn":return rage?7200f:90f;
                case "visibility":return true;
                case "sticky":return !rage;
                case "switchDelay":case "correction":return !rage;
                case "switchSeconds":return 0.25f;
                case "correctionAngle":return 2f;
                case "fallback":return true;
                default:return existing; // Keep activation, target filters, exclusions and display preferences.
            }
        }
    }
}
