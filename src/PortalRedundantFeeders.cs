using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Game.Prefabs;
namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  sealed class FeederCap {public Entity lane,edge,node,prefab;public float3 point;}
  string redundantWireStatus="";
  int SuppressRedundantPortalWires(List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors,Dictionary<Entity,PortalEdge> routes,Dictionary<Entity,List<PortalEdge>> nodes,Dictionary<Entity,int> degree,HashSet<Entity> supported,List<PortalSeamJoin> seamJoins){
   var caps=new List<FeederCap>();var nodeLanes=new List<Entity>();
   var query=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=ScopedWorkEntities(query,workLanes))foreach(var e in entities){
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
    if(!supported.Contains(prefab)&&!EntityManager.HasComponent<MixedJoinHidden>(e)&&!EntityManager.HasComponent<MixedJoinOverride>(e))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    if(EntityManager.HasComponent<Game.Net.Node>(owner)){nodeLanes.Add(e);continue;}
    PortalEdge edge;if(!routes.TryGetValue(owner,out edge)||!EntityManager.HasComponent<Game.Net.EdgeLane>(e))continue;
    if(EntityManager.HasComponent<Game.Tools.Hidden>(e)||EntityManager.HasComponent<Game.Common.Overridden>(e))continue;
    var curve=EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
    if(!math.all(math.isfinite(curve.a))||!math.all(math.isfinite(curve.d))||math.distancesq(curve.a,curve.d)<.0001f)continue;
    var delta=EntityManager.GetComponentData<Game.Net.EdgeLane>(e).m_EdgeDelta;
    if(delta.x<=.00001f)caps.Add(new FeederCap{lane=e,edge=owner,node=edge.edge.m_Start,prefab=prefab,point=curve.a});
    if(delta.y>=.99999f)caps.Add(new FeederCap{lane=e,edge=owner,node=edge.edge.m_End,prefab=prefab,point=curve.d});
   }
   int count=0;var summary=new Dictionary<string,int>();
   foreach(var lane in nodeLanes){
    var node=EntityManager.GetComponentData<Game.Common.Owner>(lane).m_Owner;
    var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
    List<PortalEdge> pair;bool redundant=false;
    if(nodes.TryGetValue(node,out pair)&&degree[node]==2&&pair.Count==2){
     var rows=poses.Where(p=>p.owner==node&&anchors.ContainsKey(p.entity)).ToArray();
     if(rows.Length==1&&pair[0].family!=pair[1].family){
      var points=anchors[rows[0].entity].Where(a=>a.prefab==prefab).ToArray();
      if(points.Length>0){
       var first=caps.Where(c=>c.node==node&&c.edge==pair[0].entity&&c.prefab==prefab).Select(c=>c.point).ToArray();
       var second=caps.Where(c=>c.node==node&&c.edge==pair[1].entity&&c.prefab==prefab).Select(c=>c.point).ToArray();
       redundant=PortalFeederCoverage.Complete(points.Select(p=>p.point).ToArray(),first,second);
      }
     }
     else if(rows.Length==0){
      // Only a seam actually rebuilt in this pass can replace its native
      // connector. Scope proof by node AND channel: a lane managed at its
      // other endpoint is not evidence of continuity here.
      var proof=seamJoins.Where(s=>s.node==node&&s.prefab==prefab).ToArray();
      var first=caps.Where(c=>c.node==node&&c.edge==pair[0].entity&&c.prefab==prefab).ToArray();
      var second=caps.Where(c=>c.node==node&&c.edge==pair[1].entity&&c.prefab==prefab).ToArray();
      redundant=first.All(c=>proof.Any(s=>s.first==c.lane))&&second.All(c=>proof.Any(s=>s.second==c.lane))
       &&PortalFeederCoverage.Complete(proof.Select(s=>s.point).ToArray(),first.Select(c=>c.point).ToArray(),second.Select(c=>c.point).ToArray());
     }
    }
    // Keep native lane identities/topology intact. Restore our suppression
    // if edits remove coverage or replace the simple two-edge join.
    MixedJoinVisibility.Apply(EntityManager,lane,redundant);
    if(redundant){count++;string key="node"+node.Index+"/prefab"+prefab.Index;if(!summary.ContainsKey(key))summary[key]=0;summary[key]++;}
   }
   var status=string.Join(",",summary.OrderBy(p=>p.Key).Select(p=>p.Key+"="+p.Value));
   if(status!=redundantWireStatus){redundantWireStatus=status;Mod.Log.Info("PORTAL_REDUNDANT_WIRES total="+count+" groups="+status);}
   return count;
  }
 }
}
