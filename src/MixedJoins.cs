using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Game.Prefabs;
using Colossal.Serialization.Entities;

namespace JPCatenaryPrototype {
 public struct MixedJoinOrder : IComponentData,ISerializable {
  public Entity prefab;
  public long order;
  public void Serialize<TWriter>(TWriter writer) where TWriter:IWriter {writer.Write(prefab);writer.Write(order);}
  public void Deserialize<TReader>(TReader reader) where TReader:IReader {reader.Read(out prefab);reader.Read(out order);}
 }
 public struct MixedJoinChoice : IComponentData,ISerializable {
  public Entity prefab;
  public void Serialize<TWriter>(TWriter writer) where TWriter:IWriter {writer.Write(prefab);}
  public void Deserialize<TReader>(TReader reader) where TReader:IReader {reader.Read(out prefab);}
 }
 // Runtime-only logical suppression. Rendering uses an owned Overridden
 // flag; Game.Tools.Hidden is temporary editor state, not stable hiding.
 public struct MixedJoinHidden : IComponentData {}
 public partial class CatenarySystem {
  sealed class JoinEdge {public Entity entity,prefab;public Game.Net.Edge net;public AlternatingFamily family;public long order;}
  bool mixedReady,mixedFailed;
  long mixedOrder;
  string mixedStatus="";
  void UpdateMixedJoins(){
   if(!expansionBuilt||expansionLinked.Count!=expansionTracks.Count||mixedFailed)return;
   try {ReconcileMixedJoins();}catch(Exception ex){mixedFailed=true;Mod.Log.Error(ex,"Mixed-join reconciliation stopped; original network prefabs were not modified.");}
  }
  void ReconcileMixedJoins(){
   var tracks=new Dictionary<Entity,AlternatingFamily>();var poles=new Dictionary<Entity,AlternatingFamily>();
   foreach(var f in expansionFamilies){foreach(var t in f.tracks)tracks[system.GetEntity(t)]=f;poles[system.GetEntity(f.a)]=f;poles[system.GetEntity(f.b)]=f;}
   var query=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   var edges=new List<JoinEdge>();var adjacency=new Dictionary<Entity,List<JoinEdge>>();
   // Count every connected edge, including stock tracks. Reconciliation is
   // deliberately limited to two expansion tracks, never branch junctions.
   var degree=new Dictionary<Entity,int>();
   using(var entities=ScopedWorkEntities(query,workEdges))foreach(var e in entities){
    var net=EntityManager.GetComponentData<Game.Net.Edge>(e);
    foreach(var n in new[]{net.m_Start,net.m_End}){if(!degree.ContainsKey(n))degree[n]=0;degree[n]++;}
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;AlternatingFamily family;
    if(!tracks.TryGetValue(prefab,out family))continue;
    var item=new JoinEdge{entity=e,prefab=prefab,net=net,family=family};
    if(EntityManager.HasComponent<MixedJoinOrder>(e)){
     var stamp=EntityManager.GetComponentData<MixedJoinOrder>(e);mixedOrder=Math.Max(mixedOrder,stamp.order);
     if(stamp.prefab==prefab)item.order=stamp.order;
    }
    edges.Add(item);
    foreach(var n in new[]{net.m_Start,net.m_End}){List<JoinEdge> list;if(!adjacency.TryGetValue(n,out list)){list=new List<JoinEdge>();adjacency.Add(n,list);}list.Add(item);}
   }
   // One order per observed edit batch. On the first loaded scene unknown
   // chronology is a tie; saved node choices remain authoritative on ties.
   long batch=mixedReady?mixedOrder+1:Math.Max(1,mixedOrder);
   foreach(var e in edges.Where(e=>e.order==0)){
    e.order=batch;var stamp=new MixedJoinOrder{prefab=e.prefab,order=batch};
    if(EntityManager.HasComponent<MixedJoinOrder>(e.entity))EntityManager.SetComponentData(e.entity,stamp);else EntityManager.AddComponentData(e.entity,stamp);
    mixedOrder=Math.Max(mixedOrder,batch);
   }
   mixedReady=true;
   var winners=new Dictionary<Entity,AlternatingFamily>();int ties=0;
   foreach(var entry in adjacency){
    var node=entry.Key;var pair=entry.Value;
    if(degree[node]!=2||pair.Count!=2||pair[0].family==pair[1].family)continue;
    string previous=null;
    if(EntityManager.HasComponent<MixedJoinChoice>(node)){
     var saved=EntityManager.GetComponentData<MixedJoinChoice>(node);AlternatingFamily f;if(tracks.TryGetValue(saved.prefab,out f))previous=f.key;
    }
    string key=MixedJoinPolicy.Select(pair[0].family.key,pair[0].order,pair[1].family.key,pair[1].order,previous);
    var winner=pair.First(e=>e.family.key==key);winners.Add(node,winner.family);
    if(pair[0].order==pair[1].order&&previous==null)ties++;
    var choice=new MixedJoinChoice{prefab=winner.prefab};
    if(EntityManager.HasComponent<MixedJoinChoice>(node))EntityManager.SetComponentData(node,choice);else EntityManager.AddComponentData(node,choice);
   }
   var objects=GetEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   int hidden=0,visible=0,changes=0;
   using(var entities=ScopedWorkEntities(objects,workObjects))foreach(var e in entities){
    AlternatingFamily family;if(!poles.TryGetValue(EntityManager.GetComponentData<PrefabRef>(e).m_Prefab,out family))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    for(int depth=0;depth<4&&!adjacency.ContainsKey(owner)&&!EntityManager.HasComponent<Game.Net.Edge>(owner)&&EntityManager.HasComponent<Game.Common.Owner>(owner);depth++)owner=EntityManager.GetComponentData<Game.Common.Owner>(owner).m_Owner;
    AlternatingFamily selected;bool atJoin=winners.TryGetValue(owner,out selected);
    // Re-enabling native Intersection supports is only for mixed joins.
    // Other connected nodes keep the previous pole-free junction policy.
    bool hide=atJoin?family!=selected:adjacency.ContainsKey(owner)&&degree[owner]>=2;
    if(atJoin){if(hide)hidden++;else visible++;}
    if(MixedJoinVisibility.Apply(EntityManager,e,hide))changes++;
   }
   string status="joins="+winners.Count+" visiblePoles="+visible+" hiddenAnchorPoles="+hidden+" chronologyFallbacks="+ties+" choices="+string.Join(",",winners.Select(w=>w.Key.Index+":"+w.Value.key));
   if(status!=mixedStatus||changes>0){mixedStatus=status;Mod.Log.Info("MIXED_JOIN "+status+" changes="+changes);}
  }
  void MarkJoinRender(Entity entity){if(!EntityManager.HasComponent<Game.Common.BatchesUpdated>(entity))EntityManager.AddComponent<Game.Common.BatchesUpdated>(entity);}
 }
}
