using System;
namespace SPTPracticeMenu
{
    public static class PredictionMath
    {
        private static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
        public static bool TryOffset(float rx,float ry,float rz,float vx,float vy,float vz,float speed,float gravity,float maximum,
            out float x,out float y,out float z,out float time)
        {
            x=y=z=time=0;
            if(!Finite(rx)||!Finite(ry)||!Finite(rz)||!Finite(vx)||!Finite(vy)||!Finite(vz)||!Finite(speed)||speed<1||!Finite(maximum)||maximum<=0||!Finite(gravity)||gravity<0)return false;
            double a=(double)vx*vx+(double)vy*vy+(double)vz*vz-(double)speed*speed;
            double b=2*((double)rx*vx+(double)ry*vy+(double)rz*vz),c=(double)rx*rx+(double)ry*ry+(double)rz*rz,t;
            if(c<0.0001)return false;
            if(Math.Abs(a)<0.00001){if(Math.Abs(b)<0.00001)return false;t=-c/b;}
            else
            {
                double discriminant=b*b-4*a*c;if(discriminant<0)return false;
                double root=Math.Sqrt(discriminant),one=(-b-root)/(2*a),two=(-b+root)/(2*a);
                t=one>0&&(two<=0||one<two)?one:two;
            }
            if(!Finite(t)||t<=0||t>maximum)return false;
            // Refine the coupled lead/drop solution without physics casts or projectile creation.
            for(int i=0;i<4;i++)
            {
                double px=rx+vx*t,py=ry+vy*t+0.5*gravity*t*t,pz=rz+vz*t;
                t=Math.Sqrt(px*px+py*py+pz*pz)/speed;
                if(!Finite(t)||t<=0||t>maximum)return false;
            }
            x=(float)(vx*t);y=(float)(vy*t+0.5*gravity*t*t);z=(float)(vz*t);time=(float)t;
            return Finite(x)&&Finite(y)&&Finite(z);
        }
    }
}
