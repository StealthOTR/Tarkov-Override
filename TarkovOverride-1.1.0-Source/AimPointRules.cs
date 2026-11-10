using System;
namespace SPTPracticeMenu
{
    public static class AimPointRules
    {
        public static AimBone Selected(AimBone requested,bool strict){return strict?AimBone.Head:requested;}
        public static bool IsHeadName(string name)
        {return !string.IsNullOrEmpty(name)&&(name.Equals("Head",StringComparison.OrdinalIgnoreCase)||name.EndsWith("HumanHead",StringComparison.OrdinalIgnoreCase));}
        public static int Count(AimBone selected,bool fallback,bool visibility,bool strictHead)
        {
            if(selected==AimBone.NearestToCrosshair)return 3;
            return fallback&&visibility&&!(strictHead&&selected==AimBone.Head)?3:1;
        }
        public static AimBone At(AimBone selected,int index)
        {
            if(index<0||index>2)throw new ArgumentOutOfRangeException("index");
            if(selected==AimBone.NearestToCrosshair)return (AimBone)index;
            if(index==0)return selected;
            if(selected==AimBone.Head)return index==1?AimBone.Chest:AimBone.Pelvis;
            if(selected==AimBone.Chest)return index==1?AimBone.Head:AimBone.Pelvis;
            return index==1?AimBone.Chest:AimBone.Head;
        }
    }
}
