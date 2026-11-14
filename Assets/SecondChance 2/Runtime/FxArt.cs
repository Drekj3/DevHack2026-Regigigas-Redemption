using System;
using UnityEngine;
using UnityEngine.Rendering;
namespace SecondChance
{
    // Procedural ability art: textures, transparent materials and particle systems generated at startup, no imported assets.
    public static class FxArt
    {
        public static Texture2D Dot, Ring, Starburst, Flame, Lava;
        public static void Build()
        {
            if(Dot!=null)return;
            Dot=Tex(64,64,(u,v)=>new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-R(u,v)),2.2f)));
            Ring=Tex(128,128,(u,v)=>{float d=R(u,v);return new Color(1,1,1,Mathf.Clamp01(Mathf.Exp(-Sq((d-.62f)/.1f))+Mathf.Pow(Mathf.Clamp01(1-d*2.2f),2)));});
            Starburst=Tex(256,256,(u,v)=>
            {
                float d=R(u,v),a=Mathf.Atan2(v-.5f,u-.5f);
                float rays=Mathf.Pow(Mathf.Abs(Mathf.Sin(a*9)),24)*.7f+Mathf.Pow(Mathf.Abs(Mathf.Sin(a*14+1.3f)),40)*.6f+Mathf.Pow(Mathf.Abs(Mathf.Sin(a*5+.4f)),60)*.8f;
                return new Color(1,1,1,Mathf.Clamp01(Mathf.Pow(Mathf.Clamp01(1-d*1.6f),3)*1.5f+rays*Mathf.Clamp01(1-d)));
            });
            // u runs along the wall (tiles seamlessly), v runs up: white-hot base, orange body, crimson tongues that fade out.
            Flame=Tex(128,128,(u,v)=>
            {
                float n=Seam(u,v*3,4)*.7f+Seam(u,v*6+9,9)*.3f;
                Color c=v<.15f?Color.Lerp(new Color(1,.97f,.8f),new Color(1,.6f,.2f),v/.15f):Color.Lerp(new Color(1,.45f,.25f),new Color(.95f,.22f,.3f),(v-.15f)/.6f);
                c.a=Mathf.Clamp01((.45f+.45f*n-v)*5f)*.95f;return c;
            },true);
            Lava=Tex(128,128,(u,v)=>
            {
                float d=R(u,v),n=Mathf.PerlinNoise(u*6+3,v*6+3),vein=1-Mathf.Abs(Mathf.PerlinNoise(u*9+7,v*9+1)*2-1);
                Color c=Color.Lerp(new Color(.75f,.12f,.12f),new Color(1,.42f,.2f),n);
                c=Color.Lerp(c,new Color(1,.85f,.55f),Mathf.SmoothStep(0,1,Mathf.InverseLerp(.82f,.97f,vein)));
                c.a=Mathf.Clamp01((1-d+(n-.5f)*.25f)*6f)*.9f;return c;
            });
        }
        static float Sq(float x)=>x*x;
        static float R(float u,float v)=>Vector2.Distance(new Vector2(u,v),new Vector2(.5f,.5f))*2;
        // Perlin blended with itself one period over, so the texture repeats along u without a seam.
        static float Seam(float u,float y,float freq)=>Mathf.Lerp(Mathf.PerlinNoise(u*freq+10,y),Mathf.PerlinNoise((u-1)*freq+10,y),u);
        static Texture2D Tex(int w,int h,Func<float,float,Color> f,bool repeatU=false)
        {
            var t=new Texture2D(w,h,TextureFormat.RGBA32,true){wrapModeU=repeatU?TextureWrapMode.Repeat:TextureWrapMode.Clamp,wrapModeV=TextureWrapMode.Clamp};
            var px=new Color[w*h];
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)px[y*w+x]=f((x+.5f)/w,(y+.5f)/h);
            t.SetPixels(px);t.Apply();return t;
        }
        // ponytail: runtime-configured URP transparent materials; a build may strip the transparent variant, add a shader variant collection if so.
        public static Material Mat(Texture tex,Color c,bool additive,bool particles=false)
        {
            var m=new Material(Shader.Find(particles?"Universal Render Pipeline/Particles/Unlit":"Universal Render Pipeline/Unlit")){color=c,mainTexture=tex};
            float dst=(float)(additive?BlendMode.One:BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Surface",1);m.SetFloat("_Blend",additive?2:0);m.SetFloat("_Cull",0);m.SetFloat("_ZWrite",0);
            m.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);m.SetFloat("_DstBlend",dst);
            m.SetFloat("_SrcBlendAlpha",(float)BlendMode.One);m.SetFloat("_DstBlendAlpha",dst);
            m.SetOverrideTag("RenderType","Transparent");m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue=(int)RenderQueue.Transparent;
            return m;
        }
        // World-space, rising, fading particles. rate 0 = emit manually with ps.Emit.
        public static ParticleSystem Particles(Transform parent,Material m,Color a,Color b,float life,float size,float speed,float rate,float gravity=-.2f)
        {
            var g=new GameObject("Particles");g.transform.SetParent(parent,false);
            var ps=g.AddComponent<ParticleSystem>();
            ps.Stop(true,ParticleSystemStopBehavior.StopEmittingAndClear);
            var main=ps.main;
            main.startLifetime=new ParticleSystem.MinMaxCurve(life*.6f,life);
            main.startSize=new ParticleSystem.MinMaxCurve(size*.5f,size);
            main.startSpeed=new ParticleSystem.MinMaxCurve(0,speed);
            main.startColor=new ParticleSystem.MinMaxGradient(a,b);
            main.gravityModifier=gravity;
            main.simulationSpace=ParticleSystemSimulationSpace.World;
            main.maxParticles=800;
            var em=ps.emission;em.rateOverTime=rate;
            var sh=ps.shape;sh.shapeType=ParticleSystemShapeType.Sphere;sh.radius=.08f;
            var col=ps.colorOverLifetime;col.enabled=true;
            var grad=new Gradient();
            grad.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(new Color(1,.55f,.5f),1)},
                new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(.8f,.4f),new GradientAlphaKey(0,1)});
            col.color=grad;
            var sz=ps.sizeOverLifetime;sz.enabled=true;
            sz.size=new ParticleSystem.MinMaxCurve(1,new AnimationCurve(new Keyframe(0,.5f),new Keyframe(.25f,1),new Keyframe(1,0)));
            g.GetComponent<ParticleSystemRenderer>().sharedMaterial=m;
            ps.Play();
            return ps;
        }
    }
}
