using UnityEngine;
namespace SecondChance
{
    // Immediate-mode presentation layer; no packages, font downloads or assets needed.
    public static class TacticalUI
    {
        public static readonly Color Ink=new Color(.035f,.065f,.09f,.97f);
        public static readonly Color Panel=new Color(.07f,.105f,.135f,.96f);
        public static readonly Color Line=new Color(.24f,.32f,.36f,.8f);
        public static readonly Color White=new Color(.93f,.94f,.89f);
        public static readonly Color Muted=new Color(.57f,.67f,.70f);
        public static readonly Color Coral=new Color(1f,.28f,.33f);
        public static readonly Color Mint=new Color(.35f,.89f,.80f);
        static GUIStyle click,track,thumb;
        public static void Fill(Rect r,Color c){Color old=GUI.color;GUI.color=c;GUI.DrawTexture(r,Texture2D.whiteTexture);GUI.color=old;}
        public static void Border(Rect r,Color c){Fill(new Rect(r.x,r.y,r.width,1),c);Fill(new Rect(r.x,r.yMax-1,r.width,1),c);Fill(new Rect(r.x,r.y,1,r.height),c);Fill(new Rect(r.xMax-1,r.y,1,r.height),c);}
        public static void Text(Rect r,string text,int size=16,Color? color=null,bool bold=false,TextAnchor align=TextAnchor.MiddleLeft)
        {
            var style=new GUIStyle(GUI.skin.label){fontSize=size,fontStyle=bold?FontStyle.Bold:FontStyle.Normal,alignment=align,wordWrap=true,padding=new RectOffset(0,0,0,0)};
            style.normal.textColor=color??White;GUI.Label(r,text,style);
        }
        public static void Label(float x,float y,string text){Text(new Rect(x,y,340,22),text,12,Muted,true);}
        public static void Card(Rect r){Fill(r,Panel);Border(r,Line);}
        public static bool Button(Rect r,string text,bool primary=false,bool selected=false)
        {
            if(click==null)click=new GUIStyle();
            bool hover=r.Contains(Event.current.mousePosition)&&GUI.enabled;
            Color bg=primary?Coral:selected?new Color(.13f,.25f,.27f):Panel;
            if(hover)bg=Color.Lerp(bg,White,.13f);if(!GUI.enabled)bg=Color.Lerp(bg,Ink,.7f);
            Fill(r,bg);Border(r,primary?Coral:selected?Mint:Line);
            if(selected)Fill(new Rect(r.x,r.y,3,r.height),Mint);
            Text(new Rect(r.x+15,r.y,r.width-30,r.height),text,r.width<130?11:15,GUI.enabled?White:Muted,true,TextAnchor.MiddleCenter);
            return GUI.Button(r,GUIContent.none,click);
        }
        public static float Slider(Rect r,float value,float min,float max)
        {
            if(track==null){track=new GUIStyle(){fixedHeight=20};thumb=new GUIStyle(){fixedWidth=16,fixedHeight=20};}
            Fill(new Rect(r.x,r.y+9,r.width,3),Line);
            float fraction=Mathf.InverseLerp(min,max,value);
            Fill(new Rect(r.x,r.y+9,r.width*fraction,3),Mint);
            Fill(new Rect(r.x+(r.width-16)*fraction,r.y+3,16,16),White);
            return GUI.HorizontalSlider(r,value,min,max,track,thumb);
        }
        public static void Bar(Rect r,float value,Color color){Fill(r,Line);Fill(new Rect(r.x,r.y,r.width*Mathf.Clamp01(value),r.height),color);}
    }
}
