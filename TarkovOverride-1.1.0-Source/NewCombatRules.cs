using System;
namespace SPTPracticeMenu
{
    public enum DirectBodyPart {Head,Thorax,Stomach,LeftArm,RightArm,LeftLeg,RightLeg}
    public static class NewCombatRules
    {
        public static int BodyIndex(DirectBodyPart part)
        {int value=(int)part;if(value<0||value>6)throw new ArgumentOutOfRangeException("part");return value;}
        public static bool Finite(float value){return !float.IsNaN(value)&&!float.IsInfinity(value);}
        public static float Healing(float before,float after,float percentage,float killBonus,bool killed,float cap)
        {
            if(!Finite(before)||!Finite(after)||!Finite(percentage)||!Finite(killBonus)||!Finite(cap)||before<=0||cap<=0)return 0;
            float lost=Math.Max(0,before-Math.Max(0,after));
            if(lost<=0)return 0;
            return Math.Min(cap,lost*Math.Max(0,Math.Min(200,percentage))/100f+(killed?Math.Max(0,killBonus):0));
        }
    }
}
