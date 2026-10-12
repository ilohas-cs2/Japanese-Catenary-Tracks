using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Game.Prefabs;
using Colossal.Serialization.Entities;

namespace JPCatenaryPrototype {
 public struct AuxiliaryTerminated : IComponentData,IEmptySerializable {}
 public partial class CatenarySystem {
  void TerminateAuxiliaryWires(List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors,Dictionary<Entity,PortalEdge> routes,Dictionary<Entity,List<PortalEdge>> nodes,HashSet<Entity> feeders){
   // Include the original five tracks for termination only. Their previous
   // alignment/alternation rules are deliberately left unchanged.
   routes=new Dictionary<Entity,PortalEdge>(routes);
   nodes=nodes.ToDictionary(n=>n.Key,n=>new List<PortalEdge>(n.Value));
   poses=new List<PortalPose>(poses);anchors=new Dictionary<Entity,List<PortalAnchor>>(anchors);
   feeders=new HashSet<Entity>(feeders);feeders.Add(system.GetEntity(feederLane));
   var legacy=new AlternatingFamily{key="LegacyEquipped",projectionAnchor=new Unity.Mathematics.float3(Reach,ContactLocal,0)};
   var legacyTracks=new HashSet<Entity>(new[]{track,doubleTrack,alternatingTrack,alternatingSingleTrack,alternatingOnewayTrack}.Select(t=>system.GetEntity(t)));
   var legacyPoles=new HashSet<Entity>(new[]{pole,alternatingI,alternatingO}.Select(p=>system.GetEntity(p)));
   foreach(var e in workEdges){
    if(!LiveWorkEntity(e)||!EntityManager.HasComponent<PrefabRef>(e)||!EntityManager.HasComponent<Game.Net.Edge>(e)||!legacyTracks.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab))continue;
    var net=EntityManager.GetComponentData<Game.Net.Edge>(e);
    var r=new PortalEdge{entity=e,edge=net,family=legacy};routes[e]=r;
    foreach(var node in new[]{net.m_Start,net.m_End}){if(!nodes.ContainsKey(node))nodes[node]=new List<PortalEdge>();nodes[node].Add(r);}
   }
   foreach(var e in workObjects){
    if(!LiveWorkEntity(e)||!EntityManager.HasComponent<PrefabRef>(e)||!EntityManager.HasComponent<Game.Objects.Transform>(e)||!EntityManager.HasComponent<Game.Common.Owner>(e)||EntityManager.HasComponent<MixedJoinHidden>(e)||EntityManager.HasComponent<Game.Tools.Hidden>(e))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;if(!legacyPoles.Contains(prefab)||!EntityManager.HasBuffer<Game.Prefabs.SubLane>(prefab))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    for(int i=0;i<4&&!EntityManager.HasComponent<Game.Net.Edge>(owner)&&!EntityManager.HasComponent<Game.Net.Node>(owner)&&EntityManager.HasComponent<Game.Common.Owner>(owner);i++)owner=EntityManager.GetComponentData<Game.Common.Owner>(owner).m_Owner;
    var pose=EntityManager.GetComponentData<Game.Objects.Transform>(e);poses.Add(new PortalPose{entity=e,owner=owner,family=legacy,pose=pose});
    var points=new List<PortalAnchor>();
    foreach(var a in EntityManager.GetBuffer<Game.Prefabs.SubLane>(prefab))if(a.m_NodeIndex.x==a.m_NodeIndex.y)points.Add(new PortalAnchor{pole=e,prefab=a.m_Prefab,index=(ushort)a.m_NodeIndex.x,point=pose.m_Position+Unity.Mathematics.math.mul(pose.m_Rotation,a.m_Curve.a)});
    anchors[e]=points;
   }
   var boundaries=new HashSet<Entity>(nodes.Where(n=>AuxiliaryTerminationPolicy.TerminalBoundary(n.Value.Count(e=>!e.family.noAuxiliary),n.Value.Count(e=>e.family.noAuxiliary))).Select(n=>n.Key));
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   int suppressed=0;
   using(var entities=ScopedWorkEntities(q,workLanes))foreach(var lane in entities){
    var prefab=EntityManager.GetComponentData<PrefabRef>(lane).m_Prefab;
    if(!feeders.Contains(prefab))continue; // Never touch train contact/messenger wires.
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(lane).m_Owner;
    bool hide=boundaries.Contains(owner);
    PortalEdge edge;
    if(routes.TryGetValue(owner,out edge)&&EntityManager.HasComponent<Game.Net.EdgeLane>(lane)){
     var delta=EntityManager.GetComponentData<Game.Net.EdgeLane>(lane).m_EdgeDelta;
     bool start=delta.x<=.00001f&&boundaries.Contains(edge.edge.m_Start);
     bool end=delta.y>=.99999f&&boundaries.Contains(edge.edge.m_End);
     bool first=false,last=false;
     if(EntityManager.HasComponent<Game.Net.EdgeGeometry>(owner)){
      var geometry=EntityManager.GetComponentData<Game.Net.EdgeGeometry>(owner);
      var curve=EntityManager.GetComponentData<Game.Net.Curve>(lane).m_Bezier;
      first=FindPortalSupport(owner,edge.edge,geometry,delta.x,prefab,curve.a,poses,anchors)!=null;
      last=FindPortalSupport(owner,edge.edge,geometry,delta.y,prefab,curve.d,poses,anchors)!=null;
     }
     hide=AuxiliaryTerminationPolicy.SuppressSpan(true,edge.family.noAuxiliary,start,end,first,last);
    }
    bool owned=EntityManager.HasComponent<AuxiliaryTerminated>(lane);
    if(hide){
     if(!owned)EntityManager.AddComponent<AuxiliaryTerminated>(lane);
     MixedJoinVisibility.Apply(EntityManager,lane,true);suppressed++;
    }else if(owned){
     EntityManager.RemoveComponent<AuxiliaryTerminated>(lane);
     // Edge caps have no other suppressor. Node lanes are reconciled by
     // SuppressRedundantPortalWires earlier in this same pass.
     if(!EntityManager.HasComponent<Game.Net.Node>(owner))MixedJoinVisibility.Apply(EntityManager,lane,false);
    }
   }
   string status=boundaries.Count+"/"+suppressed;
   if(status!=auxiliaryTerminationStatus){auxiliaryTerminationStatus=status;Mod.Log.Info("AUXILIARY_TERMINALS nodes="+boundaries.Count+" hiddenSpans="+suppressed);}
  }
  string auxiliaryTerminationStatus="";
 }
}
