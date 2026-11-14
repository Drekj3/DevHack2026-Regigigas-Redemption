using System.Collections.Generic;
using UnityEngine;
namespace SecondChance
{
    // Static one-metre grid. Built once from courtyard colliders; no baking required.
    public class CourtyardNavigation
    {
        const int W=35,H=43;
        readonly bool[] open=new bool[W*H];
        public bool Built { get; private set; }
        static Vector3 Point(int i)=>new Vector3(i%W-17,.06f,i/W-21);
        static int Index(Vector3 p)=>Mathf.Clamp(Mathf.RoundToInt(p.z)+21,0,H-1)*W+Mathf.Clamp(Mathf.RoundToInt(p.x)+17,0,W-1);
        public void Build(Transform geometry)
        {
            Physics.SyncTransforms();
            for(int i=0;i<open.Length;i++)
            {
                open[i]=true;
                foreach(var c in Physics.OverlapBox(Point(i)+Vector3.up*.9f,new Vector3(.43f,.82f,.43f),Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
                    if(c.transform.IsChildOf(geometry)){open[i]=false;break;}
            }
            Built=true;
        }
        public bool IsWalkable(Vector3 p)=>open[Index(p)];
        public Vector3 Snap(Vector3 p)
        {
            int best=-1;float distance=float.MaxValue;
            for(int i=0;i<open.Length;i++)if(open[i]){float d=(Point(i)-p).sqrMagnitude;if(d<distance){distance=d;best=i;}}
            return best<0?p:Point(best);
        }
        public List<Vector3> Route(Vector3 from,Vector3 to)
        {
            int start=Index(Snap(from)),goal=Index(Snap(to));
            var prev=new int[open.Length];for(int i=0;i<prev.Length;i++)prev[i]=-1;
            var queue=new Queue<int>();queue.Enqueue(start);prev[start]=start;
            int closest=start;float closestD=(Point(start)-Point(goal)).sqrMagnitude;
            while(queue.Count>0)
            {
                int n=queue.Dequeue();float d=(Point(n)-Point(goal)).sqrMagnitude;
                if(d<closestD){closest=n;closestD=d;}if(n==goal){closest=n;break;}
                int x=n%W,z=n/W;
                foreach(int k in new[]{x>0?n-1:-1,x<W-1?n+1:-1,z>0?n-W:-1,z<H-1?n+W:-1})
                    if(k>=0&&open[k]&&prev[k]<0){prev[k]=n;queue.Enqueue(k);}
            }
            var path=new List<Vector3>();
            for(int n=closest;n!=start;n=prev[n])path.Add(Point(n));
            path.Reverse();return path;
        }
    }
}
