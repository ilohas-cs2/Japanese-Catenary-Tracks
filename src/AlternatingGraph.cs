using System;
using System.Collections.Generic;

namespace JPCatenaryPrototype {
 // XOR constraints between actual pole rows. Internal span constraints are supplied
 // first, so unavoidable odd-cycle conflicts stay at junctions, not within spans.
 public static class AlternatingGraph {
  public struct Link {
   public int a,b;
   public bool different;
   public Link(int a,int b,bool different){this.a=a;this.b=b;this.different=different;}
  }
  public sealed class Result {
   public bool[] colors;
   public readonly List<Link> seams=new List<Link>();
  }
  sealed class ParitySet {
   readonly int[] parent,rank;
   readonly bool[] parity;
   public ParitySet(int count){parent=new int[count];rank=new int[count];parity=new bool[count];for(int i=0;i<count;i++)parent[i]=i;}
   public int Root(int x){if(parent[x]!=x){int old=parent[x];parent[x]=Root(old);parity[x]^=parity[old];}return parent[x];}
   public bool Phase(int x){Root(x);return parity[x];}
   public bool Join(Link link){
    int a=Root(link.a),b=Root(link.b);bool delta=Phase(link.a)^Phase(link.b)^link.different;
    if(a==b)return !delta;
    if(rank[a]<rank[b]){int t=a;a=b;b=t;}
    parent[b]=a;parity[b]=delta;if(rank[a]==rank[b])rank[a]++;return true;
   }
  }
  public static Result Solve(int count,IEnumerable<Link> links){
   var set=new ParitySet(count);var result=new Result{colors=new bool[count]};
   foreach(var link in links){
    if(link.a<0||link.a>=count||link.b<0||link.b>=count)throw new ArgumentOutOfRangeException("links");
    if(!set.Join(link))result.seams.Add(link);
   }
   var rootPhase=new Dictionary<int,bool>();
   for(int i=0;i<count;i++){
    int root=set.Root(i);bool phase=set.Phase(i),initial;
    if(!rootPhase.TryGetValue(root,out initial)){initial=phase;rootPhase.Add(root,initial);}
    result.colors[i]=phase^initial;
   }
   return result;
  }
 }
}
