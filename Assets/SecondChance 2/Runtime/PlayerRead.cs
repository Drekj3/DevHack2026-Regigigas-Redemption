using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
namespace SecondChance
{
    // What the bot has scouted about this player. One record per finished attempt, the last few kept;
    // every read is recomputed from those records, so a habit fades once the player stops repeating it.
    // Lives in the scenario: it saves/loads with it and shows in the Inspector for designers.
    // ponytail: thresholds are hand-picked, not fitted to player data; tune after playtesting.
    [Serializable] public class PlayerRead
    {
        [Serializable] public struct Attempt
        {
            public bool contact;      // the approach below is known (false after taking over a replay past it)
            public Vector3 lane;      // player 10m out of spawn, or where the fight began if sooner: which way they went
            public Vector3 spot;      // player when the fight began: where to expect them
            public float time;        // ...and when
            public bool moving;       // pushed into the fight (true) or was holding an angle for it (false)
            public bool flashed;      // their Curveball blinded the bot
            public float left,right;  // seconds strafing each way (the player's own left/right) in the bot's sight
            public bool won,lost;     // the player's result
        }
        public const int Window=6;             // attempts remembered
        public const float LaneAt=10f,Near=5f; // metres
        public List<Attempt> attempts=new List<Attempt>();
        public string verdict="";              // how the last attempt went against the read

        public static float Gap(Vector3 a,Vector3 b){a-=b;a.y=0;return a.magnitude;}

        // Reads one finished attempt out of its recorded frames. `spawn` is the player's scenario start,
        // `need` how many showings this bot wants before it acts on a habit (RankProfile.reads).
        public void Learn(List<PracticeSnapshot> frames,Vector3 spawn,int need)
        {
            if(frames.Count<2)return;
            bool had=Approach(need,out Attempt exp,out _);
            var a=new Attempt();bool met=false,laned=false;
            PracticeSnapshot first=frames[0],last=frames[frames.Count-1];
            a.won=last.opponent.health<=0;a.lost=last.player.health<=0;
            for(int i=1;i<frames.Count;i++)
            {
                PracticeSnapshot f=frames[i],g=frames[i-1];float dt=f.time-g.time;if(dt<=0)continue;
                bool meets=!met&&(f.seen||f.opponent.health<g.opponent.health||f.player.health<g.player.health);
                if(!laned&&(meets||Gap(f.player.position,spawn)>=LaneAt))
                {laned=true;a.lane=f.player.position;a.contact=first.time<=0||i>1;} // a replay takeover that starts past this point hides the approach
                if(meets)
                {
                    met=true;a.spot=f.player.position;a.time=f.time;
                    int j=i;while(j>0&&frames[j].time>f.time-1f)j--;
                    a.moving=f.time>frames[j].time&&Gap(f.player.position,frames[j].player.position)/(f.time-frames[j].time)>1.5f;
                }
                if(f.opponent.blind>g.opponent.blind+.5f&&f.abilityFx!=null)
                    foreach(var x in f.abilityFx)if(x.kind==FxKind.Flash&&x.owner==0&&x.done)a.flashed=true;
                if(f.seen)
                {
                    float yaw=f.player.yaw*Mathf.Deg2Rad;Vector3 v=(f.player.position-g.player.position)/dt;
                    float side=v.x*Mathf.Cos(yaw)-v.z*Mathf.Sin(yaw); // speed along the player's own right
                    if(side>1f)a.right+=dt;else if(side<-1f)a.left+=dt;
                }
            }
            if(!met){verdict="";return;} // no fight, nothing to learn
            verdict=!had||!a.contact?"":Gap(a.lane,exp.lane)<Near?"Read held: you came the same way again and it was waiting.":"Read broken: you changed your approach and it was watching the wrong lane.";
            attempts.Add(a);if(attempts.Count>Window)attempts.RemoveAt(0);
        }

        // The player's most repeated way in among the remembered fights (ties go to the newest), once it has
        // shown `need` times. `latest` is the newest fight that went that way, `count` how many did.
        public bool Approach(int need,out Attempt latest,out int count)
        {
            latest=default;count=0;
            foreach(var a in attempts)
            {
                if(!a.contact)continue;
                int n=0;foreach(var o in attempts)if(o.contact&&Gap(a.lane,o.lane)<Near)n++;
                if(n>=count){count=n;latest=a;}
            }
            Vector3 lane=latest.lane;
            foreach(var o in attempts)if(o.contact&&Gap(lane,o.lane)<Near)latest=o;
            return count>0&&count>=need;
        }
        public int Contacts{get{int n=0;foreach(var a in attempts)if(a.contact)n++;return n;}}
        public int Flashed{get{int n=0;foreach(var a in attempts)if(a.flashed)n++;return n;}}
        // +1: strafes right under fire, -1: left, 0: no clear habit. share = how one-sided it is.
        public int Dodge(int need,out float share)
        {
            float l=0,r=0;foreach(var a in attempts){l+=a.left;r+=a.right;}
            share=l+r>0?Mathf.Max(l,r)/(l+r):0;
            return l+r<need||share<.7f?0:r>l?1:-1; // `need` seconds of strafing on record, 70% of it one way
        }
        // Sparring pressure in rank steps: sharper while the player keeps winning, softer while they keep
        // losing, never more than one rank either way. The bot's job is a close duel, not a win rate.
        public float Edge{get{int n=0;foreach(var a in attempts)n+=a.won?1:a.lost?-1:0;return Mathf.Clamp(n*.35f,-1,1);}}
        // Spawns moved: where the player came from no longer applies; how they fight still does.
        public void ForgetApproach(){for(int i=0;i<attempts.Count;i++){var a=attempts[i];a.contact=false;attempts[i]=a;}verdict="";}

        // The read in the player's terms: each habit, the evidence, and what the bot does about it.
        public string Report(int need)
        {
            if(attempts.Count==0)return "No fights on record yet. Play a round: the bot studies how you approach, flash and strafe and how the duels end, then counters whatever you repeat.";
            const string T="<color=#59E3CC><b>",E="</b></color>   ";
            var s=new StringBuilder();
            if(verdict!="")s.Append(T+"LAST ROUND"+E+verdict+"\n\n");
            int m=Contacts,fl=Flashed,d=Dodge(need,out float share),w=0,l=0;
            foreach(var x in attempts){if(x.won)w++;if(x.lost)l++;}
            s.Append(T+"APPROACH"+E);
            if(Approach(need,out Attempt a,out int n))
                s.Append(a.moving?"You pushed the same lane in "+n+" of your last "+m+" fights. Counter: it holds that angle pre-aimed and burns the lane on your timing."
                                 :"You held the same spot in "+n+" of your last "+m+" fights. Counter: it clears that spot with utility before it peeks.");
            else s.Append(m==0?"No approach on record for these spawns.":"Nothing repeated enough to act on ("+n+" of "+need+" needed). It patrols.");
            s.Append("\n\n"+T+"FLASH"+E+(fl>=need?"Your Curveball blinded it in "+fl+" rounds. Counter: it turns away once it sees the orb. Curve it in late."
                :fl>0?"Your Curveball has blinded it "+fl+" of the "+need+" times it takes to learn to turn away.":"You have not blinded it yet."));
            s.Append("\n\n"+T+"STRAFE"+E+(d!=0?"In its sights you strafe "+(d>0?"right":"left")+" "+Mathf.RoundToInt(share*100)+"% of the time. Counter: its aim leads that side.":"No one-sided strafe to exploit."));
            float e=Edge;
            s.Append("\n\n"+T+"PACE"+E+"You are "+w+"-"+l+" over the last "+attempts.Count+". "+(e>.05f?"It plays "+e.ToString("0.0")+" rank sharper.":e<-.05f?"It plays "+(-e).ToString("0.0")+" rank softer.":"It plays at the selected rank."));
            return s.ToString();
        }

        // The smallest end-to-end check of the learning pass: null when it holds.
        // Menu: Second Chance > Self-check Adaptive Read.
        public static string SelfCheck()
        {
            Vector3 spawn=new Vector3(0,0,-15);
            // One 6s attempt: run 12m along x (west if side<0), get seen at 4s, strafe right, land a flash at 5s, win at 6s.
            List<PracticeSnapshot> Round(float side)
            {
                var frames=new List<PracticeSnapshot>();
                for(int i=0;i<=60;i++)
                {
                    float t=i*.1f;
                    var f=new PracticeSnapshot{time=t,seen=i>=40,abilityFx=new List<AbilityFx>()};
                    f.player.health=f.opponent.health=150;
                    f.player.position=spawn+Vector3.right*(side*3*Mathf.Min(t,4)+2*Mathf.Max(0,t-4));
                    if(i>=50){f.opponent.blind=1;f.abilityFx.Add(new AbilityFx{kind=FxKind.Flash,owner=0,done=true});}
                    if(i==60)f.opponent.health=0;
                    frames.Add(f);
                }
                return frames;
            }
            var read=new PlayerRead();
            read.Learn(Round(-1),spawn,2);
            if(read.Approach(2,out _,out _))return "acted on a single showing at need 2";
            read.Learn(Round(-1),spawn,2);
            if(!read.Approach(2,out Attempt a,out int n)||n!=2||!a.moving||Gap(a.lane,spawn+Vector3.left*10)>1||Gap(a.spot,spawn+Vector3.left*12)>1)return "west push not read after two showings";
            if(read.Flashed!=2)return "flash habit not counted";
            if(read.Dodge(2,out float share)!=1||share<.9f)return "right strafe not read";
            if(Mathf.Abs(read.Edge-.7f)>.01f)return "two wins should sharpen the bot by 0.7 rank";
            read.Learn(Round(1),spawn,2);
            if(!read.verdict.StartsWith("Read broken",StringComparison.Ordinal))return "a new lane should break the read";
            if(!read.Approach(2,out a,out n)||a.lane.x>0)return "one change of lane should not erase a habit";
            read.Learn(Round(1),spawn,2);read.Learn(Round(1),spawn,2);
            if(!read.Approach(2,out a,out n)||a.lane.x<0||n!=3)return "a new habit should replace the old one";
            read.ForgetApproach();
            if(read.Approach(1,out _,out _)||read.Flashed!=5)return "moving spawns should forget lanes but keep fighting habits";
            return null;
        }
    }
}
