using System;
using System.Collections.Generic;
using UnityEngine;
namespace SPTPracticeMenu
{
    public sealed partial class Plugin
    {
        private readonly Dictionary<LootCategory,Texture2D> lootGlyphs=new Dictionary<LootCategory,Texture2D>();
        private void RefreshLootLabel(LootMarker marker)
        {
            string distance=Vector3.Distance(local.Position,marker.Transform.position).ToString("0")+"m";
            marker.Label.text=marker.Name+" | "+distance;
            marker.Compact.text=(marker.Importance==2?"[QUEST] ":marker.Importance==1?"[WISH] ":"")+marker.Category+" | "+distance;
        }
        private bool LootFocused(Vector3 position)
        {return Vector3.Angle(camera.transform.forward,position-camera.transform.position)<=lootFocus.Value;}
        private void DrawLooseMarker(LootMarker marker,bool avoidOverlap=false)
        {
            Vector2 screen;
            if(marker.Transform==null||!marker.Transform.gameObject.activeInHierarchy)return;
            Vector3 position=marker.Transform.position;float distance=Vector3.Distance(local.Position,position);
            if(distance>lootRange.Value||!ScreenPoint(position,out screen))return;
            bool full=!lootDistanceDetail.Value||distance<=lootDetailDistance.Value||LootFocused(position);
            if(full||marker.Importance>0)
            {
                var content=full?marker.Label:marker.Compact;
                if(avoidOverlap)
                {
                    Vector2 size=text.CalcSize(content);Rect rect=new Rect(screen.x-size.x*0.5f-18,screen.y-10,size.x+26,size.y+8);
                    foreach(var previous in occupied)if(rect.Overlaps(previous))return;
                    occupied.Add(rect);
                }
                Label(screen,content,marker.Colour);
                if(lootIcons.Value)DrawLootGlyph(new Vector2(screen.x-text.CalcSize(content).x*0.5f-13,screen.y),marker.Category,marker.Colour);
            }
            else if(lootIcons.Value)DrawLootGlyph(screen,marker.Category,marker.Colour);
            else DrawDiamond(screen,3,marker.Colour);
        }
        private void DrawLootGroups()
        {
            if(!clutter.Value){foreach(var marker in loot)DrawLooseMarker(marker);return;}
            if(Time.unscaledTime>=nextGroups)
            {
                nextGroups=Time.unscaledTime+0.1f;groups.Clear();groupMap.Clear();
                foreach(var marker in loot)
                {
                    Vector2 screen;
                    if(marker.Transform==null||!marker.Transform.gameObject.activeInHierarchy||Vector3.Distance(local.Position,marker.Transform.position)>lootRange.Value||!ScreenPoint(marker.Transform.position,out screen))continue;
                    long cell=((long)(int)(screen.x/clusterSize.Value)<<32)|(uint)(int)(screen.y/clusterSize.Value);
                    LootGroup group;
                    if(!groupMap.TryGetValue(cell,out group))
                    {
                        if(groups.Count==groupPool.Count)groupPool.Add(new LootGroup());
                        group=groupPool[groups.Count];group.Rows.Clear();group.Builder.Length=0;group.Count=0;
                        group.Anchor=marker.Transform;group.Colour=marker.Colour;group.Category=marker.Category;
                        groups.Add(group);groupMap[cell]=group;
                    }
                    group.Rows.Add(marker);group.Count++;
                }
                foreach(var group in groups)
                {
                    if(group.Count==1){group.Content.text="";continue;}
                    bool expanded=!expandLootClusters.Value||LootFocused(group.Anchor.position);
                    if(!expanded)
                    {
                        var first=group.Rows[0];
                        group.Builder.Append(first.Importance==2?"[QUEST] ":first.Importance==1?"[WISH] ":"").Append(group.Count).Append(" items | ").Append(Vector3.Distance(local.Position,group.Anchor.position).ToString("0")).Append("m");
                    }
                    else
                    {
                        int max=expandLootClusters.Value?expandedLootNames.Value:clusterNames.Value;
                        for(int i=0;i<Math.Min(group.Count,max);i++)
                        {if(i>0)group.Builder.Append("\n");group.Builder.Append(group.Rows[i].Label.text);}
                        if(group.Count>max)group.Builder.Append("\n+").Append(group.Count-max).Append(" nearby");
                    }
                    group.Content.text=group.Builder.ToString();
                }
            }
            occupied.Clear();
            foreach(var group in groups)
            {
                if(group.Count==1){DrawLooseMarker(group.Rows[0],true);continue;}
                Vector2 screen;
                if(group.Anchor==null||!group.Anchor.gameObject.activeInHierarchy||!ScreenPoint(group.Anchor.position,out screen))continue;
                Vector2 size=text.CalcSize(group.Content);Rect rect=new Rect(screen.x-size.x/2-18,screen.y-10,size.x+26,size.y+8);
                bool overlaps=false;foreach(var previous in occupied)if(rect.Overlaps(previous)){overlaps=true;break;}
                if(overlaps)continue;occupied.Add(rect);Label(screen,group.Content,group.Colour);
                if(lootIcons.Value)DrawLootGlyph(new Vector2(screen.x-size.x*0.5f-13,screen.y),group.Category,group.Colour);
            }
        }
        private void DrawLootGlyph(Vector2 point,LootCategory category,Color colour)
        {
            Texture2D glyph;
            if(!lootGlyphs.TryGetValue(category,out glyph))
            {
                glyph=new Texture2D(16,16,TextureFormat.RGBA32,false);glyph.hideFlags=HideFlags.HideAndDontSave;
                var pixels=new Color[256];
                for(int y=0;y<16;y++)for(int x=0;x<16;x++)pixels[y*16+x]=new Color(1,1,1,GlyphPixel(category,x,y)?1:0);
                glyph.SetPixels(pixels);glyph.Apply(false,true);lootGlyphs[category]=glyph;menuTextures.Add(glyph);
            }
            Color previous=GUI.color;GUI.color=colour;GUI.DrawTexture(new Rect(point.x-7,point.y-7,14,14),glyph);GUI.color=previous;
        }
        private static bool GlyphPixel(LootCategory kind,int x,int y)
        {
            int dx=Math.Abs(x-8),dy=Math.Abs(y-8);
            switch(kind)
            {
                case LootCategory.Ammo:return ((x>=3&&x<=5)||(x>=9&&x<=11))&&y>=3&&y<=11||((x==4||x==10)&&y==12);
                case LootCategory.Keys:
                    int ring=(x-4)*(x-4)+(y-10)*(y-10);
                    return (ring>=4&&ring<=10)||(x>=6&&x<=13&&y>=8&&y<=9)||((x==10||x==13)&&y>=6&&y<=8);
                case LootCategory.Medical:return (dx<=1&&dy<=6)||(dy<=1&&dx<=6);
                case LootCategory.Valuables:return dx+dy>=5&&dx+dy<=7;
                case LootCategory.Attachments:return (dx*dx+dy*dy>=18&&dx*dx+dy*dy<=30)||(dx==0&&dy<=7)||(dy==0&&dx<=7);
                case LootCategory.Weapons:return (x>=2&&x<=13&&y>=8&&y<=10)||(x>=4&&x<=6&&y>=4&&y<=7)||(x>=10&&x<=12&&y>=10&&y<=12);
                case LootCategory.Equipment:return ((x==3||x==12)&&y>=6&&y<=12)||(y==12&&x>=3&&x<=12)||(y>=2&&y<=6&&Math.Abs(x-8)==y-2);
                case LootCategory.Provisions:return ((x==4||x==11)&&y>=3&&y<=12)||((y==3||y==12)&&x>=4&&x<=11);
                case LootCategory.Barter:return (x>=2&&x<=7&&(y==3||y==8))||(y>=3&&y<=8&&(x==2||x==7))||(x>=8&&x<=13&&(y==7||y==12))||(y>=7&&y<=12&&(x==8||x==13));
                default:return dx+dy<=3;
            }
        }
    }
}
