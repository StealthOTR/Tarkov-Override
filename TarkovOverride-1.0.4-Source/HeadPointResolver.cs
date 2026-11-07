using UnityEngine;
namespace SPTPracticeMenu
{
    public static class HeadPointResolver
    {
        public static bool Finite(Vector3 point)
        {return !float.IsNaN(point.x)&&!float.IsInfinity(point.x)&&!float.IsNaN(point.y)&&!float.IsInfinity(point.y)&&!float.IsNaN(point.z)&&!float.IsInfinity(point.z);}
        public static bool TryResolve(bool hasLiveBone,Vector3 liveBone,bool hasLiveCenter,Vector3 liveCenter,out Vector3 point,out bool usedCenter)
        {
            point=default(Vector3);usedCenter=false;
            // An actual animated head is the anchor. Never guess a standing head height.
            if(!hasLiveBone||!Finite(liveBone))return false;
            point=liveBone;
            if(hasLiveCenter&&Finite(liveCenter))
            {
                float x=liveCenter.x-liveBone.x,y=liveCenter.y-liveBone.y,z=liveCenter.z-liveBone.z;
                if(x*x+y*y+z*z<=0.22f*0.22f){point=liveCenter;usedCenter=true;}
            }
            return true;
        }
    }
}
