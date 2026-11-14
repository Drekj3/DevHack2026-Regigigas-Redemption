using UnityEngine;
namespace SecondChance
{
    // Original Mediterranean courtyard, built entirely from local geometry.
    public static class PracticeWorld
    {
        public static Material plaster, stone, teal, dark, brass, roof, foliage, red, ivory, glow;
        static Transform root;
        public static Material Mat(string name, Color color, float metallic = 0f)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var m = new Material(shader) { name = name, color = color };
            m.SetFloat("_Metallic", metallic); m.SetFloat("_Smoothness", .28f);
            return m;
        }
        public static GameObject Part(string name, Vector3 pos, Vector3 size, Material mat, Transform parent = null, bool solid = true, PrimitiveType type = PrimitiveType.Cube)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name;
            g.transform.SetParent(parent == null ? root : parent, false);
            g.transform.localPosition = pos; g.transform.localScale = size;
            g.GetComponent<Renderer>().sharedMaterial = mat;
            if (!solid) { g.GetComponent<Collider>().enabled = false; Object.Destroy(g.GetComponent<Collider>()); g.layer = 2; }
            return g;
        }
        static void Sign(string text, Vector3 pos, float yaw, float size)
        {
            var go = new GameObject("Sign " + text); go.transform.SetParent(root);
            go.transform.position = pos; go.transform.rotation = Quaternion.Euler(0,yaw,0);
            var t = go.AddComponent<TextMesh>(); t.text = text; t.fontSize = 80;
            t.characterSize = size; t.anchor = TextAnchor.MiddleCenter; t.color = new Color(.12f,.25f,.27f);
        }
        public static Transform Build(Transform parent)
        {
            root = new GameObject("Courtyard - original architecture").transform; root.SetParent(parent);
            plaster = Mat("Warm limestone", new Color(.83f,.75f,.60f));
            stone = Mat("Pale paving", new Color(.48f,.52f,.49f));
            teal = Mat("Oxidised teal", new Color(.12f,.36f,.36f),.3f);
            dark = Mat("Gunmetal", new Color(.055f,.075f,.09f),.65f);
            brass = Mat("Warm metal", new Color(.83f,.53f,.22f),.6f);
            roof = Mat("Terracotta", new Color(.60f,.25f,.17f));
            foliage = Mat("Cypress green", new Color(.16f,.29f,.16f));
            red = Mat("Opponent coral", new Color(.85f,.20f,.19f));
            ivory = Mat("Ceramic rifle shell", new Color(.86f,.89f,.85f),.25f);
            glow = Mat("Cyan accent", new Color(.12f,.85f,.8f),.4f);
            glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor",new Color(.05f,.7f,.65f)*1.5f);
            Part("Paving",new Vector3(0,-.2f,0),new Vector3(36,.4f,44),stone);
            // Paving joints remain decorative so movement stays smooth.
            for(int z=-21;z<22;z+=3) Part("Paving joint",new Vector3(0,.006f,z),new Vector3(36,.008f,.025f),plaster,null,false);
            for(int x=-18;x<=18;x+=3) Part("Paving joint",new Vector3(x,.006f,0),new Vector3(.025f,.008f,44),plaster,null,false);
            Building(new Vector3(-18,0,0),new Vector3(3,8,44));
            Building(new Vector3(18,0,0),new Vector3(3,9,44));
            Building(new Vector3(0,0,22),new Vector3(36,8,3));
            Building(new Vector3(0,0,-22),new Vector3(36,7,3));
            // Raised upper facades add skyline detail without blocking the arena.
            Building(new Vector3(-11,0,20),new Vector3(8,11,4));
            Building(new Vector3(12,0,-20),new Vector3(8,10,4));
            // Central arcade: open doorway, two pillars, curved lintel.
            Part("Arcade west",new Vector3(-5,2.5f,1),new Vector3(5,5,2),plaster);
            Part("Arcade east",new Vector3(5,2.5f,1),new Vector3(5,5,2),plaster);
            for(int i=0;i<11;i++)
            {
                float angle = i*18f*Mathf.Deg2Rad;
                var b=Part("Arch stone",new Vector3(Mathf.Cos(angle)*2.4f,3.1f+Mathf.Sin(angle)*2.4f,1),new Vector3(.8f,.65f,2.1f),plaster);
                b.transform.localRotation=Quaternion.Euler(0,0,i*18f+90);
            }
            Part("Arcade cornice",new Vector3(0,6.1f,1),new Vector3(15.5f,.35f,2.5f),roof);
            Sign("PIAZZA  /  A",new Vector3(0,5.7f,-.08f),0,.055f);
            // Staggered cover leaves the center and both flank routes usable.
            Crate(new Vector3(-8,0,-8),new Vector3(3.6f,2.4f,3));
            Crate(new Vector3(7,0,10),new Vector3(3.4f,2.4f,3));
            Crate(new Vector3(11,0,-4),new Vector3(2.8f,2.1f,3));
            Crate(new Vector3(-10,0,12),new Vector3(3,2.2f,3));
            // Fountain and basin.
            Part("Fountain basin",new Vector3(0,.35f,12),new Vector3(4.5f,.7f,4.5f),plaster);
            Part("Water",new Vector3(0,.72f,12),new Vector3(3.8f,.04f,3.8f),teal,null,false);
            Part("Fountain pedestal",new Vector3(0,1.3f,12),new Vector3(.65f,2.6f,.65f),plaster);
            Part("Fountain bowl",new Vector3(0,2.3f,12),new Vector3(2,.2f,2),plaster,null,true,PrimitiveType.Cylinder);
            for(int i=0;i<6;i++)
            {
                float z=-16+i*6;
                Window(new Vector3(-16.45f,4.8f,z),90);
                Window(new Vector3(16.45f,5.2f,z),-90);
            }
            for(int x=-12;x<=12;x+=6) Window(new Vector3(x,5.4f,20.45f),0);
            foreach(var p in new[]{new Vector3(-14,0,-17),new Vector3(14,0,17),new Vector3(-14,0,17)})
            {
                Part("Planter",p+Vector3.up*.4f,new Vector3(1.7f,.8f,1.7f),plaster);
                Part("Tree trunk",p+Vector3.up*1.4f,new Vector3(.3f,2,.3f),roof);
                Part("Cypress crown",p+Vector3.up*3,new Vector3(1.3f,4,1.3f),foliage,null,false,PrimitiveType.Sphere);
            }
            // Overhead cables and cloth panels.
            for(int i=0;i<7;i++)
                Part("Sunshade",new Vector3(-10+i*1.1f,6.7f,-14),new Vector3(.9f,.06f,4),i%2==0?teal:plaster,null,false);
            Sign("A",new Vector3(6,1.6f,-.06f),0,.32f);
            Sign("SECOND CHANCE",new Vector3(0,4,-20.45f),180,.07f);
            // Low-cost courtyard dressing, all decorative pieces are non-colliding.
            for(int x=-12;x<=12;x+=4)
            {
                Part("Drain grate",new Vector3(x,.015f,-18),new Vector3(1.1f,.02f,.35f),dark,null,false);
                for(int j=0;j<5;j++)Part("Grate slat",new Vector3(x-.4f+j*.2f,.03f,-18),new Vector3(.06f,.02f,.32f),brass,null,false);
            }
            foreach(float x in new[]{-15.8f,15.8f})for(int z=-12;z<=12;z+=12)
            {
                Part("Lantern bracket",new Vector3(x,3.5f,z),new Vector3(.6f,.08f,.08f),dark,null,false);
                Part("Lantern frame",new Vector3(x,3.15f,z),new Vector3(.3f,.55f,.3f),dark,null,false);
                Part("Lantern glass",new Vector3(x,3.15f,z-.16f),new Vector3(.22f,.4f,.025f),brass,null,false);
            }
            // Site border and stencilled route directions.
            for(int side=-1;side<=1;side+=2)
            {
                Part("Site boundary",new Vector3(side*5,.02f,12),new Vector3(.07f,.015f,10),brass,null,false);
                Part("Site boundary",new Vector3(0,.02f,12+side*5),new Vector3(10,.015f,.07f),brass,null,false);
            }
            Sign("MARKET  >",new Vector3(-5,2.8f,-.06f),0,.05f);
            Sign("01",new Vector3(7,1.4f,8.46f),0,.09f);
            var sun=new GameObject("Late afternoon sun").AddComponent<Light>(); sun.transform.SetParent(root);
            sun.type=LightType.Directional; sun.intensity=2f; sun.color=new Color(1,.88f,.72f); sun.shadows=LightShadows.Soft;
            sun.transform.rotation=Quaternion.Euler(48,-35,0);
            RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor=new Color(.53f,.66f,.8f);
            RenderSettings.ambientEquatorColor=new Color(.48f,.47f,.43f);
            RenderSettings.ambientGroundColor=new Color(.25f,.25f,.24f);
            RenderSettings.fog=true; RenderSettings.fogColor=new Color(.68f,.76f,.80f); RenderSettings.fogMode=FogMode.Linear;
            RenderSettings.fogStartDistance=45; RenderSettings.fogEndDistance=110;
            return root;
        }
        static void Building(Vector3 basePos, Vector3 size)
        {
            Part("Plaster facade",basePos+Vector3.up*size.y/2,size,plaster);
            Part("Foundation trim",basePos+Vector3.up*.35f,new Vector3(size.x+.12f,.7f,size.z+.12f),teal);
            Part("Roof cornice",basePos+Vector3.up*size.y,new Vector3(size.x+.5f,.35f,size.z+.5f),roof);
            Part("Upper moulding",basePos+Vector3.up*(size.y-.5f),new Vector3(size.x+.2f,.18f,size.z+.2f),plaster);
            for(float x=-size.x/2;x<size.x/2;x+=.65f)
                Part("Roof tile ridge",basePos+new Vector3(x,size.y+.23f,0),new Vector3(.14f,.14f,size.z+.3f),roof,null,false);
        }
        static void Window(Vector3 p,float yaw)
        {
            var r=new GameObject("Shuttered window").transform; r.SetParent(root); r.position=p; r.rotation=Quaternion.Euler(0,yaw,0);
            Part("Frame",Vector3.zero,new Vector3(1.8f,2.4f,.14f),plaster,r,false);
            Part("Shutters",new Vector3(0,0,-.1f),new Vector3(1.5f,2.1f,.15f),teal,r,false);
            for(int j=-4;j<=4;j++) Part("Shutter slat",new Vector3(0,j*.21f,-.2f),new Vector3(1.45f,.05f,.06f),dark,r,false);
            Part("Window sill",new Vector3(0,-1.25f,-.15f),new Vector3(2,.18f,.5f),plaster,r,false);
        }
        static void Crate(Vector3 p,Vector3 size)
        {
            Part("Tactical crate",p+Vector3.up*size.y/2,size,teal);
            for(int side=-1;side<=1;side+=2)
            {
                Part("Crate rim",p+new Vector3(0,size.y/2,side*(size.z/2+.015f)),new Vector3(size.x,.13f,.05f),brass,null,false);
                var brace=Part("Diagonal brace",p+new Vector3(0,size.y/2,side*(size.z/2+.04f)),new Vector3(size.x*.85f,.12f,.05f),dark,null,false);
                brace.transform.rotation=Quaternion.Euler(0,0,30);
            }
        }
        public static Transform Rifle(Transform parent)
        {
            var r=new GameObject("Sentinel rifle - original mesh assembly").transform; r.SetParent(parent,false);
            Part("Receiver",Vector3.zero,new Vector3(.14f,.18f,.48f),dark,r,false);
            foreach(int side in new[]{-1,1})
            {
                Part("Ceramic receiver shell",new Vector3(side*.078f,.015f,-.02f),new Vector3(.022f,.13f,.34f),ivory,r,false);
                Part("Energy seam",new Vector3(side*.092f,.045f,-.01f),new Vector3(.008f,.015f,.28f),glow,r,false);
                Part("Gold receiver trim",new Vector3(side*.09f,-.055f,-.02f),new Vector3(.012f,.018f,.36f),brass,r,false);
                for(int j=0;j<3;j++)Part("Receiver fastener",new Vector3(side*.095f,-.015f,-.12f+j*.11f),new Vector3(.012f,.025f,.025f),dark,r,false);
            }
            Part("Handguard",new Vector3(0,.01f,.34f),new Vector3(.12f,.14f,.34f),teal,r,false);
            Part("Barrel",new Vector3(0,.025f,.62f),new Vector3(.045f,.045f,.3f),dark,r,false);
            Part("Muzzle brake",new Vector3(0,.025f,.79f),new Vector3(.075f,.065f,.07f),dark,r,false);
            Part("Stock",new Vector3(0,-.01f,-.33f),new Vector3(.12f,.16f,.22f),ivory,r,false);
            Part("Rubber stock pad",new Vector3(0,-.02f,-.46f),new Vector3(.14f,.2f,.055f),dark,r,false);
            Part("Charging handle",new Vector3(.12f,.045f,-.1f),new Vector3(.1f,.035f,.055f),brass,r,false);
            for(int j=0;j<4;j++)Part("Handguard rib",new Vector3(0,.01f,.23f+j*.075f),new Vector3(.135f,.155f,.018f),ivory,r,false);
            Part("Reflex optic base",new Vector3(0,.15f,-.05f),new Vector3(.1f,.045f,.13f),dark,r,false);
            Part("Optic left",new Vector3(-.045f,.21f,-.05f),new Vector3(.015f,.1f,.05f),ivory,r,false);
            Part("Optic right",new Vector3(.045f,.21f,-.05f),new Vector3(.015f,.1f,.05f),ivory,r,false);
            Part("Optic bridge",new Vector3(0,.26f,-.05f),new Vector3(.105f,.015f,.05f),ivory,r,false);
            var mag=Part("Magazine",new Vector3(0,-.2f,.02f),new Vector3(.09f,.29f,.13f),dark,r,false); mag.transform.localRotation=Quaternion.Euler(-12,0,0);
            Part("Grip",new Vector3(0,-.18f,-.15f),new Vector3(.09f,.22f,.1f),dark,r,false);
            Part("Top rail",new Vector3(0,.12f,.08f),new Vector3(.055f,.03f,.46f),brass,r,false);
            Part("Front sight",new Vector3(0,.15f,.46f),new Vector3(.025f,.09f,.025f),dark,r,false);
            Part("Rear sight",new Vector3(0,.16f,-.12f),new Vector3(.07f,.08f,.035f),dark,r,false);
            for(int i=0;i<5;i++) Part("Vent",new Vector3(.063f,.04f,.23f+i*.055f),new Vector3(.01f,.055f,.025f),dark,r,false);
            return r;
        }
    }
}
