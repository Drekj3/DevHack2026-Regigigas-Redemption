using UnityEngine;
namespace SecondChance
{
    // Original Mediterranean courtyard, built entirely from local geometry.
    public static class PracticeWorld
    {
        public static Material plaster, stone, teal, dark, brass, roof, foliage, red, ivory, glow, plasterB, plasterC, inlay, wood, lamp;
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
            if (t.font == null) t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = TextMaterial(t.font);
        }
        // The built-in text material ignores depth, so signs showed through walls. This one depth-tests.
        static Material textMat;
        static Material TextMaterial(Font font)
        {
            if (textMat == null)
            {
                textMat = new Material(Resources.Load<Shader>("WorldText")) { mainTexture = font.material.mainTexture };
                // Dynamic fonts can rebuild their atlas; keep pointing at the current one.
                Font.textureRebuilt += f => { if (f == font && textMat != null) textMat.mainTexture = f.material.mainTexture; };
            }
            return textMat;
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
            plasterB = Mat("Peach plaster", new Color(.88f,.66f,.50f));
            plasterC = Mat("Cream plaster", new Color(.93f,.87f,.73f));
            inlay = Mat("Dark stone inlay", new Color(.36f,.36f,.34f));
            wood = Mat("Weathered wood", new Color(.42f,.29f,.18f));
            lamp = Mat("Warm bulb", new Color(1,.85f,.55f));
            lamp.EnableKeyword("_EMISSION"); lamp.SetColor("_EmissionColor",new Color(1,.7f,.35f)*2f);
            // Layout (x east, z north): south spawn plaza, bell-tower mid between two blocks with vaulted
            // passages (the chokes), west alley, east market street, north fountain plaza = site A.
            Part("Paving",new Vector3(0,-.2f,0),new Vector3(36,.4f,44),stone);
            // Paving joints remain decorative so movement stays smooth.
            for(int z=-21;z<22;z+=3) Part("Paving joint",new Vector3(0,.006f,z),new Vector3(36,.008f,.025f),plaster,null,false);
            for(int x=-18;x<=18;x+=3) Part("Paving joint",new Vector3(x,.006f,0),new Vector3(.025f,.008f,44),plaster,null,false);
            Part("Inlay band",new Vector3(0,.009f,-12),new Vector3(33,.01f,.4f),inlay,null,false);
            Part("Inlay band",new Vector3(0,.009f,8),new Vector3(33,.01f,.4f),inlay,null,false);
            Part("Inlay band",new Vector3(0,.009f,-2),new Vector3(.4f,.01f,20),inlay,null,false);
            // Perimeter in varied heights and colours so the skyline reads as a town, not a box.
            Building(new Vector3(-18,0,-11),new Vector3(3,8,22),plaster);
            Building(new Vector3(-18,0,11),new Vector3(3,10,22),plasterB);
            Building(new Vector3(18,0,-11),new Vector3(3,9,22),plasterC);
            Building(new Vector3(18,0,11),new Vector3(3,7.5f,22),plaster);
            Building(new Vector3(-9,0,22),new Vector3(18,9,3),plasterC);
            Building(new Vector3(9,0,22),new Vector3(18,11,3),plasterB);
            Building(new Vector3(-9,0,-22),new Vector3(18,7,3),plasterB);
            Building(new Vector3(9,0,-22),new Vector3(18,8,3),plaster);
            // West block with a vaulted passage: alley <-> mid choke.
            Building(new Vector3(-7.5f,0,-5.6f),new Vector3(5,7,8.8f),plasterB);
            Building(new Vector3(-7.5f,0,4.1f),new Vector3(5,9,5.8f),plaster);
            Passage(new Vector3(-7.5f,0,0),5,2.4f,7,plasterB);
            // East block with a passage: mid <-> market choke.
            Building(new Vector3(7.5f,0,-3),new Vector3(5,8,10),plasterC);
            Building(new Vector3(7.5f,0,6.2f),new Vector3(5,6.5f,3.6f),plasterB);
            Passage(new Vector3(7.5f,0,3.2f),5,2.4f,6.5f,plasterC);
            // Mid: bell tower landmark, a gate out of spawn, low walls and a vine pergola.
            Tower(new Vector3(0,0,-3));
            foreach(int s in new[]{-1,1})
            {
                Part("Gate wall",new Vector3(s*3.5f,1.6f,-12),new Vector3(3,3.2f,.8f),plasterC);
                Part("Gate cap",new Vector3(s*3.5f,3.3f,-12),new Vector3(3.2f,.2f,1),roof);
            }
            for(int i=0;i<=10;i++)
            {
                float a=i*18f*Mathf.Deg2Rad;
                var st=Part("Gate arch",new Vector3(Mathf.Cos(a)*2,3.2f+Mathf.Sin(a)*1.1f,-12),new Vector3(.4f,.35f,.8f),plasterC,null,false);
                st.transform.rotation=Quaternion.Euler(0,0,i*18f+90);
            }
            Sign("PIAZZA DELLA TORRE",new Vector3(0,4.55f,-12.42f),0,.04f);
            LowWall(new Vector3(-2.6f,0,4.5f),new Vector3(2.6f,1.1f,.6f));
            LowWall(new Vector3(3,0,-9),new Vector3(2.4f,1.1f,.6f));
            for(int i=0;i<7;i++) Part("Pergola beam",new Vector3(0,4.4f,-10.6f+i*.8f),new Vector3(10,.12f,.14f),wood,null,false);
            for(int i=0;i<9;i++) Part("Vine",new Vector3(-4.2f+i*1.05f,4.55f,-10.6f+(i*37%5)*1.2f),new Vector3(.9f,.35f,1.3f),foliage,null,false,PrimitiveType.Sphere);
            // West alley: stacked cargo, laundry overhead.
            Crate(new Vector3(-13,0,-6),new Vector3(2.6f,2.2f,2.6f));
            Crate(new Vector3(-14.5f,0,3.5f),new Vector3(1.6f,1.3f,1.6f));
            Banner(new Vector3(-16.5f,5.2f,-9),new Vector3(-10,5.2f,-9));
            Banner(new Vector3(-16.5f,5.6f,6),new Vector3(-10,5.6f,6));
            // East market street.
            Stall(new Vector3(14.6f,0,-12),teal);
            Stall(new Vector3(14.6f,0,-5),red);
            Stall(new Vector3(14.6f,0,2),teal);
            Crate(new Vector3(12,0,11),new Vector3(2.4f,2,2.4f));
            Sign("MERCATO",new Vector3(16.42f,4.1f,-5),90,.06f);
            // Site A: north fountain plaza.
            Fountain(new Vector3(-10,0,14));
            Crate(new Vector3(3,0,12),new Vector3(3,2.4f,3));
            Crate(new Vector3(10,0,17),new Vector3(2.6f,2,2.6f));
            LowWall(new Vector3(-4,0,16),new Vector3(4,1.2f,.6f));
            for(int side=-1;side<=1;side+=2)
            {
                Part("Site boundary",new Vector3(4+side*6,.02f,14),new Vector3(.07f,.015f,11),brass,null,false);
                Part("Site boundary",new Vector3(4,.02f,14+side*5.5f),new Vector3(12,.015f,.07f),brass,null,false);
            }
            Sign("A",new Vector3(4,2.4f,20.42f),0,.35f);
            Sign("SECOND CHANCE",new Vector3(0,4,-20.42f),180,.07f);
            // Facades: shutters, balconies, doors, lanterns, string lights, cypress trees.
            for(float z=-17;z<=17;z+=5.5f){Window(new Vector3(-16.45f,4.6f,z),-90);Window(new Vector3(16.45f,5f,z),90);}
            for(float x=-14;x<=14;x+=5.6f){Window(new Vector3(x,5.2f,20.45f),0);Window(new Vector3(x,4.4f,-20.45f),180);}
            foreach(float z in new[]{-8f,-4f,4.5f})Window(new Vector3(-4.95f,4.5f,z),-90);
            foreach(float z in new[]{-6f,-1.5f})Window(new Vector3(4.95f,4.6f,z),90);
            Balcony(new Vector3(-10,4.1f,20.45f),0);
            Balcony(new Vector3(9,6.2f,20.45f),0);
            Balcony(new Vector3(16.45f,5.4f,9),90);
            Balcony(new Vector3(-4.95f,5.2f,-6),-90);
            Door(new Vector3(-16.45f,0,-12),-90);Door(new Vector3(-4.95f,0,-8.5f),-90);Door(new Vector3(4.95f,0,-6.5f),90);
            Door(new Vector3(-6,0,20.45f),0);Door(new Vector3(12,0,-20.45f),180);
            foreach(var l in new[]{new Vector3(-16.4f,3.2f,-3),new Vector3(16.4f,3.2f,-9),new Vector3(16.4f,3.2f,6),new Vector3(-4.9f,3.2f,-2),new Vector3(4.9f,3.2f,0)})
                Lantern(l);
            StringLights(new Vector3(-5,6,6),new Vector3(5,6,6),12);
            StringLights(new Vector3(-16.5f,5.8f,15),new Vector3(16.5f,6.8f,15),26);
            StringLights(new Vector3(10,5,-15),new Vector3(16.5f,5,-15),6);
            foreach(var t in new[]{new Vector3(-14,0,-18),new Vector3(14,0,-18),new Vector3(-15,0,19),new Vector3(15,0,18.5f)}) Cypress(t);
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
        static void Building(Vector3 basePos, Vector3 size, Material wall)
        {
            Part("Plaster facade",basePos+Vector3.up*size.y/2,size,wall);
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
        // Vaulted passage through a block (walk direction = x): solid vault above head height, arches on both faces.
        static void Passage(Vector3 c,float depth,float gap,float height,Material m)
        {
            Part("Passage vault",c+Vector3.up*(3.2f+(height-3.2f)/2),new Vector3(depth,height-3.2f,gap),m);
            foreach(int side in new[]{-1,1})
                for(int i=0;i<=10;i++)
                {
                    float a=i*18f*Mathf.Deg2Rad;
                    var s=Part("Arch stone",c+new Vector3(side*(depth/2+.06f),2.3f+Mathf.Sin(a)*gap/2,Mathf.Cos(a)*gap/2),new Vector3(.14f,.32f,.32f),plasterC,null,false);
                    s.transform.rotation=Quaternion.Euler(-i*18f,0,0);
                }
        }
        static void Tower(Vector3 p)
        {
            Part("Tower plinth",p+Vector3.up*.5f,new Vector3(3.1f,1,3.1f),stone);
            Part("Tower shaft",p+Vector3.up*4.5f,new Vector3(2.6f,9,2.6f),plasterC);
            Part("Tower band",p+Vector3.up*6,new Vector3(2.75f,.2f,2.75f),roof,null,false);
            Part("Belfry floor",p+Vector3.up*9.05f,new Vector3(2.9f,.15f,2.9f),plaster,null,false);
            for(int sx=-1;sx<=1;sx+=2)for(int sz=-1;sz<=1;sz+=2)
                Part("Belfry pier",p+new Vector3(sx*1.05f,10.25f,sz*1.05f),new Vector3(.5f,2.5f,.5f),plasterC,null,false);
            Part("Bell",p+Vector3.up*10.1f,new Vector3(.9f,1,.9f),brass,null,false,PrimitiveType.Sphere);
            for(int i=0;i<4;i++){float s=3.3f-i*.8f;Part("Stepped roof",p+Vector3.up*(11.65f+i*.45f),new Vector3(s,.45f,s),roof,null,false);}
            Part("Spire",p+Vector3.up*13.9f,new Vector3(.12f,1.2f,.12f),dark,null,false);
            // Clock facing the spawn plaza.
            var clock=Part("Clock face",p+new Vector3(0,7.2f,-1.32f),new Vector3(1.5f,.03f,1.5f),ivory,null,false,PrimitiveType.Cylinder);
            clock.transform.rotation=Quaternion.Euler(90,0,0);
            Part("Clock hand",p+new Vector3(0,7.45f,-1.36f),new Vector3(.06f,.5f,.02f),dark,null,false);
            var hand=Part("Clock hand",p+new Vector3(.18f,7.2f,-1.36f),new Vector3(.06f,.38f,.02f),dark,null,false);
            hand.transform.rotation=Quaternion.Euler(0,0,-90);
        }
        static void LowWall(Vector3 p,Vector3 size)
        {
            Part("Low wall",p+Vector3.up*size.y/2,size,plasterC);
            Part("Wall capstone",p+Vector3.up*(size.y+.06f),new Vector3(size.x+.15f,.12f,size.z+.15f),stone,null,false);
        }
        static void Fountain(Vector3 p)
        {
            Part("Fountain basin",p+Vector3.up*.35f,new Vector3(4.5f,.7f,4.5f),plaster);
            Part("Water",p+Vector3.up*.72f,new Vector3(3.8f,.04f,3.8f),teal,null,false);
            Part("Fountain pedestal",p+Vector3.up*1.3f,new Vector3(.65f,2.6f,.65f),plaster);
            Part("Fountain bowl",p+Vector3.up*2.3f,new Vector3(2,.2f,2),plaster,null,true,PrimitiveType.Cylinder);
        }
        static void Stall(Vector3 p,Material stripe)
        {
            Part("Stall counter",p+Vector3.up*.52f,new Vector3(1.3f,1.05f,2.6f),wood);
            Part("Counter top",p+Vector3.up*1.08f,new Vector3(1.45f,.08f,2.75f),plaster,null,false);
            for(int i=0;i<5;i++)Part("Produce",p+new Vector3(-.3f+(i%2)*.5f,1.24f,-1+i*.5f),Vector3.one*.28f,i%2==0?red:brass,null,false,PrimitiveType.Sphere);
            for(int s=-1;s<=1;s+=2)Part("Awning post",p+new Vector3(-.75f,1.3f,s*1.3f),new Vector3(.08f,2.6f,.08f),dark,null,false);
            for(int i=0;i<6;i++)
            {
                var a=Part("Awning stripe",p+new Vector3(-.35f,2.7f,-1.375f+i*.55f),new Vector3(2.1f,.05f,.55f),i%2==0?stripe:plasterC,null,false);
                a.transform.rotation=Quaternion.Euler(0,0,-14);
            }
        }
        // Decorations whose front faces local -z: yaw 0 faces south, 180 north, 90 west, -90 east.
        static Transform Facing(string name,Vector3 p,float yaw)
        {
            var r=new GameObject(name).transform;r.SetParent(root);r.position=p;r.rotation=Quaternion.Euler(0,yaw,0);return r;
        }
        static void Balcony(Vector3 p,float yaw)
        {
            var r=Facing("Balcony",p,yaw);
            Part("Balcony slab",new Vector3(0,0,-.45f),new Vector3(2.4f,.14f,.9f),plaster,r,false);
            Part("Rail",new Vector3(0,.9f,-.88f),new Vector3(2.4f,.05f,.05f),dark,r,false);
            for(int i=0;i<9;i++)Part("Baluster",new Vector3(-1.15f+i*.2875f,.45f,-.88f),new Vector3(.04f,.9f,.04f),dark,r,false);
            Part("Flower box",new Vector3(0,1,-.88f),new Vector3(1.8f,.22f,.25f),roof,r,false);
            for(int i=0;i<6;i++)Part("Flowers",new Vector3(-.75f+i*.3f,1.2f,-.88f),Vector3.one*.22f,i%2==0?red:foliage,r,false,PrimitiveType.Sphere);
            Part("Balcony door",new Vector3(0,1.15f,-.02f),new Vector3(1.1f,2.2f,.06f),teal,r,false);
        }
        static void Door(Vector3 p,float yaw)
        {
            var r=Facing("Door",p,yaw);
            Part("Door frame",new Vector3(0,1.3f,-.02f),new Vector3(1.7f,2.6f,.06f),stone,r,false);
            Part("Door",new Vector3(0,1.2f,-.06f),new Vector3(1.3f,2.3f,.06f),wood,r,false);
            Part("Door handle",new Vector3(.45f,1.1f,-.1f),new Vector3(.06f,.06f,.06f),brass,r,false);
        }
        static void Lantern(Vector3 p)
        {
            Part("Lantern frame",p,new Vector3(.3f,.5f,.3f),dark,null,false);
            Part("Lantern light",p,new Vector3(.22f,.38f,.22f),lamp,null,false);
        }
        static void StringLights(Vector3 a,Vector3 b,int bulbs)
        {
            var w=Part("Light wire",(a+b)/2,new Vector3(.02f,.02f,(b-a).magnitude),dark,null,false);
            w.transform.rotation=Quaternion.LookRotation(b-a);
            for(int i=1;i<bulbs;i++)Part("Bulb",Vector3.Lerp(a,b,i/(float)bulbs)+Vector3.down*.1f,Vector3.one*.12f,lamp,null,false,PrimitiveType.Sphere);
        }
        // Laundry line: hanging cloth panels in alternating colours.
        static void Banner(Vector3 a,Vector3 b)
        {
            var w=Part("Laundry line",(a+b)/2,new Vector3(.02f,.02f,(b-a).magnitude),dark,null,false);
            w.transform.rotation=Quaternion.LookRotation(b-a);
            Material[] cloth={ivory,teal,red,plasterB};
            for(int i=1;i<6;i++)
            {
                var c=Part("Cloth",Vector3.Lerp(a,b,i/6f)+Vector3.down*.45f,new Vector3(.7f,.9f,.02f),cloth[i%4],null,false);
                c.transform.rotation=Quaternion.LookRotation(Vector3.Cross(b-a,Vector3.up));
            }
        }
        static void Cypress(Vector3 p)
        {
            Part("Planter",p+Vector3.up*.4f,new Vector3(1.7f,.8f,1.7f),plaster);
            Part("Tree trunk",p+Vector3.up*1.4f,new Vector3(.3f,2,.3f),wood);
            Part("Cypress crown",p+Vector3.up*3,new Vector3(1.3f,4,1.3f),foliage,null,false,PrimitiveType.Sphere);
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
