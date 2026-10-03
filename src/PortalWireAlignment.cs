using System;
using System.Linq;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Mathematics;

namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  sealed class PortalPose {public Entity entity,owner;public AlternatingFamily family;public Game.Objects.Transform pose;}
  bool portalAlignmentFailed;
  string portalWireStatus="";
  bool finalizingPortalWires;
  public void FinalizePortalWires(){
   if(!expansionBuilt||expansionLinked.Count!=expansionTracks.Count)return;
   // MainLoop runs before native lane regeneration and Hidden-state changes.
   // Repeat reconciliation after those edits, before RequiredBatches consumes
   // the curves. This pass is idle when the already-correct geometry is stable.
   finalizingPortalWires=true;
   try{UpdateMixedJoins();UpdatePortalWireAlignment();}
   finally{finalizingPortalWires=false;}
  }
  void UpdatePortalWireAlignment(){
   if(!expansionBuilt||expansionLinked.Count!=expansionTracks.Count||portalAlignmentFailed)return;
   try {AlignPortalWires();}catch(Exception ex){portalAlignmentFailed=true;Mod.Log.Error(ex,"Portal wire alignment stopped; existing track prefabs retained.");}
  }
  void AlignPortalWires(){
   var families=new Dictionary<Entity,AlternatingFamily>();
   var portalTracks=new HashSet<Entity>(expansionFamilies.Where(f=>f.portal||f.commonWiring).SelectMany(f=>f.tracks).Select(t=>system.GetEntity(t)));
   var feederPrefabs=new HashSet<Entity>(expansionFeederChannels.Concat(commonUpperChannels).Where(f=>f!=null).Select(f=>system.GetEntity(f)));
   foreach(var f in expansionFamilies.Where(f=>f.portal||f.commonWiring)){families[system.GetEntity(f.a)]=f;families[system.GetEntity(f.b)]=f;}
   var poses=new List<PortalPose>();
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=q.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){
    AlternatingFamily f;if(!families.TryGetValue(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab,out f))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    for(int i=0;i<4&&!EntityManager.HasComponent<Game.Net.Edge>(owner)&&!EntityManager.HasComponent<Game.Net.Node>(owner)&&EntityManager.HasComponent<Game.Common.Owner>(owner);i++)owner=EntityManager.GetComponentData<Game.Common.Owner>(owner).m_Owner;
    poses.Add(new PortalPose{entity=e,owner=owner,family=f,pose=EntityManager.GetComponentData<Game.Objects.Transform>(e)});
   }
   Dictionary<Entity,List<PortalEdge>> routeNodes;Dictionary<Entity,int> routeDegree;
   var routes=PortalRoutes(out routeNodes,out routeDegree);
   int rotated=OrientPortalRows(poses,routes,routeNodes),rewired=0;
   var nodeAnchors=new Dictionary<int,List<PortalAnchor>>();
   foreach(var node in poses.Where(p=>EntityManager.HasComponent<Game.Net.Node>(p.owner))){
    // LaneSystem anchors endpoints before this MainLoop correction. Refresh
    // only lanes already referencing this portal; do not regenerate the node
    // (that would recreate its reversed native pose on every update).
    var prefab=EntityManager.GetComponentData<PrefabRef>(node.entity).m_Prefab;
    if(!EntityManager.HasBuffer<Game.Prefabs.SubLane>(prefab))continue;
    var anchors=new List<PortalAnchor>();
    foreach(var a in EntityManager.GetBuffer<Game.Prefabs.SubLane>(prefab)){
     if(a.m_NodeIndex.x!=a.m_NodeIndex.y)continue;
     anchors.Add(new PortalAnchor{pole=node.entity,prefab=a.m_Prefab,index=(ushort)a.m_NodeIndex.x,point=node.pose.m_Position+math.mul(node.pose.m_Rotation,a.m_Curve.a)});
    }
    nodeAnchors[node.entity.Index]=anchors;
   }
   // Collect the final world-space supports after all node rotations. Native
   // auxiliary lanes retain their EdgeDelta even when FindAnchor has moved
   // their endpoints elsewhere. Use that interval, not the displaced curve
   // or its old PathNodes, to identify the intended support row.
   var supportAnchors=new Dictionary<Entity,List<PortalAnchor>>();
   foreach(var p in poses){
    if(EntityManager.HasComponent<MixedJoinHidden>(p.entity)||EntityManager.HasComponent<Game.Tools.Hidden>(p.entity))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(p.entity).m_Prefab;
    if(!EntityManager.HasBuffer<Game.Prefabs.SubLane>(prefab))continue;
    var points=new List<PortalAnchor>();
    foreach(var a in EntityManager.GetBuffer<Game.Prefabs.SubLane>(prefab)){
     if(a.m_NodeIndex.x!=a.m_NodeIndex.y||!EntityManager.HasComponent<UtilityLaneData>(a.m_Prefab))continue;
     points.Add(new PortalAnchor{pole=p.entity,prefab=a.m_Prefab,index=(ushort)a.m_NodeIndex.x,point=p.pose.m_Position+math.mul(p.pose.m_Rotation,a.m_Curve.a)});
    }
    supportAnchors[p.entity]=points;
   }
   // Native node lanes can still reference the losing pole. Its Hidden flag
   // is transient (SubObjectHiddenSystem removes it for a visible owner), so
   // use our persistent decision marker when redirecting those references.
   foreach(var p in poses.Where(p=>EntityManager.HasComponent<MixedJoinHidden>(p.entity))){
    if(!nodeAnchors.ContainsKey(p.entity.Index))continue;
    var visible=poses.Where(v=>v.owner==p.owner&&supportAnchors.ContainsKey(v.entity)).ToArray();
    if(visible.Length==1)nodeAnchors[p.entity.Index]=supportAnchors[visible[0].entity];
    else nodeAnchors.Remove(p.entity.Index); // Never reconnect to a suppressed support.
   }
   // The seam pass owns entire split curves. Do not subsequently re-sag one
   // half in the endpoint pass; that caused repeated competing corrections.
   int seamPairs;HashSet<Entity> seamLanes;List<PortalSeamJoin> seamJoins;
   rewired+=JoinPortalWireSeams(poses,supportAnchors,routes,routeNodes,routeDegree,out seamPairs,out seamLanes,out seamJoins);
   var supportedWires=new HashSet<Entity>(supportAnchors.Values.SelectMany(a=>a).Select(a=>a.prefab));
   int feederLanes=0,railLanes=0,routed=0,unmatched=0,degenerate=0,hiddenLanes=0;
   var misses=new List<string>();
   var lanes=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadWrite<Game.Net.Curve>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=lanes.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){
    if(seamLanes.Contains(e))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    if(!EntityManager.HasComponent<Game.Net.Edge>(owner)&&!EntityManager.HasComponent<Game.Net.Node>(owner))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
    if(!EntityManager.HasComponent<UtilityLaneData>(prefab))continue;
    if(EntityManager.HasComponent<Game.Net.Node>(owner)&&supportedWires.Contains(prefab)&&EntityManager.HasComponent<MixedJoinHidden>(e))continue;
    var lane=EntityManager.GetComponentData<Game.Net.Lane>(e);var curve=EntityManager.GetComponentData<Game.Net.Curve>(e);
    var start=curve.m_Bezier.a;var end=curve.m_Bezier.d;
    bool changed;
    if(supportedWires.Contains(prefab)&&EntityManager.HasComponent<Game.Net.Edge>(owner)&&EntityManager.HasComponent<Game.Net.EdgeGeometry>(owner)&&EntityManager.HasComponent<Game.Net.EdgeLane>(e)&&EntityManager.HasComponent<PrefabRef>(owner)&&portalTracks.Contains(EntityManager.GetComponentData<PrefabRef>(owner).m_Prefab)){
     bool feeder=feederPrefabs.Contains(prefab);
     if(feeder)feederLanes++;else railLanes++;
     if(EntityManager.HasComponent<Game.Tools.Hidden>(e))hiddenLanes++;
     if(!math.all(math.isfinite(start))||!math.all(math.isfinite(end))||math.distance(start,end)<.01f)degenerate++;
     var edge=EntityManager.GetComponentData<Game.Net.Edge>(owner);
     var geometry=EntityManager.GetComponentData<Game.Net.EdgeGeometry>(owner);
     var delta=EntityManager.GetComponentData<Game.Net.EdgeLane>(e).m_EdgeDelta;
     var first=FindPortalSupport(owner,edge,geometry,delta.x,prefab,start,poses,supportAnchors);
     var last=FindPortalSupport(owner,edge,geometry,delta.y,prefab,end,poses,supportAnchors);
     // Correct each known endpoint independently. A missing support at the
     // opposite end must not prevent a mixed-join endpoint from being fixed.
     if((first==null&&last==null)||(first!=null&&last!=null&&first.pole==last.pole)){
      unmatched++;
      if(misses.Count<4)misses.Add("edge="+owner.Index+" delta="+delta+" start="+(first==null?"missing":first.pole.Index.ToString())+" end="+(last==null?"missing":last.pole.Index.ToString()));
      continue;
     }
     routed++;
     if(feeder){
      changed=SetPortalEndpoint(first,ref lane.m_StartNode,ref start);
      changed=SetPortalEndpoint(last,ref lane.m_EndNode,ref end)||changed;
     }else{
      // Preserve the native rail-wire connection graph at shared nodes.
      // Only move physical endpoints with a known visible support; an
      // unsupported junction endpoint retains its native continuous curve.
      changed=SetPortalPoint(first,ref start);
      changed=SetPortalPoint(last,ref end)||changed;
     }
    }else{
     changed=PortalAnchorRouting.Refresh(ref lane.m_StartNode,prefab,ref start,nodeAnchors);
     changed=PortalAnchorRouting.Refresh(ref lane.m_EndNode,prefab,ref end,nodeAnchors)||changed;
    }
    if(!changed)continue;
    curve.m_Bezier=PortalWireGeometry.Reanchor(curve.m_Bezier,start,end,EntityManager.GetComponentData<UtilityLaneData>(prefab).m_Hanging);
    curve.m_Length=MathUtils.Length(curve.m_Bezier);
    EntityManager.SetComponentData(e,lane);EntityManager.SetComponentData(e,curve);
    if(!EntityManager.HasComponent<Game.Common.Updated>(e))EntityManager.AddComponent<Game.Common.Updated>(e);
    MarkJoinRender(e);rewired++;
   }
   int redundantWires=SuppressRedundantPortalWires(poses,supportAnchors,routes,routeNodes,routeDegree,supportedWires,seamJoins);
   AuditMixedPortalFeeders(poses,supportAnchors,routes,routeNodes,routeDegree,feederPrefabs);
   string status="seamPairs="+seamPairs+" portalPoles="+poses.Count+" feederLanes="+feederLanes+" railLanes="+railLanes+" routed="+routed+" unmatched="+unmatched+" degenerate="+degenerate+" hidden="+hiddenLanes+" redundantNodeWires="+redundantWires;
   if(rotated>0||rewired>0||status!=portalWireStatus){
    portalWireStatus=status;Mod.Log.Info("PORTAL_WIRE_ALIGNMENT "+status+" rotated="+rotated+" reanchored="+rewired+" phase="+(finalizingPortalWires?"after-native-edits":"main-loop"));
    if(misses.Count>0)Mod.Log.Info("PORTAL_WIRE_UNMATCHED "+string.Join("; ",misses));
   }
  }
  static PortalAnchor FindPortalSupport(Entity owner,Game.Net.Edge edge,Game.Net.EdgeGeometry geometry,float delta,Entity feeder,float3 expectedAnchor,List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors){
   bool terminal=delta<=.00001f||delta>=.99999f;
   Entity supportOwner=terminal?(delta<=.00001f?edge.m_Start:edge.m_End):owner;
   var rows=poses.Where(p=>p.owner==supportOwner&&anchors.ContainsKey(p.entity)&&anchors[p.entity].Any(a=>a.prefab==feeder)).ToArray();
   // A node-owned support is already scoped to the exact junction. Width or
   // transition offsets must not reject it via the span's distance cutoff.
   int index=PortalWireGeometry.NearestSupport(PortalWireGeometry.EdgePoint(geometry,delta),rows.Select(p=>PortalWireGeometry.SupportStation(p.pose.m_Position,p.pose.m_Rotation,p.family.projectionAnchor)).ToArray(),terminal?float.MaxValue:3f);
   if(index<0)return null;
   var candidates=anchors[rows[index].entity].Where(a=>a.prefab==feeder).ToArray();
   int chosen=PortalWireGeometry.NearestSupport(expectedAnchor,candidates.Select(a=>a.point).ToArray(),float.MaxValue);
   return chosen<0?null:candidates[chosen];
  }
  static bool SetPortalPoint(PortalAnchor anchor,ref float3 point){
   if(anchor==null||math.distancesq(point,anchor.point)<=.000001f)return false;
   point=anchor.point;return true;
  }
  static bool SetPortalEndpoint(PortalAnchor anchor,ref Game.Pathfind.PathNode path,ref float3 point){
   if(anchor==null)return false;
   var target=new Game.Pathfind.PathNode(anchor.pole,anchor.index);
   bool changed=math.distancesq(point,anchor.point)>.000001f||!path.Equals(target);
   path=target;point=anchor.point;return changed;
  }
 }
 public sealed class CatenaryWireFinalizeSystem : Game.GameSystemBase {
  CatenarySystem catenary;
  protected override void OnCreate(){base.OnCreate();catenary=World.GetOrCreateSystemManaged<CatenarySystem>();}
  protected override void OnUpdate(){catenary.FinalizePortalWires();}
 }
}
