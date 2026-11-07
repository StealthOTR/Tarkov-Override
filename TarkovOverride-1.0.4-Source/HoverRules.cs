namespace SPTPracticeMenu
{
    public static class HoverRules
    {
        public static bool Hit(float mouseX,float mouseY,float x,float y,float width,float height,float clipX,float clipY,float clipWidth,float clipHeight)
        {
            return mouseX>=x&&mouseX<x+width&&mouseY>=y&&mouseY<y+height&&
                mouseX>=clipX&&mouseX<clipX+clipWidth&&mouseY>=clipY&&mouseY<clipY+clipHeight;
        }
    }
}
