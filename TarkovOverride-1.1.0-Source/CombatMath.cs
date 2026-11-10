using System;
namespace SPTPracticeMenu
{
    public static class CombatMath
    {
        public static float Reduce(float impulse,float percent)
        {return impulse*(1-Math.Max(0,Math.Min(100,percent))/100f);}
        public static float RescaleHealth(float current,float oldMaximum,float newMaximum,float minimum)
        {
            if(oldMaximum<=0||newMaximum<=0)return minimum;
            float fraction=Math.Max(0,Math.Min(1,current/oldMaximum));
            return Math.Max(minimum,Math.Min(newMaximum,newMaximum*fraction));
        }
    }
}
