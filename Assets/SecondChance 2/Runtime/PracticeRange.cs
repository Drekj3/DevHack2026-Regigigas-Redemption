using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SecondChance
{
    public enum PracticeMode { Setup, Live, Review, Results }
    [Serializable] public struct ActorSnapshot
    {
        public Vector3 position;
        public float yaw, pitch, health, verticalSpeed, shotCooldown, reload, bloom;
        public int ammo;
        public bool crouched, aiming;
        // Phoenix kit, Valorant round economy: charges don't recharge within the round.
        public int flashes, mollies, walls;
        public bool ultReady, ultActive;
        public float ultTimer, ultYaw, blind;
        public Vector3 ultMark, blindFrom;
    }
    public enum FxKind { Flash, Fireball, Fire, Wall }
    // Data only, so replay snapshots capture it and takeover resumes it exactly (walls deep-copied via Copy()).
    [Serializable] public struct AbilityFx
    {
        public FxKind kind;
        public int id;                   // stable key for its visuals
        public int owner;                // 0 player, 1 opponent
        public Vector3 pos, vel;         // wall: pos = travelling head
        public float age, curve, yaw, length; // flash: curve = ±1 side. wall: yaw = head heading, length = travelled
        public bool done, steer;         // flash popped / wall finished. steer = wall still bending toward caster's crosshair
        public Vector3[] path;           // wall: points laid behind the head
        public int count;
    }
    [Serializable] public class PracticeSnapshot
    {
        public float time;
        public ActorSnapshot player, opponent;
        public Vector3 lastKnown;
        public float memory, reaction;
        public uint rng;
        public string decision;
        public string externalState;
        public Vector3 patrolGoal;
        public float patrolTimer, previousBotHealth;
        public List<AbilityFx> abilityFx;
        public float botAbilityTimer;
        public float strafeTimer, strafeDir;
    }
    [Serializable] public class PracticeScenario
    {
        public Vector3 playerPosition = new Vector3(0,.06f,-15);
        public Vector3 opponentPosition = new Vector3(4,.06f,15);
        public float playerYaw, opponentYaw = 180;
        public int rank = 3;            // 0 Iron ... 8 Radiant; sets every bot skill (RankProfile)
        public string riotId = "";      // optional Riot ID used to look the rank up
        [NonSerialized] public int startingHealth = 150; // fixed for everyone; not taken from old scene/save values
    }
    // Your friend's adapter can subclass this. Capture/Restore must include its
    // memory, timers and RNG if exact counterfactual state restoration is wanted.
    public abstract class PracticeBotDriver : MonoBehaviour
    {
        public abstract void TickBot(PracticeRange arena, float deltaTime);
        public virtual string CaptureBotState() { return ""; }
        public virtual void RestoreBotState(string state) { }
        public virtual void ResetBot(PracticeScenario scenario) { }
    }
    public class PracticeRange : MonoBehaviour
    {
        public PracticeBotDriver externalBot;
        public PracticeScenario scenario = new PracticeScenario();
        public PracticeMode Mode { get; private set; } = PracticeMode.Setup;
        public RankProfile Bot => RankProfile.For(scenario.rank);
        public Vector3 PlayerPosition => p.position;
        public Vector3 OpponentPosition => b.position;
        public float OpponentHealth => b.health;
        public float PlayerHealth => p.health;
        public string Decision { get; private set; } = "Holding angle";
        Camera fps, overhead;
        CharacterController pc, bc;
        Transform playerRoot, botRoot, rifle, botVisual;
        readonly Transform[] botLegs=new Transform[2],botFists=new Transform[2];
        Vector3 lastBotPos;
        float walkPhase;
        Material tracerMaterial;
        AudioSource sound;
        AudioClip shotSound;
        ActorSnapshot p,b;
        Vector3 lastKnown;
        float memory,reaction,elapsed,reviewClock,kick,hitFlash,damageFlash;
        CourtyardNavigation navigation = new CourtyardNavigation();
        Vector3 patrolGoal;
        float patrolTimer, previousBotHealth;
        readonly List<Vector3> route = new List<Vector3>();
        Vector3 routeGoal;
        float routeAge;
        uint rng=12345;
        bool reviewPlaying, paused;
        int reviewIndex, placement;
        readonly List<PracticeSnapshot> frames=new List<PracticeSnapshot>();
        readonly List<PracticeSnapshot> review=new List<PracticeSnapshot>();
        readonly List<GameObject> effects=new List<GameObject>();
        string outcome="",previous="",notice="Choose placement, then click the courtyard.";
        string savePath => Path.Combine(Application.persistentDataPath,"second-chance-scenario.json");
        Transform geometry;
        // Abilities
        // ponytail: timings/damage are approximations of live Valorant values; tune here.
        const float EquipTime=.4f,Fuse=.55f,CurveRate=200f,FlashMax=1.1f,Gravity=8f,MollyFuse=1f,ZoneRadius=4.5f,ZoneLife=4f,
            WallMax=18f,WallSpacing=1.5f,WallGrow=16f,WallTurn=120f,WallLife=8f,WallHeight=4f,UltLife=10f;
        const int WallPathMax=14;
        readonly List<AbilityFx> fx=new List<AbilityFx>();
        readonly Vector3[] wallPts=new Vector3[WallPathMax+1];
        Material flashMat,fireMat,ringMat,glowMat,burstMat,flameMat,lavaMat,sparkMat;
        class FxView{public FxKind kind;public GameObject root;public Transform core,halo;public Renderer haloR,surfR;public MeshFilter mesh;public ParticleSystem ps;public float debt;public bool popped;public readonly List<Material> mats=new List<Material>();}
        readonly Dictionary<int,FxView> views=new Dictionary<int,FxView>();
        readonly HashSet<int> liveViews=new HashSet<int>();
        readonly List<int> deadViews=new List<int>();
        int nextFxId=1;
        Transform hand,handHalo;Renderer handCore,handHaloR;ParticleSystem handPs;
        int armed;            // player: 0 none, 1 curveball, 2 hot hands, 3 blaze
        float armTime;
        bool fireLock;        // eats the click that cast an ability so the rifle doesn't fire too
        bool playerFireHeld;  // Blaze bends only while FIRE stays held
        float botAbilityTimer;
        float strafeTimer,strafeDir=1; // bot counter-strafe rhythm
        bool rankLookup; string rankStatus="Enter a Riot ID to match the bot to that rank, or pick one.";
        void Awake()
        {
            built=true;
            geometry = PracticeWorld.Build(transform);
            navigation.Build(geometry);
            // Saved/serialized spawns from an older layout may now sit inside a building.
            if(!navigation.IsWalkable(scenario.playerPosition))scenario.playerPosition=navigation.Snap(scenario.playerPosition);
            if(!navigation.IsWalkable(scenario.opponentPosition))scenario.opponentPosition=navigation.Snap(scenario.opponentPosition);
            playerRoot=new GameObject("Player").transform; playerRoot.SetParent(transform);
            pc=playerRoot.gameObject.AddComponent<CharacterController>(); pc.height=1.8f;pc.radius=.3f;pc.center=Vector3.up*.9f;
            fps=new GameObject("Player Camera").AddComponent<Camera>();fps.transform.SetParent(playerRoot,false);fps.transform.localPosition=Vector3.up*1.62f;
            fps.nearClipPlane=.03f;fps.fieldOfView=78;fps.clearFlags=CameraClearFlags.SolidColor;fps.backgroundColor=new Color(.55f,.7f,.83f);fps.tag="MainCamera";
            fps.gameObject.AddComponent<AudioListener>();
            rifle=PracticeWorld.Rifle(fps.transform);rifle.localScale=Vector3.one*.7f;
            // Sleeves/gloves make the first-person view feel held rather than floating.
            var fpSkin=PracticeWorld.Mat("Warm brown skin",new Color(.36f,.22f,.15f));
            PracticeWorld.Part("Support hand",new Vector3(-.055f,-.1f,.3f),new Vector3(.12f,.11f,.16f),fpSkin,rifle,false);
            PracticeWorld.Part("Trigger hand",new Vector3(.015f,-.14f,-.12f),new Vector3(.1f,.12f,.14f),fpSkin,rifle,false);
            botRoot=new GameObject("Opponent - attach your bot adapter here").transform;botRoot.SetParent(transform);
            bc=botRoot.gameObject.AddComponent<CharacterController>();bc.height=1.8f;bc.radius=.3f;bc.center=Vector3.up*.9f;
            // Opponent: lean streetwear fire-caster. White high-collar jacket worn open over a glowing
            // lining, charcoal bird-emblem tee, strapped tactical trousers, orange-laced high-tops,
            // copper-tipped locs over a fade. Visual only: the CharacterController capsule stays the hitbox (head = top 0.3m).
            Material Glow(string n,Color c,float k){var m=PracticeWorld.Mat(n,c);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",c*k);return m;}
            var skin=PracticeWorld.Mat("Warm brown skin",new Color(.36f,.22f,.15f));
            var lips=PracticeWorld.Mat("Lips",new Color(.26f,.13f,.1f));
            var eye=PracticeWorld.Mat("Dark brown eye",new Color(.07f,.04f,.03f));
            var hair=PracticeWorld.Mat("Black locs",new Color(.04f,.03f,.025f));
            var tips=Glow("Copper loc tips",new Color(.9f,.4f,.1f),.6f);
            var jacket=PracticeWorld.Mat("White tech jacket",new Color(.88f,.89f,.9f));
            var panel=PracticeWorld.Mat("Pale grey panel",new Color(.66f,.68f,.7f));
            var lining=Glow("Flame lining",new Color(1,.6f,.15f),2.5f);
            var accent=PracticeWorld.Mat("Orange accent",new Color(1,.45f,.1f));
            var tee=PracticeWorld.Mat("Charcoal tee",new Color(.1f,.1f,.11f));
            var pants=PracticeWorld.Mat("Black tactical trousers",new Color(.07f,.07f,.08f));
            var shoe=PracticeWorld.Mat("Dark grey high-top",new Color(.17f,.17f,.19f));
            var sole=PracticeWorld.Mat("White sole",new Color(.92f,.92f,.9f));
            var palmFire=Glow("Palm flame",new Color(1,.8f,.3f),4f);
            botVisual=new GameObject("Opponent - streetwear fire-caster").transform;botVisual.SetParent(botRoot,false);
            Transform B=botVisual;
            GameObject P(string n,Vector3 pos,Vector3 size,Material m,Transform t=null,PrimitiveType ty=PrimitiveType.Cube)=>PracticeWorld.Part(n,pos,size,m,t??B,false,ty);
            void Tilt(GameObject g,float x,float y,float z)=>g.transform.localRotation=Quaternion.Euler(x,y,z);
            for(int s=-1,i=0;s<=1;s+=2,i++)
            {
                // Legs hang from a hip pivot so they can swing while walking.
                var hip=new GameObject("Hip").transform;hip.SetParent(B,false);hip.localPosition=new Vector3(s*.12f,.86f,0);botLegs[i]=hip;
                P("Thigh",new Vector3(0,-.2f,0),new Vector3(.2f,.44f,.22f),pants,hip);
                P("Thigh strap",new Vector3(0,-.14f,0),new Vector3(.215f,.035f,.235f),PracticeWorld.dark,hip);
                for(int a=-1;a<=1;a+=2)P("Ammo round",new Vector3(s*.115f,-.14f,a*.04f),new Vector3(.035f,.035f,.035f),PracticeWorld.brass,hip,PrimitiveType.Cylinder);
                P("Knee panel",new Vector3(0,-.44f,.1f),new Vector3(.17f,.14f,.05f),panel,hip);
                P("Shin",new Vector3(0,-.62f,0),new Vector3(.16f,.4f,.17f),pants,hip);
                P("High-top",new Vector3(0,-.78f,.04f),new Vector3(.17f,.13f,.28f),shoe,hip);
                P("Ankle band",new Vector3(0,-.72f,0),new Vector3(.175f,.025f,.19f),accent,hip);
                P("Orange laces",new Vector3(0,-.74f,.11f),new Vector3(.09f,.03f,.12f),accent,hip);
                P("Sole stripe",new Vector3(0,-.828f,.045f),new Vector3(.185f,.012f,.305f),panel,hip);
                P("Sole",new Vector3(0,-.85f,.045f),new Vector3(.18f,.035f,.3f),sole,hip);
            }
            P("Belt",new Vector3(0,.9f,0),new Vector3(.4f,.07f,.24f),PracticeWorld.dark);
            P("Belt buckle",new Vector3(0,.9f,.122f),new Vector3(.07f,.05f,.01f),PracticeWorld.brass);
            P("Hip holster",new Vector3(.23f,.8f,0),new Vector3(.06f,.2f,.13f),PracticeWorld.dark);
            // Fitted tee: broad chest tapering to a narrow waist.
            P("Chest",new Vector3(0,1.28f,0),new Vector3(.42f,.36f,.23f),tee);
            P("Waist",new Vector3(0,1.03f,0),new Vector3(.34f,.2f,.2f),tee);
            P("Bird body",new Vector3(0,1.26f,.118f),new Vector3(.035f,.12f,.01f),sole);
            for(int s=-1;s<=1;s+=2)
            {
                Tilt(P("Bird wing",new Vector3(s*.065f,1.3f,.118f),new Vector3(.12f,.03f,.01f),sole),0,0,s*35);
                Tilt(P("Bird wingtip",new Vector3(s*.115f,1.36f,.118f),new Vector3(.06f,.02f,.01f),sole),0,0,s*60);
            }
            // Jacket hangs open: two front panels leave the tee visible down the middle, lining glows at the edges.
            for(int s=-1;s<=1;s+=2)
            {
                P("Jacket panel",new Vector3(s*.165f,1.2f,0),new Vector3(.15f,.6f,.27f),jacket);
                P("Lining edge",new Vector3(s*.093f,1.2f,.125f),new Vector3(.012f,.58f,.02f),lining);
                Tilt(P("Lapel fold",new Vector3(s*.11f,1.4f,.137f),new Vector3(.045f,.17f,.012f),lining),0,0,-s*15);
                P("Panel seam",new Vector3(s*.2f,1.18f,.136f),new Vector3(.008f,.5f,.004f),panel);
                P("Jacket hem",new Vector3(s*.165f,.915f,0),new Vector3(.155f,.04f,.275f),panel);
                P("Jacket shoulder",new Vector3(s*.25f,1.45f,0),new Vector3(.17f,.12f,.27f),jacket);
                // Oversized structured collar standing up around the jaw, open at the front.
                P("Collar side",new Vector3(s*.14f,1.57f,-.01f),new Vector3(.05f,.18f,.18f),jacket);
                Tilt(P("Collar tip",new Vector3(s*.11f,1.56f,.09f),new Vector3(.05f,.15f,.06f),jacket),0,s*30,0);
            }
            P("Jacket back",new Vector3(0,1.2f,-.115f),new Vector3(.2f,.6f,.04f),jacket);
            P("Collar back",new Vector3(0,1.58f,-.09f),new Vector3(.3f,.19f,.05f),jacket);
            P("Collar lining",new Vector3(0,1.58f,-.062f),new Vector3(.25f,.16f,.008f),lining);
            P("Crown base",new Vector3(0,1.32f,-.137f),new Vector3(.12f,.03f,.01f),accent);
            for(int c=-1;c<=1;c++)P("Crown point",new Vector3(c*.045f,1.355f,-.137f),new Vector3(.025f,.05f,.01f),accent);
            for(int s=-1,i=0;s<=1;s+=2,i++)
            {
                P("Sleeve",new Vector3(s*.31f,1.28f,.04f),new Vector3(.16f,.32f,.17f),jacket);
                P("Sleeve flame stripe",new Vector3(s*.392f,1.3f,.04f),new Vector3(.01f,.26f,.04f),accent);
                P("Forearm sleeve",new Vector3(s*.27f,1.12f,.18f),new Vector3(.15f,.15f,.26f),jacket);
                P("Cuff",new Vector3(s*.255f,1.12f,.32f),new Vector3(.12f,.12f,.05f),PracticeWorld.dark);
                P("Bare hand",new Vector3(s*.24f,1.12f,.38f),new Vector3(.09f,.1f,.1f),skin);
                botFists[i]=P("Palm flame",new Vector3(s*.24f,1.17f,.42f),Vector3.one*.05f,palmFire,null,PrimitiveType.Sphere).transform;
            }
            P("Crown ring",new Vector3(.24f,1.1f,.43f),new Vector3(.03f,.02f,.02f),PracticeWorld.brass);
            P("Neck",new Vector3(0,1.53f,0),new Vector3(.12f,.1f,.12f),skin);
            // Head: slightly elongated, angular jaw, chin raised a touch (pivot at the neck).
            var head=new GameObject("Head").transform;head.SetParent(B,false);head.localPosition=new Vector3(0,1.55f,0);head.localRotation=Quaternion.Euler(-6,0,0);
            P("Skull",new Vector3(0,.12f,.01f),new Vector3(.22f,.27f,.24f),skin,head,PrimitiveType.Sphere);
            P("Jaw",new Vector3(0,.05f,.04f),new Vector3(.15f,.07f,.15f),skin,head);
            P("Nose",new Vector3(0,.11f,.125f),new Vector3(.04f,.045f,.03f),skin,head);
            Tilt(P("Smirk",new Vector3(.005f,.065f,.116f),new Vector3(.06f,.018f,.012f),lips,head),0,0,6);
            for(int s=-1;s<=1;s+=2)
            {
                P("Eye",new Vector3(s*.045f,.14f,.113f),new Vector3(.04f,.014f,.01f),eye,head);
                Tilt(P("Brow",new Vector3(s*.047f,.16f,.116f),new Vector3(.055f,.014f,.012f),hair,head),0,0,-s*10);
                P("Ear",new Vector3(s*.11f,.12f,0),new Vector3(.03f,.06f,.04f),skin,head);
                P("Gold earring",new Vector3(s*.115f,.08f,.005f),Vector3.one*.018f,PracticeWorld.brass,head,PrimitiveType.Sphere);
            }
            // Fade + asymmetric high-top of short twists, copper tips concentrated at the front.
            P("Fade",new Vector3(0,.16f,-.005f),new Vector3(.225f,.2f,.235f),hair,head,PrimitiveType.Sphere);
            for(int xi=0;xi<4;xi++)for(int zi=0;zi<3;zi++)
            {
                int k=xi*3+zi;float h=.08f+.012f*((k*5)%4)+(xi>=2?.02f:0f);
                var loc=P("Loc",new Vector3(-.075f+xi*.05f,.24f+h*.3f,-.05f+zi*.05f),new Vector3(.032f,h,.032f),hair,head);
                Tilt(loc,18+(k*7)%12,0,(k%3-1)*9);
                if(zi>0||k%4==0)P("Copper tip",new Vector3(0,.45f,0),new Vector3(1.15f,.3f,1.15f),tips,loc.transform);
            }
            var br=PracticeWorld.Rifle(B);br.localPosition=new Vector3(.12f,1.17f,.3f);br.localScale=Vector3.one*.85f;
            overhead=new GameObject("Scenario planning camera").AddComponent<Camera>();overhead.transform.SetParent(transform);
            overhead.transform.position=new Vector3(0,50,0);overhead.transform.rotation=Quaternion.Euler(90,0,0);overhead.orthographic=true;overhead.orthographicSize=27;
            overhead.clearFlags=CameraClearFlags.SolidColor;overhead.backgroundColor=new Color(.07f,.11f,.14f);
            sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.volume=.15f;
            shotSound=MakeShotSound();
            tracerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));tracerMaterial.color=new Color(1,.75f,.3f);
            flashMat=new Material(tracerMaterial){color=new Color(1,.96f,.8f)};
            fireMat=new Material(tracerMaterial){color=new Color(1,.45f,.08f)};
            FxArt.Build();
            ringMat=FxArt.Mat(FxArt.Ring,new Color(1,.35f,.35f),true);
            glowMat=FxArt.Mat(FxArt.Dot,new Color(1,.55f,.2f),true);
            burstMat=FxArt.Mat(FxArt.Starburst,new Color(1,.92f,.88f),true);
            flameMat=FxArt.Mat(FxArt.Flame,Color.white,false);
            lavaMat=FxArt.Mat(FxArt.Lava,Color.white,false);
            sparkMat=FxArt.Mat(FxArt.Dot,Color.white,true,true);
            foreach(var fist in botFists)FxArt.Particles(fist,sparkMat,new Color(1,.6f,.2f),new Color(1,.25f,.15f),.4f,.07f,.15f,25); // embers off the gauntlets
            // Flame orb held in the right hand while an ability is equipped (the rifle is lowered).
            hand=new GameObject("Held ability").transform;hand.SetParent(fps.transform,false);hand.localPosition=new Vector3(.22f,-.2f,.55f);
            handCore=PracticeWorld.Part("Core",Vector3.zero,Vector3.one*.08f,flashMat,hand,false,PrimitiveType.Sphere).GetComponent<Renderer>();
            handHalo=Billboard(hand,ringMat,.3f,out handHaloR);
            handPs=FxArt.Particles(hand,sparkMat,new Color(1,.6f,.3f),new Color(1,.3f,.25f),.35f,.06f,.15f,45);
            hand.gameObject.SetActive(false);
            ResetActors();ShowSetup();
        }
        static AudioClip MakeShotSound()
        {
            var clip=AudioClip.Create("Synthetic practice rifle",4410,1,44100,false);
            var samples=new float[4410]; var random=new System.Random(8);
            for(int i=0;i<samples.Length;i++) samples[i]=(float)(random.NextDouble()*2-1)*Mathf.Exp(-i/500f)*.6f;
            clip.SetData(samples,0);return clip;
        }
        // A script recompile during Play mode (domain reload is off in this project) drops every
        // non-serialized reference: leg/fist transforms, the fx view table, the nav grid. LateUpdate then
        // threw each frame and old flame walls stayed frozen on screen. Rebuild from scratch instead.
        [NonSerialized] bool built;
        void OnEnable()
        {
            if(built||transform.childCount==0)return;
            for(int i=transform.childCount-1;i>=0;i--)DestroyImmediate(transform.GetChild(i).gameObject);
            views.Clear();Awake();
        }
        ActorSnapshot Fresh(Vector3 pos,float yaw) => new ActorSnapshot{position=pos,yaw=yaw,health=scenario.startingHealth,ammo=25,flashes=2,mollies=1,walls=1,ultReady=true};
        void ResetActors()
        {
            p=Fresh(scenario.playerPosition,scenario.playerYaw); b=Fresh(scenario.opponentPosition,scenario.opponentYaw);
            fx.Clear();foreach(int id in new List<int>(views.Keys))Drop(id);botAbilityTimer=0;armed=0;strafeTimer=0;strafeDir=1;
            lastKnown=b.position;memory=0;reaction=0;rng=12345;Decision="Holding angle";elapsed=0;
            patrolGoal=b.position;patrolTimer=0;previousBotHealth=b.health;route.Clear();routeAge=0;
            ApplyTransforms();if(externalBot!=null)externalBot.ResetBot(scenario);
        }
        void Lock(bool value){Cursor.lockState=value?CursorLockMode.Locked:CursorLockMode.None;Cursor.visible=!value;}
        void ShowSetup()
        {
            Mode=PracticeMode.Setup;paused=false;fps.enabled=false;overhead.enabled=true;rifle.gameObject.SetActive(false);Lock(false);ClearEffects();
        }
        void StartAttempt()
        {
            ResetActors();frames.Clear();frames.Add(Capture());outcome="";EnterLive();
        }
        void EnterLive()
        {
            Mode=PracticeMode.Live;paused=false;fps.enabled=true;overhead.enabled=false;rifle.gameObject.SetActive(true);Lock(true);ClearEffects();fireLock=true;
        }
        void Update()
        {
            float dt=Time.deltaTime;hitFlash=Mathf.Max(0,hitFlash-dt);damageFlash=Mathf.Max(0,damageFlash-dt);
            if(Mode==PracticeMode.Setup) { Placement();return; }
            if(Mode==PracticeMode.Review)
            {
                if(reviewPlaying)
                {
                    reviewClock+=dt;
                    while(reviewIndex+1<review.Count&&review[reviewIndex+1].time<=reviewClock)reviewIndex++;
                    Restore(review[reviewIndex]);if(reviewIndex==review.Count-1)reviewPlaying=false;
                }
                PoseGun();return;
            }
            if(Mode!=PracticeMode.Live)return;
            if(Keyboard.current!=null&&Keyboard.current.escapeKey.wasPressedThisFrame){paused=!paused;Lock(!paused);}
            if(paused)return;
            elapsed+=dt;TickTimers(ref p,dt);TickTimers(ref b,dt);PlayerTick(dt);
            if(p.health>0&&b.health>0)
            {
                if(externalBot!=null)externalBot.TickBot(this,dt);else BuiltInBot(dt);
                TickAbilities(dt);
            }
            if(Revive(ref p)|Revive(ref b)){ApplyTransforms();route.Clear();}
            p.position=playerRoot.position;b.position=botRoot.position;
            frames.Add(Capture());PoseGun();
            if(p.health<=0||b.health<=0||elapsed>=60)EndAttempt();
        }
        void TickTimers(ref ActorSnapshot a,float dt)
        {
            a.shotCooldown=Mathf.Max(0,a.shotCooldown-dt);
            if(a.reload>0){a.reload-=dt;if(a.reload<=0){a.reload=0;a.ammo=25;}}
            a.blind=Mathf.Max(0,a.blind-dt);if(a.ultActive)a.ultTimer-=dt;
        }
        void PlayerTick(float dt)
        {
            var k=Keyboard.current;var m=Mouse.current;if(k==null||m==null)return;
            var delta=m.delta.ReadValue()*.10f*(fps.fieldOfView/HipFov); // same feel when zoomed
            p.yaw+=delta.x;p.pitch=Mathf.Clamp(p.pitch-delta.y,-80,80);
            playerRoot.rotation=Quaternion.Euler(0,p.yaw,0);fps.transform.localRotation=Quaternion.Euler(p.pitch,0,0);
            float x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),z=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);
            Vector3 move=Vector3.ClampMagnitude(playerRoot.right*x+playerRoot.forward*z,1);
            // Crouch while held; only stand up if nothing is overhead.
            bool wantCrouch=k.leftCtrlKey.isPressed; // C is Blaze now, like Valorant
            if(wantCrouch!=p.crouched&&(wantCrouch||!Physics.Raycast(playerRoot.position+Vector3.up*1.25f,Vector3.up,.6f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)))
            {p.crouched=wantCrouch;SetCrouch();}
            var cam=fps.transform.localPosition;cam.y=Mathf.MoveTowards(cam.y,p.crouched?1.05f:1.62f,dt*5);fps.transform.localPosition=cam;
            if(pc.isGrounded&&p.verticalSpeed<0)p.verticalSpeed=-2;
            if(pc.isGrounded&&k.spaceKey.wasPressedThisFrame)p.verticalSpeed=6.3f;
            p.verticalSpeed-=20*dt;
            // RMB aims down sights (RMB is Curveball's alt fire while an ability is equipped).
            p.aiming=armed==0&&m.rightButton.isPressed&&p.reload<=0;
            pc.Move((move*(p.crouched?2f:k.leftShiftKey.isPressed?2.3f:4.5f)*(p.aiming?.76f:1)+Vector3.up*p.verticalSpeed)*dt);p.position=playerRoot.position;
            p.bloom=Mathf.MoveTowards(p.bloom,0,dt*(p.aiming?7:5));
            if(k.rKey.wasPressedThisFrame&&p.ammo<25&&p.reload<=0)p.reload=1.5f;
            // Valorant flow: Q/E/C equips (press again to holster), FIRE uses after the equip time. X is instant.
            if(k.qKey.wasPressedThisFrame)Equip(1,p.flashes>0);
            if(k.eKey.wasPressedThisFrame)Equip(2,p.mollies>0);
            if(k.cKey.wasPressedThisFrame)Equip(3,p.walls>0);
            if(k.xKey.wasPressedThisFrame)RunItBack(0);
            armTime+=dt;playerFireHeld=m.leftButton.isPressed;
            if(!m.leftButton.isPressed)fireLock=false;
            bool fire=m.leftButton.wasPressedThisFrame,alt=m.rightButton.wasPressedThisFrame;
            if(armed!=0&&armTime>=EquipTime&&(fire||alt&&armed==1))
            {
                Vector3 eye=fps.transform.position,f=fps.transform.forward;
                if(armed==1)Curveball(0,eye,f,alt?1:-1);              // FIRE curves left, ALT FIRE curves right
                else if(armed==2)Fireball(0,eye+f*.5f,f*16+Vector3.up*1.5f);
                else Blaze(0,p.position,p.yaw,true);                    // keep holding FIRE to bend toward the crosshair
                armed=0;fireLock=true;
            }
            rifle.gameObject.SetActive(armed==0);
            if(armed==0&&!fireLock&&m.leftButton.isPressed&&p.shotCooldown<=0&&p.reload<=0&&p.ammo>0)
            {
                p.ammo--;p.shotCooldown=.13f;kick=p.aiming?.02f:.045f;sound.PlayOneShot(shotSound);
                // Gunfire provides an audible position, not permanent wall vision.
                if(Vector3.Distance(p.position,b.position)<22f)
                { lastKnown=p.position;memory=5f; }
                Vector2 c=UnityEngine.Random.insideUnitCircle*PlayerSpread();
                p.bloom=p.aiming?Mathf.Min(p.bloom+.2f,1.6f):Mathf.Min(p.bloom+.45f,3.5f); // ADS: less bloom per shot, lower cap
                Vector3 start=fps.transform.position,dir=fps.transform.rotation*Quaternion.Euler(-c.y,c.x,0)*Vector3.forward;
                Vector3 end=start+dir*80;
                if(Physics.Raycast(start,dir,out RaycastHit hit,80,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                {
                    end=hit.point;
                    if(hit.collider.transform==botRoot||hit.collider.transform.IsChildOf(botRoot))
                    {
                        b.health=Mathf.Max(0,b.health-(hit.point.y-b.position.y>1.5f?HeadDamage:BodyDamage));hitFlash=.15f;
                        lastKnown=p.position;memory=Mathf.Max(memory,4f); // getting hit reveals the shooter's direction at any range
                    }
                }
                Trace(rifle.TransformPoint(new Vector3(0,.025f,.82f)),end);
            }
        }
        // Spread cone in degrees: firing builds bloom, moving adds a little (walking) or a lot (running),
        // airborne is very inaccurate, crouching tightens.
        float PlayerSpread()
        {
            Vector3 v=pc.velocity;v.y=0;float s=v.magnitude;
            float move=Mathf.Min(s,2.4f)*.15f+Mathf.InverseLerp(2.4f,4.5f,s)*2.5f;
            return (.1f+p.bloom+move)*(p.crouched?.55f:1)*(p.aiming?.4f:1)+(pc.isGrounded?0:6f);
        }
        const float HipFov=78f,AdsFov=66f; // ~1.25x zoom
        float adsBlend;
        void SetCrouch(){pc.height=p.crouched?1.2f:1.8f;pc.center=Vector3.up*pc.height/2;}
        void PoseGun()
        {
            kick=Mathf.MoveTowards(kick,0,Time.deltaTime*.3f);
            adsBlend=Mathf.MoveTowards(adsBlend,p.aiming?1:0,Time.deltaTime*7);
            float t=Mathf.SmoothStep(0,1,adsBlend);
            float sway=Mode==PracticeMode.Live&&!paused?Mathf.Sin(Time.time*5)*.002f*(1-t):0;
            // ADS lines the reflex optic (local y .21 at .7 scale) up with the camera.
            rifle.localPosition=Vector3.Lerp(new Vector3(.25f+sway,-.25f,.4f),new Vector3(0,-.147f,.3f),t)-Vector3.forward*kick;
            fps.fieldOfView=Mathf.Lerp(HipFov,AdsFov,t);
            rifle.localRotation=Quaternion.Euler(p.reload>0?25:0,0,p.reload>0?-25:0);
        }
        // Fair visibility check: obstacles block sight; a forward cone prevents omniscience.
        public bool CanSeePlayer()
        {
            Vector3 eye=b.position+Vector3.up*1.6f,d=p.position+Vector3.up*1.1f-eye;
            if(b.blind>0||d.magnitude>32||Vector3.Angle(botRoot.forward,d)>65||WallBlocks(eye,eye+d)||InWall(b.position)||InWall(p.position))return false;
            return Physics.Raycast(eye,d.normalized,out RaycastHit h,32,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&h.collider.transform==playerRoot;
        }
        public void SetDecision(string message){Decision=message;}
        public void MoveOpponent(Vector3 worldDirection,float speed,float dt)
        {
            worldDirection.y=0;bc.Move((Vector3.ClampMagnitude(worldDirection,1)*Mathf.Clamp(speed,0,6)+Vector3.down*2)*dt);b.position=botRoot.position;
        }
        public void AimOpponent(Vector3 worldPoint,float turnSpeed,float dt)
        {
            Vector3 d=worldPoint-b.position;d.y=0;if(d.sqrMagnitude<.01f)return;
            botRoot.rotation=Quaternion.RotateTowards(botRoot.rotation,Quaternion.LookRotation(d),turnSpeed*dt);b.yaw=botRoot.eulerAngles.y;
        }
        public void FireOpponent(Vector3 aimPoint)
        {
            if(b.shotCooldown>0||b.reload>0||b.health<=0)return;
            if(b.ammo<=0){b.reload=1.5f;return;}
            b.ammo--;b.shotCooldown=Bot.fireInterval;
            Vector3 start=b.position+Vector3.up*1.4f;
            // Angular cone (was a world-X/Y box, so shots along the X axis never spread sideways).
            // Moving while shooting widens it, same idea as the player's spread.
            Vector3 v=bc.velocity;v.y=0;
            float spread=Mathf.Lerp(4.5f,.25f,Bot.accuracy)*(1+v.magnitude*.35f);
            Vector3 dir=(aimPoint-start).normalized,side=Vector3.Cross(Vector3.up,dir).normalized;
            dir=Quaternion.AngleAxis((NextRandom()-.5f)*2*spread,Vector3.up)*Quaternion.AngleAxis((NextRandom()-.5f)*1.2f*spread,side)*dir;
            Vector3 end=start+dir*50;
            if(Physics.Raycast(start,dir,out RaycastHit h,50,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                end=h.point;
                if(h.collider.transform==playerRoot)
                {
                    bool head=h.point.y-p.position.y>(p.crouched?.95f:1.5f); // top of the (crouch-aware) capsule
                    p.health=Mathf.Max(0,p.health-(head?HeadDamage:BodyDamage));damageFlash=.18f;
                }
            }
            Trace(start+dir*.5f,end);
        }
        const float BodyDamage=40,HeadDamage=60;
        float NextRandom(){rng=1664525u*rng+1013904223u;return (rng&0xffffff)/16777216f;}
        void BuiltInBot(float dt)
        {
            routeAge+=dt;patrolTimer-=dt;botAbilityTimer-=dt;strafeTimer-=dt;
            var k=Bot;
            bool seen=CanSeePlayer();
            bool hurt=b.health<previousBotHealth;previousBotHealth=b.health;
            Vector3 home=scenario.opponentPosition;
            // Hearing: running footsteps; higher ranks pick them up from further. Walking, crouching and ADS-walking are silent.
            Vector3 pv=pc.velocity;pv.y=0;
            if(!seen&&pc.isGrounded&&pv.magnitude>3.6f&&Vector3.Distance(b.position,p.position)<k.hearing){lastKnown=p.position;memory=Mathf.Max(memory,2.5f);}
            if(hurt){patrolTimer=0;Decision="Taking fire / repositioning";}
            if(b.blind>0){reaction=0;Decision="Flashed / backing off blind";MoveOpponent(-botRoot.forward,2f,dt);}
            else if(seen)
            {
                lastKnown=p.position;memory=6;reaction+=dt;
                float distance=Vector3.Distance(b.position,p.position);
                bool retreat=k.usesCover&&(b.health<=BodyDamage||b.reload>0); // one body shot from death
                if(retreat)
                {
                    if(patrolTimer<=0||hurt)PickPatrol(b.position,4.5f,true);
                    Decision="Low health or reload / seek cover";Navigate(patrolGoal,3.4f,dt,false);
                }
                else if(k.rushes)
                {
                    // Low-rank habit: sprint at the enemy and spray (moving widens the spread).
                    Decision=k.Name+" / running straight at you";
                    if(distance>4f)Navigate(lastKnown,3.7f,dt,false);
                }
                else
                {
                    bool push=p.reload>0||(b.health>p.health&&distance>8);
                    if(push){Decision=k.Name+" / advantage detected, advance";Navigate(lastKnown,3f,dt,false);}
                    else if(k.strafes){Decision=k.Name+" / strafe and trade";Strafe(dt,2.4f);}
                    else Decision=k.Name+" / planted, taking the fight";
                }
                // Tracking lags behind a moving target (less with higher accuracy), so wide strafes are harder to hit.
                Vector3 aimAt=p.position-pv*((1-k.accuracy)*.3f);
                AimOpponent(aimAt,k.turnSpeed,dt);
                Vector3 flat=aimAt-b.position;flat.y=0;
                bool onTarget=Vector3.Angle(botRoot.forward,flat)<6f; // no shooting before the crosshair is on you
                if(reaction>=k.reaction&&onTarget&&b.shotCooldown<=0&&b.reload<=0)
                {
                    bool head=NextRandom()<k.headshot;
                    FireOpponent(aimAt+Vector3.up*(head?(p.crouched?1.05f:1.62f):(p.crouched?.8f:1.15f)));
                }
            }
            else
            {
                // Reaction drains instead of resetting, so briefly losing sight (a strafe behind a pillar) keeps the bot ready.
                reaction=Mathf.Max(0,reaction-dt*2);memory=Mathf.Max(0,memory-dt);
                // Pre-aim the last known spot whenever there is a clear line to it.
                Vector3 eye=b.position+Vector3.up*1.5f;
                bool preaim=k.preaims&&memory>0&&!Solid(eye,lastKnown+Vector3.up*1.2f)&&!WallBlocks(eye,lastKnown);
                if(b.ultActive&&memory>0){Decision="Run It Back / pushing with no risk";Navigate(lastKnown,3.7f,dt,!preaim);}
                else if(memory>0&&Vector3.Distance(b.position,lastKnown)>1.1f)
                {
                    Decision=hurt?"Shot from out of view / turning to the shooter":"Investigate noise / last known position";
                    Navigate(lastKnown,k.rushes?3.7f:2.5f,dt,!preaim);
                }
                else
                {
                    if(memory>0)memory=0; // reached the spot and nobody is there
                    if(patrolTimer<=0||Vector3.Distance(b.position,patrolGoal)<.8f)
                        PickPatrol(k.rushes?Vector3.zero:home,k.rushes?19f:9f,false);
                    Decision=k.rushes?k.Name+" / wandering the courtyard":k.Name+" / alternate patrol and hold";
                    // Better players pause to hold angles; low ranks keep wandering.
                    if(k.rushes||Mathf.FloorToInt(elapsed/3f)%3!=2)
                        Navigate(patrolGoal,k.rushes?3.4f:2.2f,dt);
                    else AimOpponent(b.position+Quaternion.Euler(0,Mathf.Sin(elapsed)*55,0)*Vector3.forward,90,dt);
                }
                if(preaim)AimOpponent(lastKnown,hurt?600:260,dt);
                // Reload in the lull, not mid-fight.
                if(k.usesCover&&b.reload<=0&&b.ammo<12&&memory<2)b.reload=1.5f;
            }
            if(b.blind<=0)BotAbilities(seen);
            // Like a real player, look away from your own curveball until it pops.
            foreach(var f in fx)if(f.owner==1&&f.kind==FxKind.Flash&&!f.done){AimOpponent(2*b.position-f.pos,1e6f,dt);break;}
            foreach(var f in fx)
            {
                if(f.owner==1||!InFire(f,b.position,out Vector3 c))continue;
                Vector3 away=b.position-c;away.y=0;
                MoveOpponent(away.sqrMagnitude>.01f?away.normalized:-botRoot.forward,3.5f,dt);Decision="Escaping fire";break;
            }
            if(b.ammo<=0&&b.reload<=0)b.reload=1.5f;
        }
        // Counter-strafe rhythm: shuffle sideways, then plant for the last quarter-second so shots land.
        void Strafe(float dt,float speed)
        {
            if(strafeTimer<=0){strafeDir=NextRandom()<.5f?-1:1;strafeTimer=.45f+NextRandom()*.5f;}
            if(strafeTimer<.25f)return;
            if(!navigation.IsWalkable(b.position+botRoot.right*strafeDir*.8f))strafeDir=-strafeDir;
            MoveOpponent(botRoot.right*strafeDir,speed,dt);
        }
        // Phoenix habits, each gated on the throw actually paying off:
        // ult before a push, heal in own molly when hurt and safe, wall off a losing fight,
        // curve a flash only when it will pop in view of the enemy, molly only on a clear arc.
        void BotAbilities(bool seen)
        {
            if(botAbilityTimer>0||elapsed<2f)return; // the opening matches the setup: nothing cast in the first moments
            Vector3 eye=b.position+Vector3.up*1.4f,toward=lastKnown-b.position;toward.y=0;
            float dist=toward.magnitude;toward=dist>.01f?toward/dist:botRoot.forward;
            float full=scenario.startingHealth,half=full*.5f;
            Vector3 pv=pc.velocity;pv.y=0;
            var k=Bot;
            if(k.ult&&b.ultReady&&!seen&&memory>2f&&b.health>half&&dist>3&&dist<18)
            {RunItBack(1);Decision="Run It Back / free entry";}
            else if(!seen&&b.health<=half&&b.mollies>0&&memory<3f&&!InWall(b.position))
            {Fireball(1,eye+botRoot.forward*.3f,Vector3.down*4);Decision="Hot hands / healing in own fire";}
            else if(k.wall&&seen&&b.walls>0&&(b.health<=BodyDamage||b.reload>0||p.health>b.health+40))
            {
                // Lay the line across the sightline a little ahead, running sideways so it crosses between us.
                float yaw=Quaternion.LookRotation(toward).eulerAngles.y+(NextRandom()<.5f?-90:90);
                Vector3 along=Quaternion.Euler(0,yaw,0)*Vector3.forward;
                Blaze(1,b.position+toward*Mathf.Min(2f,dist*.4f)-along*4f,yaw,false);Decision="Blaze / walling off the sightline";
                patrolTimer=0; // retreat logic picks fresh cover behind the wall
            }
            else if(k.flash&&!seen&&memory>0&&dist>3&&dist<14&&b.flashes>0&&FlashSide(eye,toward,out float curve))
            {Curveball(1,eye,Quaternion.Euler(0,-curve*50,0)*toward,curve);Decision="Curveball / flashing before the peek";}
            else if(b.mollies>0&&b.health>full*.66f&&(!seen&&memory>0&&dist>3&&dist<16||seen&&dist>4&&dist<12&&pv.magnitude<1.5f)&&Lob(eye,lastKnown,out Vector3 vel))
            {Fireball(1,eye,vel);Decision=seen?"Hot hands / burning them off the angle":"Hot hands / clearing last known";}
            else return;
            botAbilityTimer=k.abilityCooldown*(.8f+NextRandom()*.4f);
        }
        // Picks the curve side whose pop point the enemy will see; false if neither side works.
        bool FlashSide(Vector3 eye,Vector3 toward,out float curve)
        {
            float first=NextRandom()<.5f?-1:1;
            for(int i=0;i<2;i++)
            {
                curve=i==0?first:-first;
                var f=new AbilityFx{pos=eye+Quaternion.Euler(0,-curve*50,0)*toward*.5f,vel=Quaternion.Euler(0,-curve*50,0)*toward*13f};
                for(float t=0;t<Fuse;t+=.02f){if(t>.12f)f.vel=Quaternion.Euler(0,curve*CurveRate*.02f,0)*f.vel;if(Step(ref f,.02f,out _))break;}
                Vector3 enemyEye=lastKnown+Vector3.up*1.5f;
                if(!Solid(f.pos,enemyEye)&&!WallBlocks(f.pos,enemyEye))return true;
            }
            curve=0;return false;
        }
        // Ballistic lob onto target before the molly fuse; tries a flat then a high arc, false if both clip geometry.
        bool Lob(Vector3 eye,Vector3 target,out Vector3 vel)
        {
            Vector3 flat=target-eye;flat.y=0;
            for(int k=0;k<2;k++)
            {
                float t=k==0?Mathf.Clamp(flat.magnitude/12,.45f,.95f):.95f;
                vel=(target-eye)/t+Vector3.up*(.5f*Gravity*t);
                Vector3 prev=eye;bool clear=true;
                for(int i=1;i<=10&&clear;i++){float s=t*i/10;Vector3 q=eye+vel*s+Vector3.down*(.5f*Gravity*s*s);clear=!Solid(prev,q);prev=q;}
                if(clear)return true;
            }
            vel=default;return false;
        }
        ref ActorSnapshot A(int who){if(who==0)return ref p;return ref b;}
        void Equip(int id,bool has){armed=armed==id||!has?0:id;armTime=0;}
        void Curveball(int who,Vector3 from,Vector3 dir,float curve)
        {
            ref var a=ref A(who);if(a.flashes<=0)return;a.flashes--;
            fx.Add(new AbilityFx{kind=FxKind.Flash,id=nextFxId++,owner=who,pos=from+dir*.5f,vel=dir.normalized*13f,curve=curve});
        }
        void Fireball(int who,Vector3 from,Vector3 velocity)
        {
            ref var a=ref A(who);if(a.mollies<=0)return;a.mollies--;
            fx.Add(new AbilityFx{kind=FxKind.Fireball,id=nextFxId++,owner=who,pos=from,vel=velocity});
        }
        void Blaze(int who,Vector3 origin,float heading,bool steer)
        {
            ref var a=ref A(who);if(a.walls<=0)return;a.walls--;
            var path=new Vector3[WallPathMax];path[0]=origin;
            fx.Add(new AbilityFx{kind=FxKind.Wall,id=nextFxId++,owner=who,pos=origin,yaw=heading,steer=steer,path=path,count=1});
        }
        // Run It Back: mark now; dying or the timer running out returns you to the mark with full health.
        void RunItBack(int who)
        {
            ref var a=ref A(who);if(!a.ultReady||a.ultActive)return;
            a.ultReady=false;a.ultActive=true;a.ultTimer=UltLife;a.ultMark=a.position;a.ultYaw=a.yaw;
        }
        bool Revive(ref ActorSnapshot a)
        {
            if(!a.ultActive||(a.health>0&&a.ultTimer>0))return false;
            a.ultActive=false;a.position=a.ultMark;a.yaw=a.ultYaw;a.health=scenario.startingHealth;a.blind=0;a.verticalSpeed=0;
            return true;
        }
        void TickAbilities(float dt)
        {
            for(int i=fx.Count-1;i>=0;i--)
            {
                var f=fx[i];f.age+=dt;
                if(f.kind==FxKind.Flash)
                {
                    if(f.age<Fuse){if(f.age>.12f)f.vel=Quaternion.Euler(0,f.curve*CurveRate*dt,0)*f.vel;if(Step(ref f,dt,out _))f.vel=Vector3.zero;}
                    else if(!f.done){f.done=true;Blind(f);}
                    else if(f.age>Fuse+.35f){fx.RemoveAt(i);continue;} // keeps the burst visible briefly
                }
                else if(f.kind==FxKind.Fireball)
                {
                    // Explodes on hitting the ground or when the fuse runs out; mid-air bursts drop the fire to the floor below.
                    f.vel+=Vector3.down*Gravity*dt;
                    bool hit=Step(ref f,dt,out RaycastHit h);
                    if(hit&&h.normal.y<=.6f){f.vel=new Vector3(0,Mathf.Min(f.vel.y,0),0);hit=false;}
                    if(hit||f.age>=MollyFuse)
                    {
                        if(!hit&&!Physics.Raycast(f.pos,Vector3.down,out h,30,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)){fx.RemoveAt(i);continue;}
                        f.kind=FxKind.Fire;f.pos=h.point;f.vel=Vector3.zero;f.age=0;
                    }
                }
                else if(f.kind==FxKind.Fire){if(f.age>ZoneLife){fx.RemoveAt(i);continue;}}
                else
                {
                    if(f.age>WallLife){fx.RemoveAt(i);continue;}
                    if(!f.done)
                    {
                        // The flame line runs forward; while FIRE is held its head turns toward the caster's crosshair.
                        if(f.steer&&f.owner==0&&!playerFireHeld)f.steer=false;
                        if(f.steer)f.yaw=Mathf.MoveTowardsAngle(f.yaw,f.owner==0?p.yaw:b.yaw,WallTurn*dt);
                        float step=Mathf.Min(WallGrow*dt,WallMax-f.length);
                        Vector3 next=f.pos+Quaternion.Euler(0,f.yaw,0)*Vector3.forward*step;
                        if(Solid(f.pos+Vector3.up*.5f,next+Vector3.up*.5f))f.done=true;
                        else
                        {
                            f.pos=next;f.length+=step;f.done=f.length>=WallMax-.01f;
                            if(f.length>=f.count*WallSpacing&&f.count<f.path.Length)f.path[f.count++]=f.pos;
                        }
                    }
                }
                fx[i]=f;
            }
            Burn(ref p,0,dt);Burn(ref b,1,dt);
        }
        bool Step(ref AbilityFx f,float dt,out RaycastHit hit)
        {
            Vector3 d=f.vel*dt;hit=default;
            if(d.sqrMagnitude<1e-8f)return false;
            if(Physics.Raycast(f.pos,d.normalized,out hit,d.magnitude+.1f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {f.pos=hit.point+hit.normal*.1f;return true;}
            f.pos+=d;return false;
        }
        bool Solid(Vector3 a,Vector3 c)=>Physics.Linecast(a,c,out RaycastHit h,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&h.collider.transform!=playerRoot&&h.collider.transform!=botRoot;
        // Blinds anyone who can see the orb when it pops, thrower included (as in Valorant).
        void Blind(AbilityFx f)
        {
            for(int who=0;who<2;who++)
            {
                ref var a=ref A(who);
                Vector3 eye=who==0?fps.transform.position:b.position+Vector3.up*1.6f,look=who==0?fps.transform.forward:botRoot.forward,d=f.pos-eye;
                if(d.magnitude>35||WallBlocks(f.pos,eye))continue;
                // The thrower always gets checked (it's their own orb, right in front of them); others need clear line of sight.
                if(who!=f.owner&&Physics.Linecast(f.pos,eye,out RaycastHit h,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&h.collider.transform!=(who==0?playerRoot:botRoot))continue;
                float angle=Vector3.Angle(look,d);
                float amount=angle<60?FlashMax:angle<110?.3f:0;
                if(amount>a.blind){a.blind=amount;a.blindFrom=f.pos;}
            }
        }
        // Fire heals its owner and burns the other side. Walls are vision-only: bullets pass through, like Blaze.
        void Burn(ref ActorSnapshot a,int who,float dt)
        {
            foreach(var f in fx)
            {
                if(!InFire(f,a.position,out _))continue;
                if(f.owner==who)a.health=Mathf.Min(scenario.startingHealth,a.health+12.5f*dt);
                else{a.health=Mathf.Max(0,a.health-(f.kind==FxKind.Fire?60f:30f)*dt);if(who==0)damageFlash=.18f;}
            }
        }
        bool InWall(Vector3 at){foreach(var f in fx)if(f.kind==FxKind.Wall&&InFire(f,at,out _))return true;return false;}
        bool InFire(AbilityFx f,Vector3 at,out Vector3 center)
        {
            center=f.pos;
            if(f.kind==FxKind.Fire){Vector3 d=at-f.pos;return new Vector2(d.x,d.z).magnitude<ZoneRadius&&Mathf.Abs(d.y)<1.5f;}
            if(f.kind!=FxKind.Wall)return false;
            int n=WallPoints(f);float best=float.MaxValue;
            for(int i=0;i<n-1;i++)
            {
                Vector3 ac=wallPts[i+1]-wallPts[i],ap=at-wallPts[i];ac.y=0;ap.y=0;
                Vector3 q=wallPts[i]+ac*Mathf.Clamp01(Vector3.Dot(ap,ac)/Mathf.Max(ac.sqrMagnitude,1e-6f)),d=at-q;d.y=0;
                if(d.magnitude<best){best=d.magnitude;center=q;}
            }
            return best<.7f&&at.y-f.pos.y<WallHeight;
        }
        // Fills wallPts with the laid path plus the moving head; returns the point count.
        int WallPoints(AbilityFx f){int n=0;for(int i=0;i<f.count;i++)wallPts[n++]=f.path[i];wallPts[n++]=f.pos;return n;}
        bool WallBlocks(Vector3 a,Vector3 c)
        {
            foreach(var f in fx)
            {
                if(f.kind!=FxKind.Wall)continue;
                int n=WallPoints(f);
                for(int i=0;i<n-1;i++)if(Crosses(a,c,wallPts[i],wallPts[i+1]))return true;
            }
            return false;
        }
        static bool Crosses(Vector3 a,Vector3 b,Vector3 c,Vector3 d)
        {
            float den=(b.x-a.x)*(d.z-c.z)-(b.z-a.z)*(d.x-c.x);if(Mathf.Abs(den)<1e-6f)return false;
            float t=((c.x-a.x)*(d.z-c.z)-(c.z-a.z)*(d.x-c.x))/den,u=((c.x-a.x)*(b.z-a.z)-(c.z-a.z)*(b.x-a.x))/den;
            return t>=0&&t<=1&&u>=0&&u<=1;
        }
        static List<AbilityFx> Copy(List<AbilityFx> src)
        {
            var list=new List<AbilityFx>(src.Count);
            foreach(var f in src){var c=f;if(c.path!=null)c.path=(Vector3[])c.path.Clone();list.Add(c);}
            return list;
        }
        // Visuals are driven from fx data every frame (keyed by id), so live play, replay and scrubbing all render the same thing.
        void LateUpdate()
        {
            Transform cam=fps.enabled?fps.transform:overhead.transform;float dt=Time.deltaTime;
            // Walk cycle: legs swing with distance travelled; teleports (replay scrub, Run It Back) don't count.
            Vector3 stepMove=botRoot.position-lastBotPos;stepMove.y=0;lastBotPos=botRoot.position;
            float moved=stepMove.magnitude>.5f?0:stepMove.magnitude;walkPhase+=moved*3.2f;
            float swing=Mathf.Sin(walkPhase)*Mathf.Clamp01(moved/Mathf.Max(dt,1e-4f)/3f)*32f;
            botLegs[0].localRotation=Quaternion.Euler(swing,0,0);botLegs[1].localRotation=Quaternion.Euler(-swing,0,0);
            liveViews.Clear();
            foreach(var f in fx)Show(f.id,f.kind,f,cam,dt);
            // Run It Back marker where the actor will return (keys -1 / -2).
            if(p.ultActive)Show(-1,FxKind.Fire,new AbilityFx{pos=p.ultMark,owner=-1},cam,dt);
            if(b.ultActive)Show(-2,FxKind.Fire,new AbilityFx{pos=b.ultMark,owner=-1},cam,dt);
            deadViews.Clear();
            foreach(var kv in views)if(!liveViews.Contains(kv.Key))deadViews.Add(kv.Key);
            foreach(int id in deadViews)Drop(id);
            bool held=Mode==PracticeMode.Live&&!paused&&armed!=0;
            if(hand.gameObject.activeSelf!=held)hand.gameObject.SetActive(held);
            if(held)
            {
                handCore.sharedMaterial=armed==1?flashMat:fireMat;handHaloR.sharedMaterial=armed==1?ringMat:glowMat;
                handHalo.rotation=cam.rotation;handHalo.localScale=Vector3.one*(.28f+.03f*Mathf.Sin(Time.time*30));
            }
        }
        void Show(int id,FxKind kind,AbilityFx f,Transform cam,float dt)
        {
            bool marker=f.owner==-1;
            liveViews.Add(id);
            if(views.TryGetValue(id,out var v)&&v.kind!=kind){Drop(id);v=null;} // fireball turned into fire
            if(v==null){v=MakeView(kind,marker);views[id]=v;}
            Transform r=v.root.transform;
            if(marker)
            {
                r.position=f.pos;v.halo.rotation=cam.rotation;v.halo.localScale=new Vector3(.9f,2.2f,1)*(1+.06f*Mathf.Sin(Time.time*12));
                Emit(v,dt,25,()=>f.pos+(Vector3)(UnityEngine.Random.insideUnitCircle*.4f),.8f);
                return;
            }
            switch(kind)
            {
                case FxKind.Flash:
                    r.position=f.pos;v.halo.rotation=cam.rotation;
                    if(!f.done){if(v.popped){v.popped=false;var em=v.ps.emission;em.enabled=true;}v.core.gameObject.SetActive(true);v.haloR.sharedMaterial=ringMat;v.halo.localScale=Vector3.one*(.7f+.1f*Mathf.Sin(Time.time*40));}
                    else
                    {
                        if(!v.popped){v.popped=true;v.core.gameObject.SetActive(false);var em=v.ps.emission;em.enabled=false;v.haloR.sharedMaterial=Own(v,burstMat);}
                        float k=Mathf.Clamp01((f.age-Fuse)/.35f);
                        v.halo.localScale=Vector3.one*Mathf.Lerp(1.5f,8f,Mathf.Sqrt(k));
                        v.haloR.sharedMaterial.color=new Color(1,.92f,.88f,1-k);
                    }
                    break;
                case FxKind.Fireball:
                    r.position=f.pos;v.halo.rotation=cam.rotation;v.halo.localScale=Vector3.one*(1.3f+.2f*Mathf.Sin(Time.time*35));
                    break;
                case FxKind.Fire:
                {
                    float fade=Mathf.Clamp01(f.age/.15f)*Mathf.Clamp01((ZoneLife-f.age)/.5f);
                    r.position=f.pos+Vector3.up*.04f;
                    var m=v.surfR.sharedMaterial;m.color=new Color(1,1,1,fade);m.mainTextureOffset=new Vector2(f.age*.03f,f.age*.05f);
                    Emit(v,dt,90*fade,()=>f.pos+new Vector3(0,.05f,0)+Flat(UnityEngine.Random.insideUnitCircle*ZoneRadius*.95f),1.8f);
                    break;
                }
                case FxKind.Wall:
                {
                    int n=WallPoints(f);
                    var verts=new Vector3[n*2];var uv=new Vector2[n*2];var tris=new int[(n-1)*6];float along=0;
                    for(int i=0;i<n;i++)
                    {
                        if(i>0)along+=Vector3.Distance(wallPts[i],wallPts[i-1]);
                        // The head of a travelling wall is a low ribbon that rises to full height behind it.
                        float h=WallHeight*(f.done?1:Mathf.Clamp01(.3f+(f.length-along)/2f));
                        verts[2*i]=wallPts[i];verts[2*i+1]=wallPts[i]+Vector3.up*h;
                        uv[2*i]=new Vector2(along/3,0);uv[2*i+1]=new Vector2(along/3,1);
                        if(i<n-1){int t=i*6,q=2*i;tris[t]=q;tris[t+1]=q+1;tris[t+2]=q+2;tris[t+3]=q+1;tris[t+4]=q+3;tris[t+5]=q+2;}
                    }
                    var mesh=v.mesh.sharedMesh;mesh.Clear();mesh.vertices=verts;mesh.uv=uv;mesh.triangles=tris;mesh.RecalculateBounds();
                    float fade=Mathf.Clamp01(f.age/.1f)*Mathf.Clamp01((WallLife-f.age)/.6f);
                    var m=v.surfR.sharedMaterial;m.color=new Color(1,1,1,fade);m.mainTextureOffset=new Vector2(f.age*.25f,0);
                    int segs=n-1;
                    Emit(v,dt,f.length*14*fade,()=>{int i=UnityEngine.Random.Range(0,Mathf.Max(1,segs));return Vector3.Lerp(wallPts[i],wallPts[Mathf.Min(i+1,n-1)],UnityEngine.Random.value)+Vector3.up*.1f;},2.6f);
                    break;
                }
            }
        }
        static Vector3 Flat(Vector2 v)=>new Vector3(v.x,0,v.y);
        // Rate-based manual emission of rising flame particles at positions picked by `where`.
        void Emit(FxView v,float dt,float rate,Func<Vector3> where,float rise)
        {
            v.debt+=rate*dt;
            for(;v.debt>=1;v.debt--)
                v.ps.Emit(new ParticleSystem.EmitParams{position=where(),velocity=new Vector3(UnityEngine.Random.Range(-.3f,.3f),rise*UnityEngine.Random.Range(.6f,1.2f),UnityEngine.Random.Range(-.3f,.3f)),applyShapeToPosition=false},1);
        }
        FxView MakeView(FxKind kind,bool marker)
        {
            var v=new FxView{kind=kind,root=new GameObject("Ability fx "+(marker?"Run It Back":kind.ToString()))};
            Transform r=v.root.transform;r.SetParent(transform,false);
            Color hot=new Color(1,.6f,.25f),deep=new Color(1,.25f,.2f);
            if(marker)
            {
                var ring=PracticeWorld.Part("Ground ring",new Vector3(0,.05f,0),Vector3.one*1.8f,ringMat,r,false,PrimitiveType.Quad);ring.transform.localRotation=Quaternion.Euler(90,0,0);
                v.halo=Billboard(r,glowMat,1,out v.haloR);v.halo.localPosition=Vector3.up*1.1f;
                v.ps=FxArt.Particles(r,sparkMat,hot,deep,1.2f,.25f,0,0);
                return v;
            }
            switch(kind)
            {
                case FxKind.Flash:
                    v.core=PracticeWorld.Part("Core",Vector3.zero,Vector3.one*.16f,flashMat,r,false,PrimitiveType.Sphere).transform;
                    v.halo=Billboard(r,ringMat,.8f,out v.haloR);
                    v.ps=FxArt.Particles(r,sparkMat,new Color(1,.5f,.45f),new Color(1,.9f,.8f),.25f,.12f,.4f,70);
                    break;
                case FxKind.Fireball:
                    v.core=PracticeWorld.Part("Core",Vector3.zero,Vector3.one*.28f,fireMat,r,false,PrimitiveType.Sphere).transform;
                    v.halo=Billboard(r,glowMat,1.3f,out v.haloR);
                    v.ps=FxArt.Particles(r,sparkMat,hot,deep,.35f,.45f,.3f,90);
                    break;
                case FxKind.Fire:
                {
                    var g=PracticeWorld.Part("Lava",Vector3.zero,Vector3.one*ZoneRadius*2,lavaMat,r,false,PrimitiveType.Quad);g.transform.localRotation=Quaternion.Euler(90,0,0);
                    v.surfR=g.GetComponent<Renderer>();v.surfR.sharedMaterial=Own(v,lavaMat);
                    v.ps=FxArt.Particles(r,sparkMat,hot,deep,.7f,.7f,0,0);
                    break;
                }
                case FxKind.Wall:
                {
                    var g=new GameObject("Flame curtain");g.transform.SetParent(r,false);
                    v.mesh=g.AddComponent<MeshFilter>();v.mesh.sharedMesh=new Mesh();
                    v.surfR=g.AddComponent<MeshRenderer>();v.surfR.sharedMaterial=Own(v,flameMat);
                    v.surfR.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                    v.ps=FxArt.Particles(r,sparkMat,new Color(1,.85f,.5f),deep,.6f,.8f,0,0);
                    break;
                }
            }
            return v;
        }
        static Material Own(FxView v,Material shared){var m=new Material(shared);v.mats.Add(m);return m;}
        Transform Billboard(Transform parent,Material m,float size,out Renderer r)
        {
            var g=PracticeWorld.Part("Glow",Vector3.zero,Vector3.one*size,m,parent,false,PrimitiveType.Quad);
            r=g.GetComponent<Renderer>();r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;return g.transform;
        }
        void Drop(int id)
        {
            if(!views.TryGetValue(id,out var v))return;
            foreach(var m in v.mats)Destroy(m);
            if(v.mesh!=null)Destroy(v.mesh.sharedMesh);
            Destroy(v.root);views.Remove(id);
        }
        void PickPatrol(Vector3 center,float radius,bool seekCover)
        {
            Vector3 best=navigation.Snap(center);float bestScore=float.MinValue;
            for(int i=0;i<20;i++)
            {
                float angle=NextRandom()*Mathf.PI*2,dist=radius*(.35f+NextRandom()*.65f);
                Vector3 candidate=navigation.Snap(center+new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*dist);
                if(Vector3.Distance(candidate,center)>radius+.5f)continue;
                float score=Vector3.Distance(candidate,b.position);
                if(seekCover)score=(Solid(candidate+Vector3.up*1.3f,lastKnown+Vector3.up*1.3f)?20:0)-score;
                if(score>bestScore){best=candidate;bestScore=score;}
            }
            patrolGoal=best;patrolTimer=seekCover?3.5f:8f;
        }
        void Navigate(Vector3 goal,float speed,float dt,bool turn=true)
        {
            if(routeAge>.65f||(goal-routeGoal).sqrMagnitude>1f||route.Count==0)
            {route.Clear();route.AddRange(navigation.Route(b.position,goal));routeGoal=goal;routeAge=0;}
            while(route.Count>0&&Vector3.Distance(b.position,route[0])<.32f)route.RemoveAt(0);
            while(route.Count>1&&ClearWalk(b.position,route[1]))route.RemoveAt(0); // 4-way grid zigzags otherwise
            Vector3 d=(route.Count>0?route[0]:goal)-b.position;d.y=0;
            if(d.magnitude<.15f||route.Count==0&&d.magnitude>1.5f)return; // empty route + far goal = unreachable
            MoveOpponent(d.normalized,Mathf.Min(speed,d.magnitude/Mathf.Max(dt,.001f)),dt);
            if(turn)AimOpponent(b.position+d,230,dt);
        }
        bool ClearWalk(Vector3 a,Vector3 c)
        {
            Vector3 d=c-a;d.y=0;float len=d.magnitude;if(len<.01f)return true;
            return !Physics.CapsuleCast(a+Vector3.up*.4f,a+Vector3.up*1.4f,.32f,d/len,out RaycastHit h,len,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)||h.collider.transform==playerRoot;
        }
        void Trace(Vector3 start,Vector3 end)
        {
            var g=new GameObject("Shot trace");g.layer=2;var line=g.AddComponent<LineRenderer>();line.sharedMaterial=tracerMaterial;line.positionCount=2;line.SetPosition(0,start);line.SetPosition(1,end);line.startWidth=.018f;line.endWidth=.006f;
            effects.Add(g);Destroy(g,.07f);effects.RemoveAll(x=>x==null);
        }
        void ClearEffects(){foreach(var e in effects)if(e!=null)Destroy(e);effects.Clear();}
        PracticeSnapshot Capture()=>new PracticeSnapshot{time=elapsed,player=p,opponent=b,lastKnown=lastKnown,memory=memory,reaction=reaction,rng=rng,decision=Decision,patrolGoal=patrolGoal,patrolTimer=patrolTimer,previousBotHealth=previousBotHealth,externalState=externalBot!=null?externalBot.CaptureBotState():"",abilityFx=Copy(fx),botAbilityTimer=botAbilityTimer,strafeTimer=strafeTimer,strafeDir=strafeDir};
        void Restore(PracticeSnapshot f)
        {
            p=f.player;b=f.opponent;lastKnown=f.lastKnown;memory=f.memory;reaction=f.reaction;rng=f.rng;Decision=f.decision;elapsed=f.time;patrolGoal=f.patrolGoal;patrolTimer=f.patrolTimer;previousBotHealth=f.previousBotHealth;route.Clear();routeAge=0;ApplyTransforms();
            fx.Clear();if(f.abilityFx!=null)fx.AddRange(Copy(f.abilityFx));botAbilityTimer=f.botAbilityTimer;strafeTimer=f.strafeTimer;strafeDir=f.strafeDir==0?1:f.strafeDir;armed=0;rifle.gameObject.SetActive(true);
            if(externalBot!=null)externalBot.RestoreBotState(f.externalState);
        }
        void ApplyTransforms()
        {
            SetCrouch();fps.transform.localPosition=Vector3.up*(p.crouched?1.05f:1.62f);
            pc.enabled=false;bc.enabled=false;playerRoot.SetPositionAndRotation(p.position,Quaternion.Euler(0,p.yaw,0));botRoot.SetPositionAndRotation(b.position,Quaternion.Euler(0,b.yaw,0));pc.enabled=true;bc.enabled=true;
            fps.transform.localRotation=Quaternion.Euler(p.pitch,0,0);Physics.SyncTransforms();
        }
        void EndAttempt()
        {
            Mode=PracticeMode.Results;Lock(false);outcome=(b.health<=0?"DUEL WON":p.health<=0?"DUEL LOST":"TIME LIMIT")+"  /  "+elapsed.ToString("0.0")+"s\nHealth remaining: "+p.health.ToString("0")+"   |   Enemy: "+b.health.ToString("0");
        }
        void OpenReview()
        {
            if(frames.Count==0)return;review.Clear();review.AddRange(frames);reviewIndex=0;reviewClock=review[0].time;reviewPlaying=true;Mode=PracticeMode.Review;Lock(false);ClearEffects();Restore(review[0]);
        }
        void TakeOver()
        {
            var f=review[reviewIndex];if(f.player.health<=0||f.opponent.health<=0)return;
            previous=outcome;Restore(f);frames.Clear();frames.Add(Capture());EnterLive();
        }
        void Placement()
        {
            if(Mouse.current==null||!Mouse.current.leftButton.wasPressedThisFrame)return;
            Vector2 mouse=Mouse.current.position.ReadValue();Vector2 ui=new Vector2(mouse.x*1600f/Screen.width,(Screen.height-mouse.y)*900f/Screen.height);
            if(ui.x<425||ui.y<80||new Rect(455,100,410,64).Contains(ui))return;
            Ray ray=overhead.ScreenPointToRay(mouse);
            if(!Physics.Raycast(ray,out RaycastHit h,100,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))return;
            if(h.normal.y<.9f||h.point.y>.15f){notice="Choose clear paving, not a roof or cover.";return;}
            Vector3 point=new Vector3(h.point.x,.06f,h.point.z);
            foreach(Collider c in Physics.OverlapCapsule(point+Vector3.up*.35f,point+Vector3.up*1.45f,.32f))
            {
                if(c.transform==playerRoot||c.transform==botRoot)continue;
                notice="Too close to a wall or obstacle.";return;
            }
            Vector3 other=placement==0?scenario.opponentPosition:scenario.playerPosition;
            if(Vector3.Distance(point,other)<1.2f){notice="Leave space between characters.";return;}
            if(placement==0)scenario.playerPosition=point;else scenario.opponentPosition=point;
            ResetActors();notice="Position updated. Select a facing direction, then start.";
        }
        void OnApplicationFocus(bool focus){if(!focus&&Mode==PracticeMode.Live){paused=true;Lock(false);}}
        void OnDisable(){Lock(false);}
        void OnGUI()
        {
            if(fps==null)return;
            Matrix4x4 oldMatrix=GUI.matrix;
            GUI.matrix=Matrix4x4.TRS(Vector3.zero,Quaternion.identity,new Vector3(Screen.width/1600f,Screen.height/900f,1));
            try { DrawInterface(); } finally { GUI.matrix=oldMatrix;GUI.color=Color.white; }
        }
        void DrawInterface()
        {
            if(Mode==PracticeMode.Setup){SetupGUI();return;}
            float dim=Mode==PracticeMode.Live?1:.6f; // softer in replay so the duel stays watchable
            if(p.blind>0)
            {
                // White-pink wash plus a starburst centred on where the flare popped.
                float a=Mathf.Clamp01(p.blind/.4f)*dim;
                TacticalUI.Fill(new Rect(0,0,1600,900),new Color(1,.88f,.86f,a*.85f));
                Vector3 s=fps.WorldToScreenPoint(p.blindFrom);
                Vector2 c=s.z>0?new Vector2(s.x*1600f/Screen.width,(Screen.height-s.y)*900f/Screen.height):new Vector2(800,450);
                GUI.color=new Color(1,.95f,.92f,a);GUI.DrawTexture(new Rect(c.x-1000,c.y-1000,2000,2000),FxArt.Starburst);GUI.color=Color.white;
            }
            else if(InWall(p.position))
            {
                TacticalUI.Fill(new Rect(0,0,1600,900),new Color(1,.45f,.3f,.65f*dim));
                GUI.color=new Color(1,1,1,.8f*dim);GUI.DrawTextureWithTexCoords(new Rect(0,300,1600,600),FxArt.Flame,new Rect(Time.time*.2f,0,2,1));GUI.color=Color.white;
            }
            if(p.ultActive){Color e=new Color(1,.45f,.2f,.55f);TacticalUI.Fill(new Rect(0,0,1600,8),e);TacticalUI.Fill(new Rect(0,892,1600,8),e);TacticalUI.Fill(new Rect(0,0,8,900),e);TacticalUI.Fill(new Rect(1592,0,8,900),e);}
            TacticalUI.Fill(new Rect(0,0,1600,85),new Color(.035f,.065f,.09f,.86f));
            TacticalUI.Fill(new Rect(25,25,4,35),TacticalUI.Coral);
            TacticalUI.Text(new Rect(42,17,340,33),"SECOND CHANCE",23,null,true);
            TacticalUI.Text(new Rect(43,49,350,20),"PIAZZA  /  PRACTICE PROTOCOL",11,TacticalUI.Muted,true);
            TacticalUI.Text(new Rect(650,12,300,25),Mode==PracticeMode.Live?"LIVE ATTEMPT":Mode==PracticeMode.Review?"REPLAY REVIEW":"ROUND COMPLETE",12,TacticalUI.Mint,true,TextAnchor.MiddleCenter);
            TacticalUI.Text(new Rect(650,34,300,43),elapsed.ToString("00.0"),32,null,true,TextAnchor.MiddleCenter);
            TacticalUI.Text(new Rect(1250,20,320,27),Bot.Name+" OPPONENT",14,null,true,TextAnchor.MiddleRight);
            TacticalUI.Text(new Rect(1250,48,320,22),"1 VS 1  /  TRAINING",11,TacticalUI.Muted,false,TextAnchor.MiddleRight);
            TacticalUI.Card(new Rect(25,105,390,67));
            TacticalUI.Label(40,113,"OPPONENT INTENT");TacticalUI.Text(new Rect(40,137,360,27),Decision,13);
            TacticalUI.Fill(new Rect(25,785,300,90),new Color(.035f,.065f,.09f,.85f));
            TacticalUI.Text(new Rect(45,791,100,62),p.health.ToString("0"),47,null,true);
            TacticalUI.Label(145,802,"HEALTH");TacticalUI.Text(new Rect(145,825,160,26),"READY FOR CONTACT",10,TacticalUI.Muted);
            TacticalUI.Bar(new Rect(45,861,260,4),p.health/Mathf.Max(1,scenario.startingHealth),p.health<=BodyDamage?TacticalUI.Coral:TacticalUI.Mint);
            TacticalUI.Fill(new Rect(1275,785,300,90),new Color(.035f,.065f,.09f,.85f));
            TacticalUI.Label(1295,794,"SENTINEL  /  RIFLE");
            TacticalUI.Text(new Rect(1295,817,260,45),p.reload>0?"RELOADING":p.ammo.ToString("00")+"  /  25",p.reload>0?22:36,null,true);
            if(p.reload>0)TacticalUI.Bar(new Rect(1295,863,260,3),1-p.reload/1.5f,TacticalUI.Coral);
            TacticalUI.Text(new Rect(340,850,920,25),"WASD  MOVE    SPACE  JUMP    CTRL  CROUCH    SHIFT  WALK    RMB  AIM    R  RELOAD    Q / E / C  EQUIP    X  RUN IT BACK    ESC  PAUSE",12,TacticalUI.Muted,false,TextAnchor.MiddleCenter);
            AbilitySlot(335,"C","BLAZE",p.walls>0?"x"+p.walls:"USED",p.walls>0,3);
            AbilitySlot(565,"Q","CURVEBALL",p.flashes>0?"x"+p.flashes:"USED",p.flashes>0,1);
            AbilitySlot(795,"E","HOT HANDS",p.mollies>0?"x"+p.mollies:"USED",p.mollies>0,2);
            AbilitySlot(1025,"X","RUN IT BACK",p.ultActive?p.ultTimer.ToString("0.0")+"s":p.ultReady?"READY":"USED",p.ultReady||p.ultActive,4);
            if(p.ultActive)
            {
                TacticalUI.Text(new Rect(650,92,300,20),"RUN IT BACK",11,TacticalUI.Coral,true,TextAnchor.MiddleCenter);
                TacticalUI.Bar(new Rect(650,114,300,4),p.ultTimer/UltLife,TacticalUI.Coral);
            }
            if(Mode==PracticeMode.Live&&!paused)
            {
                if(armed!=0)TacticalUI.Text(new Rect(450,478,700,24),armTime<EquipTime?"EQUIPPING...":armed==1?"FIRE  CURVE LEFT   /   ALT FIRE  CURVE RIGHT":armed==2?"FIRE  THROW FIREBALL":"FIRE  CAST   /   HOLD FIRE TO BEND TOWARD YOUR CROSSHAIR",12,TacticalUI.Mint,true,TextAnchor.MiddleCenter);
                Color c=hitFlash>0?TacticalUI.Coral:TacticalUI.Mint;
                // Crosshair gap tracks the real spread cone (~9.7 virtual px per degree at 78° FOV).
                float g=5+PlayerSpread()*9.7f*Mathf.Tan(HipFov*.5f*Mathf.Deg2Rad)/Mathf.Tan(fps.fieldOfView*.5f*Mathf.Deg2Rad);
                if(adsBlend>.6f)TacticalUI.Fill(new Rect(798,448,4,4),TacticalUI.Coral); // red dot in the optic
                TacticalUI.Fill(new Rect(793-g,449,7,2),c);TacticalUI.Fill(new Rect(800+g,449,7,2),c);
                TacticalUI.Fill(new Rect(799,443-g,2,7),c);TacticalUI.Fill(new Rect(799,450+g,2,7),c);
                if(damageFlash>0){TacticalUI.Fill(new Rect(0,0,1600,10),TacticalUI.Coral);TacticalUI.Fill(new Rect(0,890,1600,10),TacticalUI.Coral);}
                return;
            }
            if(Mode==PracticeMode.Review){ReviewGUI();return;}
            TacticalUI.Fill(new Rect(0,85,1600,700),new Color(.02f,.04f,.06f,.55f));
            TacticalUI.Card(new Rect(470,180,660,560));
            TacticalUI.Fill(new Rect(470,180,660,4),TacticalUI.Coral);
            TacticalUI.Label(505,205,paused?"SESSION CONTROL":"POST-ROUND ANALYSIS");
            TacticalUI.Text(new Rect(505,240,590,65),paused?"TAKE A BREATH.":b.health<=0?"DUEL WON.":p.health<=0?"TRY ANOTHER ANGLE.":"ATTEMPT COMPLETE.",36,null,true);
            if(paused)
            {
                TacticalUI.Text(new Rect(505,315,590,40),"Your scenario is paused. Resume when you are ready.",16,TacticalUI.Muted);
                if(TacticalUI.Button(new Rect(505,385,590,55),"RESUME PRACTICE",true)){paused=false;Lock(true);}
                if(TacticalUI.Button(new Rect(505,452,590,48),"END ATTEMPT")){paused=false;EndAttempt();}
            }
            else
            {
                TacticalUI.Card(new Rect(505,320,285,88));TacticalUI.Card(new Rect(810,320,285,88));
                TacticalUI.Label(525,330,"TIME ELAPSED");TacticalUI.Text(new Rect(525,351,245,45),elapsed.ToString("0.0")+" SEC",29,null,true);
                TacticalUI.Label(830,330,"HEALTH REMAINING");TacticalUI.Text(new Rect(830,351,245,45),p.health.ToString("0")+" HP",29,TacticalUI.Mint,true);
                TacticalUI.Text(new Rect(505,419,590,65),previous!=""?"Previous attempt\n"+previous:"Every decision is another chance. Review this attempt and try a new approach.",14,TacticalUI.Muted);
                if(TacticalUI.Button(new Rect(505,502,590,55),"REVIEW THIS ATTEMPT  >",true))OpenReview();
            }
            if(TacticalUI.Button(new Rect(505,583,590,48),"RETRY SAME SCENARIO")){previous=outcome;StartAttempt();}
            if(TacticalUI.Button(new Rect(505,643,590,48),"BACK TO SCENARIO BUILDER")){ResetActors();ShowSetup();}
        }
        void AbilitySlot(float x,string key,string name,string state,bool ready,int id)
        {
            Rect r=new Rect(x,800,220,40);TacticalUI.Fill(r,new Color(.035f,.065f,.09f,.85f));if(armed==id)TacticalUI.Border(r,TacticalUI.Mint);
            TacticalUI.Text(new Rect(x+12,800,30,40),key,18,ready?TacticalUI.Mint:TacticalUI.Muted,true);
            TacticalUI.Text(new Rect(x+40,800,110,40),name,11,null,true);
            TacticalUI.Text(new Rect(x+145,800,65,40),state,13,ready?TacticalUI.Mint:TacticalUI.Coral,true,TextAnchor.MiddleRight);
        }
        void ReviewGUI()
        {
            // Bottom-mounted controls keep the duel visible while scrubbing.
            TacticalUI.Card(new Rect(350,630,900,195));
            TacticalUI.Fill(new Rect(350,630,900,3),TacticalUI.Mint);
            TacticalUI.Label(375,647,"REWIND. REPOSITION. REPLAY.");
            TacticalUI.Text(new Rect(970,645,255,25),review[reviewIndex].time.ToString("0.0")+" / "+review[review.Count-1].time.ToString("0.0")+" SEC",14,null,true,TextAnchor.MiddleRight);
            int i=Mathf.RoundToInt(TacticalUI.Slider(new Rect(375,684,850,24),reviewIndex,0,Mathf.Max(1,review.Count-1)));
            i=Mathf.Clamp(i,0,review.Count-1);
            if(i!=reviewIndex){reviewIndex=i;reviewClock=review[i].time;reviewPlaying=false;Restore(review[i]);}
            if(TacticalUI.Button(new Rect(375,726,185,47),reviewPlaying?"PAUSE":"PLAY"))
            {if(reviewIndex==review.Count-1){reviewIndex=0;reviewClock=review[0].time;}reviewPlaying=!reviewPlaying;}
            GUI.enabled=p.health>0&&b.health>0;
            if(TacticalUI.Button(new Rect(575,726,400,47),"TAKE CONTROL AT THIS MOMENT",true))TakeOver();GUI.enabled=true;
            if(TacticalUI.Button(new Rect(990,726,235,47),"SCENARIO SETUP")){ResetActors();ShowSetup();}
            TacticalUI.Text(new Rect(375,785,850,25),p.health<=0||b.health<=0?"Move the timeline to before elimination to take control.":"State replay: position, aim, health, ammo and abilities. Shot traces are not replayed.",12,TacticalUI.Muted);
        }
        void SetupGUI()
        {
            TacticalUI.Fill(new Rect(0,0,1600,80),TacticalUI.Ink);
            TacticalUI.Fill(new Rect(25,22,4,37),TacticalUI.Coral);
            TacticalUI.Text(new Rect(42,16,430,38),"SECOND CHANCE",27,null,true);
            TacticalUI.Text(new Rect(43,53,430,18),"TACTICAL PRACTICE  /  BUILD YOUR NEXT MOVE",10,TacticalUI.Muted,true);
            TacticalUI.Text(new Rect(1175,20,390,40),"PIAZZA  //  SCENARIO 01",17,null,true,TextAnchor.MiddleRight);
            TacticalUI.Fill(new Rect(0,80,425,820),TacticalUI.Ink);
            TacticalUI.Fill(new Rect(424,80,1,820),TacticalUI.Line);
            TacticalUI.Label(25,98,"01  /  DEPLOYMENT");
            if(TacticalUI.Button(new Rect(25,132,180,45),"PLAYER",false,placement==0))placement=0;
            if(TacticalUI.Button(new Rect(215,132,185,45),"OPPONENT",false,placement==1))placement=1;
            TacticalUI.Text(new Rect(25,186,375,35),"Select a character, then click clear paving on the map.",13,TacticalUI.Muted);
            TacticalUI.Text(new Rect(25,231,375,23),"PLAYER FACING                         "+scenario.playerYaw.ToString("0")+"°",12,null,true);
            float py=TacticalUI.Slider(new Rect(25,260,375,22),scenario.playerYaw,0,359);
            TacticalUI.Text(new Rect(25,293,375,23),"OPPONENT FACING                     "+scenario.opponentYaw.ToString("0")+"°",12,null,true);
            float by=TacticalUI.Slider(new Rect(25,322,375,22),scenario.opponentYaw,0,359);
            if(py!=scenario.playerYaw||by!=scenario.opponentYaw){scenario.playerYaw=py;scenario.opponentYaw=by;ResetActors();}
            TacticalUI.Fill(new Rect(25,361,375,1),TacticalUI.Line);TacticalUI.Label(25,376,"02  /  OPPONENT PROFILE");
            // Riot ID lookup sets the rank; the 3x3 grid picks it by hand.
            TacticalUI.Fill(new Rect(25,405,262,38),TacticalUI.Panel);TacticalUI.Border(new Rect(25,405,262,38),TacticalUI.Line);
            var field=new GUIStyle(GUI.skin.textField){fontSize=14,alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,10,0,0)};
            field.normal.background=field.focused.background=field.hover.background=null;field.normal.textColor=field.focused.textColor=field.hover.textColor=TacticalUI.White;
            scenario.riotId=GUI.TextField(new Rect(25,405,262,38),scenario.riotId??"",40,field);
            if(string.IsNullOrEmpty(scenario.riotId))TacticalUI.Text(new Rect(36,405,240,38),"Riot ID  (Name#TAG)",13,TacticalUI.Muted);
            GUI.enabled=!rankLookup;
            if(TacticalUI.Button(new Rect(295,405,105,38),rankLookup?"...":"LOOK UP"))
            {
                rankLookup=true;rankStatus="Asking the Riot API...";
                StartCoroutine(RiotRank.Lookup(scenario.riotId,(msg,r)=>{rankLookup=false;rankStatus=msg;if(r>=0)scenario.rank=r;}));
            }
            GUI.enabled=true;
            TacticalUI.Text(new Rect(25,447,375,36),rankStatus,11,TacticalUI.Muted);
            for(int n=0;n<RankProfile.Names.Length;n++)
                if(TacticalUI.Button(new Rect(25+n%3*128,489+n/3*42,119,36),RankProfile.Names[n],false,scenario.rank==n))scenario.rank=n;
            TacticalUI.Text(new Rect(25,617,375,36),Bot.Summary,11,TacticalUI.Muted);
            TacticalUI.Fill(new Rect(25,658,375,1),TacticalUI.Line);
            if(TacticalUI.Button(new Rect(25,682,180,39),"SAVE SETUP"))
            {try{File.WriteAllText(savePath,JsonUtility.ToJson(scenario,true));notice="Scenario saved. Ready for your next session.";}catch(Exception e){notice=e.Message;}}
            if(TacticalUI.Button(new Rect(215,682,185,39),"LOAD SETUP"))
            {try{if(File.Exists(savePath)){var loaded=JsonUtility.FromJson<PracticeScenario>(File.ReadAllText(savePath));if(loaded!=null){scenario=loaded;ResetActors();notice="Saved scenario loaded.";}}else notice="No saved scenario yet.";}catch(Exception e){notice=e.Message;}}
            TacticalUI.Text(new Rect(25,733,375,52),notice,12,TacticalUI.Muted);
            if(TacticalUI.Button(new Rect(25,798,375,59),"START PRACTICE  >",true))StartAttempt();
            TacticalUI.Text(new Rect(25,866,375,22),externalBot==null?"LOCAL SIMULATION  /  DEMO OPPONENT":"EXTERNAL BOT CONNECTED",10,TacticalUI.Muted,true);
            TacticalUI.Card(new Rect(455,100,410,64));TacticalUI.Label(475,108,"TACTICAL OVERVIEW");
            TacticalUI.Text(new Rect(475,134,370,22),placement==0?"Click the map to deploy your player.":"Click the map to deploy the opponent.",14);
            TacticalUI.Text(new Rect(1140,848,420,30),"ONE MOMENT. ANOTHER CHANCE.",15,TacticalUI.White,true,TextAnchor.MiddleRight);
            Marker(scenario.playerPosition,"YOU",TacticalUI.Mint);
            Marker(scenario.opponentPosition,"OPPONENT",TacticalUI.Coral);
        }
        void Marker(Vector3 pos,string label,Color color)
        {
            Vector3 s=overhead.WorldToViewportPoint(pos+Vector3.up*2);
            float x=s.x*1600,y=(1-s.y)*900;
            TacticalUI.Fill(new Rect(x-5,y-5,10,10),color);
            TacticalUI.Fill(new Rect(x-60,y-38,120,26),TacticalUI.Ink);
            TacticalUI.Text(new Rect(x-58,y-38,116,26),label,11,color,true,TextAnchor.MiddleCenter);
            float yaw=label=="YOU"?scenario.playerYaw:scenario.opponentYaw;
            Vector2 dir=new Vector2(Mathf.Sin(yaw*Mathf.Deg2Rad),-Mathf.Cos(yaw*Mathf.Deg2Rad));
            for(int n=1;n<5;n++)TacticalUI.Fill(new Rect(x+dir.x*n*6-1,y+dir.y*n*6-1,3,3),color);
        }
    }
}
