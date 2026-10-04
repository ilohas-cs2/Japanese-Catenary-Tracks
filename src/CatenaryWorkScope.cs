using System;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Entities;
using Unity.Collections;
namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  readonly CatenaryWorkGate workGate=new CatenaryWorkGate();
  readonly HashSet<Entity> workTracks=new HashSet<Entity>();
  readonly HashSet<Entity> workEdges=new HashSet<Entity>();
  readonly HashSet<Entity> workOwners=new HashSet<Entity>();
  readonly HashSet<Entity> workObjects=new HashSet<Entity>();
  readonly HashSet<Entity> workLanes=new HashSet<Entity>();
  readonly HashSet<Entity> workVisited=new HashSet<Entity>();
  readonly List<Entity> workQueue=new List<Entity>();
  EntityQuery railwayQuery;
  bool railwayQueryReady;
  bool workFailed;
  long workPasses;
  int workPresence=-1;
  void ResetWorkScope(){workGate.Reset();workFailed=false;workPasses=0;workPresence=-1;workTracks.Clear();workEdges.Clear();workOwners.Clear();workObjects.Clear();workLanes.Clear();workVisited.Clear();workQueue.Clear();}
  bool LiveWorkEntity(Entity e){return EntityManager.Exists(e)&&!EntityManager.HasComponent<Game.Common.Deleted>(e)&&!EntityManager.HasComponent<Game.Tools.Temp>(e);}
  void AddWorkTrack(TrackPrefab t){Entity e;if(t!=null&&system.TryGetEntity(t,out e))workTracks.Add(e);}
  static void MixWork(ref ulong hash,int value){unchecked{hash=(hash^(uint)value)*1099511628211UL;}}
  void HashWorkEntity(Entity e,ref ulong hash){
   MixWork(ref hash,e.Index);MixWork(ref hash,e.Version);
   if(EntityManager.HasComponent<PrefabRef>(e)){var p=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;MixWork(ref hash,p.Index);MixWork(ref hash,p.Version);}
   if(EntityManager.HasComponent<Game.Net.Curve>(e)){var c=EntityManager.GetComponentData<Game.Net.Curve>(e);MixWork(ref hash,c.m_Bezier.a.GetHashCode());MixWork(ref hash,c.m_Bezier.b.GetHashCode());MixWork(ref hash,c.m_Bezier.c.GetHashCode());MixWork(ref hash,c.m_Bezier.d.GetHashCode());MixWork(ref hash,c.m_Length.GetHashCode());}
   if(EntityManager.HasComponent<Game.Net.Edge>(e)){var edge=EntityManager.GetComponentData<Game.Net.Edge>(e);MixWork(ref hash,edge.m_Start.Index);MixWork(ref hash,edge.m_Start.Version);MixWork(ref hash,edge.m_End.Index);MixWork(ref hash,edge.m_End.Version);}
   if(EntityManager.HasComponent<Game.Net.Node>(e))MixWork(ref hash,EntityManager.GetComponentData<Game.Net.Node>(e).m_Position.GetHashCode());
   if(EntityManager.HasComponent<Game.Net.EdgeGeometry>(e)){var g=EntityManager.GetComponentData<Game.Net.EdgeGeometry>(e);MixWork(ref hash,g.m_Start.middleLength.GetHashCode());MixWork(ref hash,g.m_End.middleLength.GetHashCode());}
   if(EntityManager.HasComponent<Game.Common.Owner>(e)){var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;MixWork(ref hash,owner.Index);MixWork(ref hash,owner.Version);}
   if(EntityManager.HasComponent<Game.Net.EdgeLane>(e))MixWork(ref hash,EntityManager.GetComponentData<Game.Net.EdgeLane>(e).m_EdgeDelta.GetHashCode());
   if(EntityManager.HasComponent<Game.Objects.Transform>(e)){var t=EntityManager.GetComponentData<Game.Objects.Transform>(e);MixWork(ref hash,t.m_Position.GetHashCode());MixWork(ref hash,t.m_Rotation.value.GetHashCode());}
  }
  void QueueWork(Entity e){if(LiveWorkEntity(e)&&workVisited.Add(e))workQueue.Add(e);}
  // Inspect railway edges, never all city objects. With no dedicated track,
  // return before following any object/lane buffers or running a correction.
  bool CaptureWorkScope(out ulong hash){
   hash=14695981039346656037UL;
   workEdges.Clear();workOwners.Clear();workObjects.Clear();workLanes.Clear();workVisited.Clear();workQueue.Clear();
   if(workTracks.Count==0){AddWorkTrack(track);AddWorkTrack(doubleTrack);AddWorkTrack(alternatingTrack);AddWorkTrack(alternatingSingleTrack);AddWorkTrack(alternatingOnewayTrack);foreach(var t in expansionTracks)AddWorkTrack(t);}
   if(!railwayQueryReady){railwayQuery=GetEntityQuery(ComponentType.ReadOnly<Game.Net.TrainTrack>(),ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());railwayQueryReady=true;}
   using(var rails=railwayQuery.ToEntityArray(Allocator.Temp))foreach(var e in rails){
    if(!workTracks.Contains(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab))continue;
    var edge=EntityManager.GetComponentData<Game.Net.Edge>(e);
    workEdges.Add(e);workOwners.Add(e);workOwners.Add(edge.m_Start);workOwners.Add(edge.m_End);
   }
   if(workEdges.Count==0)return false;
   // Include adjacent vanilla edges for junction degree and transition rules.
   foreach(var owner in workOwners){
    if(!LiveWorkEntity(owner))continue;
    if(EntityManager.HasBuffer<Game.Net.ConnectedEdge>(owner))foreach(var link in EntityManager.GetBuffer<Game.Net.ConnectedEdge>(owner))if(LiveWorkEntity(link.m_Edge))workEdges.Add(link.m_Edge);
    QueueWork(owner);
   }
   foreach(var edge in workEdges)QueueWork(edge);
   for(int i=0;i<workQueue.Count;i++){
    var e=workQueue[i];HashWorkEntity(e,ref hash);
    if(EntityManager.HasComponent<Game.Objects.Transform>(e)&&EntityManager.HasComponent<Game.Common.Owner>(e))workObjects.Add(e);
    if(EntityManager.HasComponent<Game.Net.Lane>(e)&&EntityManager.HasComponent<Game.Net.Curve>(e))workLanes.Add(e);
    if(EntityManager.HasBuffer<Game.Objects.SubObject>(e))foreach(var child in EntityManager.GetBuffer<Game.Objects.SubObject>(e))QueueWork(child.m_SubObject);
    if(EntityManager.HasBuffer<Game.Net.SubLane>(e))foreach(var child in EntityManager.GetBuffer<Game.Net.SubLane>(e))QueueWork(child.m_SubLane);
   }
   return true;
  }
  NativeArray<Entity> ScopedWorkEntities(EntityQuery query,HashSet<Entity> candidates){
   var selected=new List<Entity>();
   foreach(var e in candidates)if(LiveWorkEntity(e)&&query.MatchesIgnoreFilter(e))selected.Add(e);
   return new NativeArray<Entity>(selected.ToArray(),Allocator.Temp);
  }
  void RunCatenaryWork(){
   if(workFailed||Game.SceneFlow.GameManager.instance.isGameLoading||!linked||!expansionBuilt||expansionLinked.Count!=expansionTracks.Count)return;
   if(!workGate.PollDue(UnityEngine.Time.realtimeSinceStartupAsDouble))return;
   try{
    ulong hash;bool present=CaptureWorkScope(out hash);
    int presence=present?1:0;
    if(workPresence!=presence){workPresence=presence;Mod.Log.Info(present?"CATENARY_ACTIVE railway-scoped correction enabled.":"CATENARY_IDLE no dedicated tracks; object/lane scans and corrections skipped.");}
    if(!workGate.Observe(present,hash))return;
    var timer=System.Diagnostics.Stopwatch.StartNew();
    finalizingPortalWires=true;
    try{UpdateAlternatingPoles();UpdateMixedJoins();UpdatePortalWireAlignment();}
    finally{finalizingPortalWires=false;}
    ++workPasses;
    if(workPasses<=3||timer.ElapsedMilliseconds>=50)Mod.Log.Info("CATENARY_WORK pass="+workPasses+" edges="+workEdges.Count+" objects="+workObjects.Count+" lanes="+workLanes.Count+" elapsedMs="+timer.ElapsedMilliseconds);
   }catch(Exception ex){workFailed=true;Mod.Log.Error(ex,"Catenary scoped correction stopped; existing prefabs retained.");}
  }
 }
}
