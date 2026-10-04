using System;
using System.Linq;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Mathematics;
namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  sealed class PortalEdge {public Entity entity;public Game.Net.Edge edge;public Bezier4x3 curve;public AlternatingFamily family;public bool reverse;}
  sealed class PortalEnd {public Entity lane,node;public PortalEdge edge;public Entity prefab;public bool atStart;public PortalAnchor anchor;}
  sealed class PortalSeamJoin {public Entity node,prefab,first,second;public float3 point;}
  Dictionary<Entity,PortalEdge> PortalRoutes(out Dictionary<Entity,List<PortalEdge>> nodes,out Dictionary<Entity,int> degree){
   var tracks=new Dictionary<Entity,AlternatingFamily>();
   foreach(var f in expansionFamilies.Where(f=>f.portal||f.commonWiring))foreach(var t in f.tracks)tracks[system.GetEntity(t)]=f;
   var routes=new Dictionary<Entity,PortalEdge>();nodes=new Dictionary<Entity,List<PortalEdge>>();degree=new Dictionary<Entity,int>();
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=ScopedWorkEntities(q,workEdges))foreach(var e in entities){
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
    // Count railway continuations separately from crossing road arms. The
    // pole-placement and mixed-join policies still see the full junction,
    // so a road crossing does not gain a new pole in the carriageway.
    if(!CatenaryJunctionPolicy.CountsForWireDegree(tracks.ContainsKey(prefab),EntityManager.HasComponent<Game.Net.TrainTrack>(e),EntityManager.HasComponent<Game.Net.Road>(e)))continue;
    var net=EntityManager.GetComponentData<Game.Net.Edge>(e);
    foreach(var n in new[]{net.m_Start,net.m_End}){if(!degree.ContainsKey(n))degree[n]=0;degree[n]++;}
    AlternatingFamily f;if(!tracks.TryGetValue(prefab,out f))continue;
    var edge=new PortalEdge{entity=e,edge=net,curve=EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier,family=f};routes.Add(e,edge);
    foreach(var n in new[]{net.m_Start,net.m_End}){if(!nodes.ContainsKey(n))nodes[n]=new List<PortalEdge>();nodes[n].Add(edge);}
   }
   var visited=new HashSet<Entity>();
   foreach(var seed in routes.Values.OrderBy(e=>math.min(e.curve.a.x,e.curve.d.x)).ThenBy(e=>math.min(e.curve.a.z,e.curve.d.z)).ThenBy(e=>e.entity.Index)){
    if(!visited.Add(seed.entity))continue;
    seed.reverse=CompareNodePositions(seed.edge.m_Start,seed.edge.m_End)>0;
    var pending=new Queue<PortalEdge>();pending.Enqueue(seed);
    while(pending.Count>0){var e=pending.Dequeue();foreach(var n in new[]{e.edge.m_Start,e.edge.m_End}){
     if(degree[n]!=2||nodes[n].Count!=2)continue;
     foreach(var next in nodes[n]){
      if((next.family!=e.family&&!(next.family.commonWiring&&e.family.commonWiring&&next.family.portal==e.family.portal))||!visited.Add(next.entity))continue;
      next.reverse=PortalWireGeometry.NextReverse(e.reverse,e.edge.m_Start==n,next.edge.m_Start==n);pending.Enqueue(next);
     }
    }}
   }
   return routes;
  }
  int OrientPortalRows(List<PortalPose> poses,Dictionary<Entity,PortalEdge> routes,Dictionary<Entity,List<PortalEdge>> nodes){
   int changed=0;
   foreach(var p in poses){
    if(!p.family.portal)continue; // Preserve native single-track travel-side orientation.
    PortalEdge edge;float t;
    if(routes.TryGetValue(p.owner,out edge))t=ProjectToCurve(edge.curve,p.pose.m_Position);
    else{
     List<PortalEdge> adjacent;if(!nodes.TryGetValue(p.owner,out adjacent))continue;
     edge=adjacent.Where(e=>e.family==p.family).OrderByDescending(e=>math.distance(e.curve.a,e.curve.d)).ThenBy(e=>e.entity.Index).FirstOrDefault();
     if(edge==null)continue;t=edge.edge.m_Start==p.owner?0:1;
    }
    var tangent=CurveTangent(edge.curve,t)*(edge.reverse?-1:1);tangent.y=0;
    if(math.lengthsq(tangent)<.000001f)continue;
    var rotation=quaternion.LookRotationSafe(tangent,new float3(0,1,0));
    if(math.abs(math.dot(p.pose.m_Rotation.value,rotation.value))>.999999f)continue;
    var pose=p.pose;pose.m_Rotation=rotation;p.pose=pose;EntityManager.SetComponentData(p.entity,pose);MarkJoinRender(p.entity);changed++;
   }
   return changed;
  }
  // A degree-two seam without a pole is not a support. Build one hanging
  // curve between real supports and split it at the shared node. Each
  // half keeps its native lane identity and directed ordering.
  int JoinPortalWireSeams(List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors,Dictionary<Entity,PortalEdge> routes,Dictionary<Entity,List<PortalEdge>> nodes,Dictionary<Entity,int> degree,out int pairs,out HashSet<Entity> managed,out List<PortalSeamJoin> joins){
   pairs=0;managed=new HashSet<Entity>();joins=new List<PortalSeamJoin>();int changed=0;var ends=new Dictionary<Entity,List<PortalEnd>>();
   var supported=new HashSet<Entity>(anchors.Values.SelectMany(a=>a).Select(a=>a.prefab));
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Net.EdgeLane>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>(),ComponentType.Exclude<Game.Tools.Hidden>());
   using(var entities=ScopedWorkEntities(q,workLanes))foreach(var e in entities){
    PortalEdge edge;if(!routes.TryGetValue(EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner,out edge))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;if(!supported.Contains(prefab)||!EntityManager.HasComponent<Game.Net.EdgeGeometry>(edge.entity))continue;
    var delta=EntityManager.GetComponentData<Game.Net.EdgeLane>(e).m_EdgeDelta;
    var curve=EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
    foreach(bool atStart in new[]{true,false}){
     if(atStart?delta.x>.00001f:delta.y<.99999f)continue;
     var node=atStart?edge.edge.m_Start:edge.edge.m_End;
     if(degree[node]!=2||nodes[node].Count!=2)continue;
     if(nodes[node][0].family!=nodes[node][1].family&&!(nodes[node][0].family.commonWiring&&nodes[node][1].family.commonWiring&&nodes[node][0].family.portal==nodes[node][1].family.portal))continue;
     if(poses.Any(p=>p.owner==node&&anchors.ContainsKey(p.entity)))continue;
     var anchor=FindPortalSupport(edge.entity,edge.edge,EntityManager.GetComponentData<Game.Net.EdgeGeometry>(edge.entity),atStart?delta.y:delta.x,prefab,atStart?curve.d:curve.a,poses,anchors);
     if(anchor==null)continue;
     if(!ends.ContainsKey(node))ends[node]=new List<PortalEnd>();
     ends[node].Add(new PortalEnd{lane=e,node=node,edge=edge,prefab=prefab,atStart=atStart,anchor=anchor});
    }
   }
   foreach(var entry in ends)foreach(var channel in entry.Value.GroupBy(e=>e.prefab)){
    var adjacent=nodes[entry.Key];
    var first=channel.Where(e=>e.edge==adjacent[0]).OrderBy(e=>e.lane.Index).ToList();
    var second=channel.Where(e=>e.edge==adjacent[1]).ToList();
    if(first.Count!=second.Count||first.Count==0)continue;
    foreach(var a in first){
     var b=second.OrderBy(e=>math.distancesq(e.anchor.point,a.anchor.point)).ThenBy(e=>e.lane.Index).First();second.Remove(b);
     var nodePoint=EntityManager.GetComponentData<Game.Net.Node>(entry.Key).m_Position;
     float hanging=EntityManager.GetComponentData<UtilityLaneData>(a.prefab).m_Hanging;
     Bezier4x3 left,right;
     if(!PortalWireGeometry.JoinAtNode(a.anchor.point,b.anchor.point,nodePoint,hanging,out left,out right))continue;
     // Left is support A -> seam; right is seam -> support B.
     if(a.atStart)left=MathUtils.Invert(left);
     if(!b.atStart)right=MathUtils.Invert(right);
     if(ApplyPortalWireCurve(a.lane,left))changed++;
     if(ApplyPortalWireCurve(b.lane,right))changed++;
     managed.Add(a.lane);managed.Add(b.lane);
     joins.Add(new PortalSeamJoin{node=entry.Key,prefab=a.prefab,first=a.lane,second=b.lane,point=a.atStart?left.a:left.d});
     pairs++;
    }
   }
   return changed;
  }
  bool ApplyPortalWireCurve(Entity lane,Bezier4x3 value){
   var curve=EntityManager.GetComponentData<Game.Net.Curve>(lane);
   if(PortalWireGeometry.SameCurve(curve.m_Bezier,value))return false;
   curve.m_Bezier=value;curve.m_Length=MathUtils.Length(value);EntityManager.SetComponentData(lane,curve);
   if(!EntityManager.HasComponent<Game.Common.Updated>(lane))EntityManager.AddComponent<Game.Common.Updated>(lane);
   MarkJoinRender(lane);return true;
  }
 }
}
