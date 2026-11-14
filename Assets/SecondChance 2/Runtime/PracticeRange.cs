using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
namespace SecondChance
{
    public enum OpponentStyle { Aggressive, Balanced, Defensive }
    public enum PracticeMode { Setup, Live, Review, Results }
    [Serializable] public struct ActorSnapshot
    {
        public Vector3 position;
        public float yaw, pitch, health, verticalSpeed, shotCooldown, reload, bloom;
        public int ammo;
        public bool crouched;
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
    }
    [Serializable] public class PracticeScenario
    {
        public Vector3 playerPosition = new Vector3(0,.06f,-15);
        public Vector3 opponentPosition = new Vector3(8,.06f,6);
        public float playerYaw, opponentYaw = 180;
        public OpponentStyle style = OpponentStyle.Balanced;
        public float reactionSeconds = .5f;
        public float accuracy = .65f;
        public int startingHealth = 100;
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
        public OpponentStyle Style => scenario.style;
        public Vector3 PlayerPosition => p.position;
        public Vector3 OpponentPosition => b.position;
        public float OpponentHealth => b.health;
        public float PlayerHealth => p.health;
        public string Decision { get; private set; } = "Holding angle";
        Camera fps, overhead;
        CharacterController pc, bc;
        Transform playerRoot, botRoot, rifle, botVisual;
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
        void Awake()
        {
            geometry = PracticeWorld.Build(transform);
            navigation.Build(geometry);
            playerRoot=new GameObject("Player").transform; playerRoot.SetParent(transform);
            pc=playerRoot.gameObject.AddComponent<CharacterController>(); pc.height=1.8f;pc.radius=.3f;pc.center=Vector3.up*.9f;
            fps=new GameObject("Player Camera").AddComponent<Camera>();fps.transform.SetParent(playerRoot,false);fps.transform.localPosition=Vector3.up*1.62f;
            fps.nearClipPlane=.03f;fps.fieldOfView=78;fps.clearFlags=CameraClearFlags.SolidColor;fps.backgroundColor=new Color(.55f,.7f,.83f);fps.tag="MainCamera";
            fps.gameObject.AddComponent<AudioListener>();
            rifle=PracticeWorld.Rifle(fps.transform);rifle.localScale=Vector3.one*.7f;
            // Sleeves/gloves make the first-person view feel held rather than floating.
            PracticeWorld.Part("Support glove",new Vector3(-.055f,-.1f,.3f),new Vector3(.14f,.13f,.18f),PracticeWorld.dark,rifle,false);
            PracticeWorld.Part("Trigger glove",new Vector3(.015f,-.14f,-.12f),new Vector3(.12f,.14f,.16f),PracticeWorld.dark,rifle,false);
            botRoot=new GameObject("Opponent - attach your bot adapter here").transform;botRoot.SetParent(transform);
            bc=botRoot.gameObject.AddComponent<CharacterController>();bc.height=1.8f;bc.radius=.3f;bc.center=Vector3.up*.9f;
            botVisual=new GameObject("Opponent armour").transform;botVisual.SetParent(botRoot,false);
            PracticeWorld.Part("Armoured torso",new Vector3(0,1.15f,0),new Vector3(.55f,.65f,.32f),PracticeWorld.teal,botVisual,false);
            PracticeWorld.Part("Helmet",new Vector3(0,1.7f,0),new Vector3(.35f,.37f,.35f),PracticeWorld.red,botVisual,false,PrimitiveType.Sphere);
            PracticeWorld.Part("Visor",new Vector3(0,1.72f,.17f),new Vector3(.28f,.09f,.04f),PracticeWorld.dark,botVisual,false);
            for(int s=-1;s<=1;s+=2)
            {
                PracticeWorld.Part("Leg",new Vector3(s*.16f,.43f,0),new Vector3(.2f,.85f,.23f),PracticeWorld.dark,botVisual,false);
                PracticeWorld.Part("Arm",new Vector3(s*.34f,1.1f,.08f),new Vector3(.16f,.55f,.19f),PracticeWorld.red,botVisual,false);
            }
            var br=PracticeWorld.Rifle(botVisual);br.localPosition=new Vector3(.16f,1.17f,.22f);br.localScale=Vector3.one*.85f;
            overhead=new GameObject("Scenario planning camera").AddComponent<Camera>();overhead.transform.SetParent(transform);
            overhead.transform.position=new Vector3(0,50,0);overhead.transform.rotation=Quaternion.Euler(90,0,0);overhead.orthographic=true;overhead.orthographicSize=27;
            overhead.clearFlags=CameraClearFlags.SolidColor;overhead.backgroundColor=new Color(.07f,.11f,.14f);
            sound=gameObject.AddComponent<AudioSource>();sound.playOnAwake=false;sound.volume=.15f;
            shotSound=MakeShotSound();
            tracerMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit"));tracerMaterial.color=new Color(1,.75f,.3f);
            ResetActors();ShowSetup();
        }
        static AudioClip MakeShotSound()
        {
            var clip=AudioClip.Create("Synthetic practice rifle",4410,1,44100,false);
            var samples=new float[4410]; var random=new System.Random(8);
            for(int i=0;i<samples.Length;i++) samples[i]=(float)(random.NextDouble()*2-1)*Mathf.Exp(-i/500f)*.6f;
            clip.SetData(samples,0);return clip;
        }
        // A script recompile during Play mode wipes the nav grid and generated clip,
        // which froze the bot. OnEnable runs after that reload, so rebuild them here.
        void OnEnable()
        {
            if(geometry!=null&&!navigation.Built){navigation.Build(geometry);route.Clear();}
            if(sound!=null&&shotSound==null)shotSound=MakeShotSound();
        }
        ActorSnapshot Fresh(Vector3 pos,float yaw) => new ActorSnapshot{position=pos,yaw=yaw,health=scenario.startingHealth,ammo=25};
        void ResetActors()
        {
            p=Fresh(scenario.playerPosition,scenario.playerYaw); b=Fresh(scenario.opponentPosition,scenario.opponentYaw);
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
            Mode=PracticeMode.Live;paused=false;fps.enabled=true;overhead.enabled=false;rifle.gameObject.SetActive(true);Lock(true);ClearEffects();
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
            }
            p.position=playerRoot.position;b.position=botRoot.position;
            frames.Add(Capture());PoseGun();
            if(p.health<=0||b.health<=0||elapsed>=60)EndAttempt();
        }
        void TickTimers(ref ActorSnapshot a,float dt)
        {
            a.shotCooldown=Mathf.Max(0,a.shotCooldown-dt);
            if(a.reload>0){a.reload-=dt;if(a.reload<=0){a.reload=0;a.ammo=25;}}
        }
        void PlayerTick(float dt)
        {
            var k=Keyboard.current;var m=Mouse.current;if(k==null||m==null)return;
            var delta=m.delta.ReadValue()*.10f;p.yaw+=delta.x;p.pitch=Mathf.Clamp(p.pitch-delta.y,-80,80);
            playerRoot.rotation=Quaternion.Euler(0,p.yaw,0);fps.transform.localRotation=Quaternion.Euler(p.pitch,0,0);
            float x=(k.dKey.isPressed?1:0)-(k.aKey.isPressed?1:0),z=(k.wKey.isPressed?1:0)-(k.sKey.isPressed?1:0);
            Vector3 move=Vector3.ClampMagnitude(playerRoot.right*x+playerRoot.forward*z,1);
            // Crouch while held; only stand up if nothing is overhead.
            bool wantCrouch=k.leftCtrlKey.isPressed||k.cKey.isPressed;
            if(wantCrouch!=p.crouched&&(wantCrouch||!Physics.Raycast(playerRoot.position+Vector3.up*1.25f,Vector3.up,.6f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)))
            {p.crouched=wantCrouch;SetCrouch();}
            var cam=fps.transform.localPosition;cam.y=Mathf.MoveTowards(cam.y,p.crouched?1.05f:1.62f,dt*5);fps.transform.localPosition=cam;
            if(pc.isGrounded&&p.verticalSpeed<0)p.verticalSpeed=-2;
            if(pc.isGrounded&&k.spaceKey.wasPressedThisFrame)p.verticalSpeed=6.3f;
            p.verticalSpeed-=20*dt;
            pc.Move((move*(p.crouched?2f:k.leftShiftKey.isPressed?2.3f:4.5f)+Vector3.up*p.verticalSpeed)*dt);p.position=playerRoot.position;
            p.bloom=Mathf.MoveTowards(p.bloom,0,dt*5);
            if(k.rKey.wasPressedThisFrame&&p.ammo<25&&p.reload<=0)p.reload=1.5f;
            if(m.leftButton.isPressed&&p.shotCooldown<=0&&p.reload<=0&&p.ammo>0)
            {
                p.ammo--;p.shotCooldown=.13f;kick=.045f;sound.PlayOneShot(shotSound);
                // Gunfire provides an audible position, not permanent wall vision.
                if(Vector3.Distance(p.position,b.position)<22f)
                { lastKnown=p.position;memory=5f; }
                Vector2 c=UnityEngine.Random.insideUnitCircle*PlayerSpread();
                p.bloom=Mathf.Min(p.bloom+.45f,3.5f);
                Vector3 start=fps.transform.position,dir=fps.transform.rotation*Quaternion.Euler(-c.y,c.x,0)*Vector3.forward;
                Vector3 end=start+dir*80;
                if(Physics.Raycast(start,dir,out RaycastHit hit,80,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                {
                    end=hit.point;
                    if(hit.collider.transform==botRoot||hit.collider.transform.IsChildOf(botRoot))
                    {b.health=Mathf.Max(0,b.health-(hit.point.y-b.position.y>1.5f?100:34));hitFlash=.15f;}
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
            return (.1f+p.bloom+move)*(p.crouched?.55f:1)+(pc.isGrounded?0:6f);
        }
        void SetCrouch(){pc.height=p.crouched?1.2f:1.8f;pc.center=Vector3.up*pc.height/2;}
        void PoseGun()
        {
            kick=Mathf.MoveTowards(kick,0,Time.deltaTime*.3f);
            float sway=Mode==PracticeMode.Live&&!paused?Mathf.Sin(Time.time*5)*.002f:0;
            rifle.localPosition=new Vector3(.25f+sway,-.25f,.4f-kick);
            rifle.localRotation=Quaternion.Euler(p.reload>0?25:0,0,p.reload>0?-25:0);
        }
        // Fair visibility check: obstacles block sight; a forward cone prevents omniscience.
        public bool CanSeePlayer()
        {
            Vector3 eye=b.position+Vector3.up*1.6f,d=p.position+Vector3.up*1.1f-eye;
            if(d.magnitude>32||Vector3.Angle(botRoot.forward,d)>65)return false;
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
            b.ammo--;b.shotCooldown=Style==OpponentStyle.Aggressive?.25f:.4f;
            Vector3 start=b.position+Vector3.up*1.4f;
            float spread=Mathf.Lerp(2.3f,.06f,scenario.accuracy);
            Vector3 target=aimPoint+new Vector3(NextRandom()-.5f,NextRandom()-.5f,0)*spread;
            Vector3 dir=(target-start).normalized,end=start+dir*50;
            if(Physics.Raycast(start,dir,out RaycastHit h,50,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {end=h.point;if(h.collider.transform==playerRoot){p.health=Mathf.Max(0,p.health-20);damageFlash=.18f;}}
            Trace(start+dir*.5f,end);
        }
        float NextRandom(){rng=1664525u*rng+1013904223u;return (rng&0xffffff)/16777216f;}
        void BuiltInBot(float dt)
        {
            routeAge+=dt;patrolTimer-=dt;
            bool seen=CanSeePlayer();
            bool hurt=b.health<previousBotHealth;previousBotHealth=b.health;
            Vector3 home=scenario.opponentPosition;
            if(hurt){patrolTimer=0;Decision="Taking fire / repositioning";}
            if(seen)
            {
                lastKnown=p.position;memory=6;reaction+=dt;
                float distance=Vector3.Distance(b.position,p.position);
                bool retreat=b.health<=34||b.reload>0;
                if(Style==OpponentStyle.Defensive)
                {
                    // Never chase beyond the spawn neighbourhood.
                    if(retreat||hurt)PickPatrol(home,4f,true);
                    else if(patrolTimer<=0)PickPatrol(home,3.5f,false);
                    Decision=retreat?"Defensive / recover behind cover":"Defensive / short peek near home";
                    Navigate(patrolGoal,retreat?3.2f:1.8f,dt,false);
                }
                else if(retreat)
                {
                    if(patrolTimer<=0||hurt)PickPatrol(b.position,4.5f,true);
                    Decision="Low health or reload / seek cover";Navigate(patrolGoal,3.4f,dt,false);
                }
                else if(Style==OpponentStyle.Aggressive)
                {
                    Decision=p.reload>0?"Enemy reloading / fast push":"Aggressive / close distance";
                    if(distance>4.5f)Navigate(lastKnown,p.reload>0?4.4f:3.7f,dt,false);
                    else MoveOpponent(botRoot.right*Mathf.Sin(elapsed*2),2f,dt);
                }
                else
                {
                    bool push=p.reload>0||(b.health>p.health&&distance>8);
                    if(push){Decision="Balanced / advantage detected, advance";Navigate(lastKnown,3f,dt,false);}
                    else
                    {
                        if(patrolTimer<=0||hurt)PickPatrol(b.position,3f,false);
                        Decision="Balanced / reposition and hold";Navigate(patrolGoal,2.1f,dt,false);
                    }
                }
                AimOpponent(p.position,320,dt);
                if(reaction>=scenario.reactionSeconds)FireOpponent(p.position+Vector3.up*(p.crouched?.8f:1.15f));
            }
            else
            {
                reaction=0;memory=Mathf.Max(0,memory-dt);
                if(Style==OpponentStyle.Defensive)
                {
                    if(Vector3.Distance(b.position,home)>4.5f)patrolGoal=navigation.Snap(home);
                    else if(patrolTimer<=0||Vector3.Distance(b.position,patrolGoal)<.6f)PickPatrol(home,3.5f,false);
                    Decision="Defensive / patrol home perimeter";Navigate(patrolGoal,1.7f,dt);
                    if(memory>0)AimOpponent(lastKnown,160,dt);
                }
                else if(memory>0&&Vector3.Distance(b.position,lastKnown)>1.1f)
                {
                    Decision="Investigate gunfire / last known position";
                    Navigate(lastKnown,Style==OpponentStyle.Aggressive?3.7f:2.5f,dt);
                }
                else
                {
                    if(patrolTimer<=0||Vector3.Distance(b.position,patrolGoal)<.8f)
                        PickPatrol(Style==OpponentStyle.Aggressive?Vector3.zero:home,Style==OpponentStyle.Aggressive?19f:9f,false);
                    Decision=Style==OpponentStyle.Aggressive?"Aggressive / sweep courtyard routes":"Balanced / alternate patrol and hold";
                    // Balanced pauses occasionally; aggressive continues searching.
                    if(Style==OpponentStyle.Aggressive||Mathf.FloorToInt(elapsed/3f)%3!=2)
                        Navigate(patrolGoal,Style==OpponentStyle.Aggressive?3.4f:2.2f,dt);
                    else AimOpponent(b.position+Quaternion.Euler(0,Mathf.Sin(elapsed)*55,0)*Vector3.forward,90,dt);
                }
            }
            if(b.ammo<=0&&b.reload<=0)b.reload=1.5f;
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
                if(seekCover)
                {
                    Vector3 eye=candidate+Vector3.up*1.3f,target=lastKnown+Vector3.up*1.3f;
                    if(Physics.Linecast(eye,target,out RaycastHit h,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)&&h.collider.transform!=playerRoot&&h.collider.transform!=botRoot)score+=20;
                }
                if(score>bestScore){best=candidate;bestScore=score;}
            }
            patrolGoal=best;patrolTimer=Style==OpponentStyle.Defensive?3.5f:8f;
        }
        void Navigate(Vector3 goal,float speed,float dt,bool turn=true)
        {
            if(routeAge>.65f||(goal-routeGoal).sqrMagnitude>1f||route.Count==0)
            {route.Clear();route.AddRange(navigation.Route(b.position,goal));routeGoal=goal;routeAge=0;}
            while(route.Count>0&&Vector3.Distance(b.position,route[0])<.32f)route.RemoveAt(0);
            if(route.Count==0)return;
            Vector3 d=route[0]-b.position;d.y=0;
            MoveOpponent(d.normalized,Mathf.Min(speed,d.magnitude/Mathf.Max(dt,.001f)),dt);
            if(turn)AimOpponent(b.position+d,230,dt);
        }
        void Trace(Vector3 start,Vector3 end)
        {
            var g=new GameObject("Shot trace");g.layer=2;var line=g.AddComponent<LineRenderer>();line.sharedMaterial=tracerMaterial;line.positionCount=2;line.SetPosition(0,start);line.SetPosition(1,end);line.startWidth=.018f;line.endWidth=.006f;
            effects.Add(g);Destroy(g,.07f);effects.RemoveAll(x=>x==null);
        }
        void ClearEffects(){foreach(var e in effects)if(e!=null)Destroy(e);effects.Clear();}
        PracticeSnapshot Capture()=>new PracticeSnapshot{time=elapsed,player=p,opponent=b,lastKnown=lastKnown,memory=memory,reaction=reaction,rng=rng,decision=Decision,patrolGoal=patrolGoal,patrolTimer=patrolTimer,previousBotHealth=previousBotHealth,externalState=externalBot!=null?externalBot.CaptureBotState():""};
        void Restore(PracticeSnapshot f)
        {
            p=f.player;b=f.opponent;lastKnown=f.lastKnown;memory=f.memory;reaction=f.reaction;rng=f.rng;Decision=f.decision;elapsed=f.time;patrolGoal=f.patrolGoal;patrolTimer=f.patrolTimer;previousBotHealth=f.previousBotHealth;route.Clear();routeAge=0;ApplyTransforms();
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
            TacticalUI.Fill(new Rect(0,0,1600,85),new Color(.035f,.065f,.09f,.86f));
            TacticalUI.Fill(new Rect(25,25,4,35),TacticalUI.Coral);
            TacticalUI.Text(new Rect(42,17,340,33),"SECOND CHANCE",23,null,true);
            TacticalUI.Text(new Rect(43,49,350,20),"PIAZZA  /  PRACTICE PROTOCOL",11,TacticalUI.Muted,true);
            TacticalUI.Text(new Rect(650,12,300,25),Mode==PracticeMode.Live?"LIVE ATTEMPT":Mode==PracticeMode.Review?"REPLAY REVIEW":"ROUND COMPLETE",12,TacticalUI.Mint,true,TextAnchor.MiddleCenter);
            TacticalUI.Text(new Rect(650,34,300,43),elapsed.ToString("00.0"),32,null,true,TextAnchor.MiddleCenter);
            TacticalUI.Text(new Rect(1250,20,320,27),Style.ToString().ToUpper()+" OPPONENT",14,null,true,TextAnchor.MiddleRight);
            TacticalUI.Text(new Rect(1250,48,320,22),"1 VS 1  /  TRAINING",11,TacticalUI.Muted,false,TextAnchor.MiddleRight);
            TacticalUI.Card(new Rect(25,105,390,67));
            TacticalUI.Label(40,113,"OPPONENT INTENT");TacticalUI.Text(new Rect(40,137,360,27),Decision,13);
            TacticalUI.Fill(new Rect(25,785,300,90),new Color(.035f,.065f,.09f,.85f));
            TacticalUI.Text(new Rect(45,791,100,62),p.health.ToString("0"),47,null,true);
            TacticalUI.Label(145,802,"HEALTH");TacticalUI.Text(new Rect(145,825,160,26),"READY FOR CONTACT",10,TacticalUI.Muted);
            TacticalUI.Bar(new Rect(45,861,260,4),p.health/Mathf.Max(1,scenario.startingHealth),p.health<35?TacticalUI.Coral:TacticalUI.Mint);
            TacticalUI.Fill(new Rect(1275,785,300,90),new Color(.035f,.065f,.09f,.85f));
            TacticalUI.Label(1295,794,"SENTINEL  /  RIFLE");
            TacticalUI.Text(new Rect(1295,817,260,45),p.reload>0?"RELOADING":p.ammo.ToString("00")+"  /  25",p.reload>0?22:36,null,true);
            if(p.reload>0)TacticalUI.Bar(new Rect(1295,863,260,3),1-p.reload/1.5f,TacticalUI.Coral);
            TacticalUI.Text(new Rect(440,850,720,25),"WASD  MOVE     SPACE  JUMP     CTRL  CROUCH     SHIFT  WALK     R  RELOAD     ESC  PAUSE",12,TacticalUI.Muted,false,TextAnchor.MiddleCenter);
            if(Mode==PracticeMode.Live&&!paused)
            {
                Color c=hitFlash>0?TacticalUI.Coral:TacticalUI.Mint;
                // Crosshair gap tracks the real spread cone (~9.7 virtual px per degree at 78° FOV).
                float g=5+PlayerSpread()*9.7f;
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
            TacticalUI.Text(new Rect(375,785,850,25),p.health<=0||b.health<=0?"Move the timeline to before elimination to take control.":"State replay: position, aim, health and ammo. Shot effects are not replayed.",12,TacticalUI.Muted);
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
            string[] names={"AGGRESSIVE","BALANCED","DEFENSIVE"};
            for(int n=0;n<3;n++)if(TacticalUI.Button(new Rect(25+n*128,409,119,42),names[n],false,(int)scenario.style==n))scenario.style=(OpponentStyle)n;
            string description=scenario.style==OpponentStyle.Aggressive?"Hunts across the courtyard. Pushes an opening.":scenario.style==OpponentStyle.Defensive?"Protects its starting area. Repositions near cover.":"Alternates patrols and holding. Pushes an advantage.";
            TacticalUI.Text(new Rect(25,463,375,40),description,14,TacticalUI.Muted);
            TacticalUI.Text(new Rect(25,517,375,25),"REACTION TIME                             "+scenario.reactionSeconds.ToString("0.00")+"s",12,null,true);
            scenario.reactionSeconds=TacticalUI.Slider(new Rect(25,548,375,22),scenario.reactionSeconds,.15f,1.5f);
            TacticalUI.Text(new Rect(25,583,375,25),"ACCURACY                                       "+Mathf.RoundToInt(scenario.accuracy*100)+"%",12,null,true);
            scenario.accuracy=TacticalUI.Slider(new Rect(25,614,375,22),scenario.accuracy,.1f,1);
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
