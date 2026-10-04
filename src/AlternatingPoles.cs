using System;
using System.Collections.Generic;
using System.Linq;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;
using Colossal.Mathematics;

namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  sealed class TrackEdge {
   public Entity entity,start,end;
   public Bezier4x3 curve;
   public float length;
   public bool reverse,paired;
   public readonly List<int> rows=new List<int>();
  }
  sealed class PoleSlot {public Entity entity;public float station;public bool oppositeSide;}
  sealed class PoleRow {public float3 tangent;public bool paired;public readonly List<PoleSlot> poles=new List<PoleSlot>();}
  struct Port {
   public TrackEdge edge;public bool atStart;
   public Port(TrackEdge edge,bool atStart){this.edge=edge;this.atStart=atStart;}
  }
  struct RowReference {
   public int row;public bool flip;
   public RowReference(int row,bool flip){this.row=row;this.flip=flip;}
  }
  sealed class AlternatingFamily {
   public string key,lastStatus="";
   public StaticObjectPrefab a,b;
   public TrackPrefab[] tracks;
   public TrackPrefab pairedTrack;
   public float3 projectionAnchor;
   public bool failed,portal,commonWiring;
  }
  AlternatingFamily originalFamily;
  readonly List<AlternatingFamily> expansionFamilies=new List<AlternatingFamily>();
  void UpdateAlternatingPoles(){
   if(alternatingTrack==null)return;
   if(originalFamily==null)originalFamily=new AlternatingFamily{key="Original",a=alternatingI,b=alternatingO,tracks=new[]{alternatingTrack,alternatingSingleTrack,alternatingOnewayTrack},pairedTrack=alternatingTrack,projectionAnchor=new float3(Reach,ContactLocal,0)};
   ReconcileFamily(originalFamily);
   if(expansionBuilt&&expansionLinked.Count==expansionTracks.Count)foreach(var family in expansionFamilies)ReconcileFamily(family);
  }
  void ReconcileFamily(AlternatingFamily family){
   if(family.failed)return;
   try{ReconcileAlternatingPoles(family);}catch(Exception ex){family.failed=true;Mod.Log.Error(ex,"Alternation stopped for "+family.key+"; network remains registered.");}
  }
  void ReconcileAlternatingPoles(AlternatingFamily family){
   var iPrefab=system.GetEntity(family.a);var oPrefab=system.GetEntity(family.b);
   var pairedPrefab=family.pairedTrack==null?Entity.Null:system.GetEntity(family.pairedTrack);
   var supportedPrefabs=new HashSet<Entity>(family.tracks.Select(t=>system.GetEntity(t)));
   if(!EntityManager.GetComponentData<ObjectData>(iPrefab).m_Archetype.Equals(EntityManager.GetComponentData<ObjectData>(oPrefab).m_Archetype))throw new InvalidOperationException("I/O pole archetypes differ.");
   var edgeQuery=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Edge>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   var edges=new Dictionary<Entity,TrackEdge>();var adjacency=new Dictionary<Entity,List<Port>>();
   using(var entities=ScopedWorkEntities(edgeQuery,workEdges)){
    foreach(var entity in entities){
     var prefab=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;if(!supportedPrefabs.Contains(prefab))continue;
     var net=EntityManager.GetComponentData<Game.Net.Edge>(entity);var curve=EntityManager.GetComponentData<Game.Net.Curve>(entity);
     var edge=new TrackEdge{entity=entity,start=net.m_Start,end=net.m_End,curve=curve.m_Bezier,length=curve.m_Length,paired=prefab==pairedPrefab,reverse=CompareNodePositions(net.m_Start,net.m_End)>0};
     edges.Add(entity,edge);
     foreach(var port in new[]{new Port(edge,true),new Port(edge,false)}){
      var node=port.atStart?edge.start:edge.end;List<Port> list;
      if(!adjacency.TryGetValue(node,out list)){list=new List<Port>();adjacency.Add(node,list);}list.Add(port);
     }
    }
   }
   var orderedEdges=edges.Values.OrderBy(e=>MathUtils.Position(e.curve,.5f).x).ThenBy(e=>MathUtils.Position(e.curve,.5f).z).ThenBy(e=>e.entity.Index).ToList();
   var orderedNodes=adjacency.Keys.ToList();orderedNodes.Sort(CompareNodePositions);
   var byOwner=new Dictionary<Entity,List<Entity>>();int poleCount=0,matched=0;
   var query=GetEntityQuery(ComponentType.ReadOnly<Game.Objects.Transform>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=ScopedWorkEntities(query,workObjects)){
    foreach(var entity in entities){
     var prefab=EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;if(prefab!=iPrefab&&prefab!=oPrefab)continue;poleCount++;
     var owner=EntityManager.GetComponentData<Game.Common.Owner>(entity).m_Owner;
     for(int depth=0;depth<4&&!edges.ContainsKey(owner)&&!adjacency.ContainsKey(owner)&&EntityManager.HasComponent<Game.Common.Owner>(owner);depth++)owner=EntityManager.GetComponentData<Game.Common.Owner>(owner).m_Owner;
     if(!edges.ContainsKey(owner)&&!adjacency.ContainsKey(owner))continue;
     List<Entity> list;if(!byOwner.TryGetValue(owner,out list)){list=new List<Entity>();byOwner.Add(owner,list);}list.Add(entity);matched++;
    }
   }
   var rows=new List<PoleRow>();var nodeRows=new Dictionary<Entity,int>();
   foreach(var edge in orderedEdges){
    List<Entity> entities;if(!byOwner.TryGetValue(edge.entity,out entities))continue;
    var slots=new List<PoleSlot>();
    foreach(var entity in entities){
     var pose=EntityManager.GetComponentData<Game.Objects.Transform>(entity);
     var anchor=pose.m_Position+math.mul(pose.m_Rotation,family.projectionAnchor);
     float t=ProjectToCurve(edge.curve,anchor);
     slots.Add(new PoleSlot{entity=entity,station=(edge.reverse?1-t:t)*edge.length,oppositeSide=edge.paired&&OppositeSide(pose.m_Position,MathUtils.Position(edge.curve,t),CanonicalTangent(edge,t))});
    }
    slots.Sort((a,b)=>a.station.CompareTo(b.station));float station=float.NegativeInfinity;PoleRow row=null;
    foreach(var slot in slots){
     if(row==null||slot.station-station>1f){
      station=slot.station;float t=edge.length>.001f?station/edge.length:0;if(edge.reverse)t=1-t;
      row=new PoleRow{paired=edge.paired,tangent=CanonicalTangent(edge,t)};edge.rows.Add(rows.Count);rows.Add(row);
     }
     row.poles.Add(slot);
    }
   }
   foreach(var node in orderedNodes){
    List<Entity> entities;if(!byOwner.TryGetValue(node,out entities))continue;
    // Junction poles use the reference frame of the longest adjoining track.
    var reference=adjacency[node].OrderByDescending(p=>p.edge.length).ThenBy(p=>p.edge.entity.Index).First();
    var row=new PoleRow{paired=adjacency[node].Any(p=>p.edge.paired),tangent=CanonicalTangent(reference.edge,reference.atStart?0:1)};
    var center=EntityManager.GetComponentData<Game.Net.Node>(node).m_Position;
    foreach(var entity in entities){var pose=EntityManager.GetComponentData<Game.Objects.Transform>(entity);row.poles.Add(new PoleSlot{entity=entity,oppositeSide=row.paired&&OppositeSide(pose.m_Position,center,row.tangent)});}
    nodeRows.Add(node,rows.Count);rows.Add(row);
   }
   // Span constraints come first: unavoidable parity conflicts stay at junctions.
   var links=new List<AlternatingGraph.Link>();
   foreach(var edge in orderedEdges)for(int i=1;i<edge.rows.Count;i++)links.Add(new AlternatingGraph.Link(edge.rows[i-1],edge.rows[i],true));
   var linkKeys=new HashSet<string>();
   foreach(var node in orderedNodes){
    var reference=adjacency[node].OrderByDescending(p=>p.edge.length).ThenBy(p=>p.edge.entity.Index).First();
    var tangent=CanonicalTangent(reference.edge,reference.atStart?0:1);
    var neighbors=new List<RowReference>();
    foreach(var port in adjacency[node]){
     bool portFlip=math.dot(tangent.xz,CanonicalTangent(port.edge,port.atStart?0:1).xz)<0;
     foreach(var found in FindNearestRows(port,adjacency,nodeRows,rows,new HashSet<Entity>()))neighbors.Add(new RowReference(found.row,rows[found.row].paired&&(found.flip^portFlip)));
    }
    int nodeRow;
    neighbors=neighbors.GroupBy(n=>n.row+":"+n.flip).Select(g=>g.First()).ToList();
    if(nodeRows.TryGetValue(node,out nodeRow)){
     foreach(var neighbor in neighbors)AddLink(links,linkKeys,nodeRow,neighbor.row,true^neighbor.flip);
    } else {
     // A pole-free node adds no imaginary row; cross empty short edges as well.
     for(int a=0;a<neighbors.Count;a++)for(int b=a+1;b<neighbors.Count;b++)AddLink(links,linkKeys,neighbors[a].row,neighbors[b].row,true^neighbors[a].flip^neighbors[b].flip);
    }
   }
   var result=AlternatingGraph.Solve(rows.Count,links);int changed=0,iCount=0,oCount=0;
   for(int r=0;r<rows.Count;r++)foreach(var slot in rows[r].poles){bool useO=result.colors[r]^slot.oppositeSide;if(useO)oCount++;else iCount++;if(SetPoleVariant(slot.entity,useO?oPrefab:iPrefab))changed++;}
   var samples=new List<string>();
   foreach(var edge in orderedEdges.Take(16)){
    string a=string.Join("-",edge.rows.Take(16).Select(r=>result.colors[r]?"O":"I"));
    string b=edge.paired?string.Join("-",edge.rows.Take(16).Select(r=>result.colors[r]?"I":"O")):"single";
    float usable=edge.length;
    if(EntityManager.HasComponent<Game.Net.EdgeGeometry>(edge.entity)){var geometry=EntityManager.GetComponentData<Game.Net.EdgeGeometry>(edge.entity);usable=geometry.m_Start.middleLength+geometry.m_End.middleLength;}
    samples.Add("length="+Math.Round(edge.length,1)+"m usable="+Math.Round(usable,1)+"m rows="+edge.rows.Count+" A:"+a+" B:"+b);
   }
   string status="edges="+edges.Count+" poles="+poleCount+" matched="+matched+" rows="+rows.Count+" I="+iCount+" O="+oCount+" junctions="+adjacency.Count(n=>n.Value.Count>2)+" seams="+result.seams.Count+" sequences="+string.Join(" / ",samples);
   if(status!=family.lastStatus||changed>0){family.lastStatus=status;Mod.Log.Info("IO_GRAPH family="+family.key+" "+status+" changed="+changed);}
  }
  static void AddLink(List<AlternatingGraph.Link> links,HashSet<string> keys,int a,int b,bool different){
   int low=Math.Min(a,b),high=Math.Max(a,b);string key=low+":"+high+":"+different;
   if(keys.Add(key))links.Add(new AlternatingGraph.Link(a,b,different));
  }
  static List<RowReference> FindNearestRows(Port port,Dictionary<Entity,List<Port>> adjacency,Dictionary<Entity,int> nodeRows,List<PoleRow> rows,HashSet<Entity> visited){
   var found=new List<RowReference>();var edge=port.edge;if(!visited.Add(edge.entity))return found;
   if(edge.rows.Count>0){bool first=port.atStart!=edge.reverse;found.Add(new RowReference(edge.rows[first?0:edge.rows.Count-1],false));return found;}
   var far=port.atStart?edge.end:edge.start;float farT=port.atStart?1:0;
   var tangent=CanonicalTangent(edge,farT);int nodeRow;
   if(nodeRows.TryGetValue(far,out nodeRow)){found.Add(new RowReference(nodeRow,math.dot(tangent.xz,rows[nodeRow].tangent.xz)<0));return found;}
   foreach(var next in adjacency[far]){
    if(visited.Contains(next.edge.entity))continue;
    bool flip=math.dot(tangent.xz,CanonicalTangent(next.edge,next.atStart?0:1).xz)<0;
    foreach(var item in FindNearestRows(next,adjacency,nodeRows,rows,new HashSet<Entity>(visited)))found.Add(new RowReference(item.row,item.flip^flip));
   }
   return found;
  }
  static bool OppositeSide(float3 position,float3 center,float3 tangent){var delta=position-center;return tangent.x*delta.z-tangent.z*delta.x>0;}
  static float3 CanonicalTangent(TrackEdge edge,float t){return CurveTangent(edge.curve,t)*(edge.reverse?-1f:1f);}
  bool SetPoleVariant(Entity entity,Entity target){
   var prefab=EntityManager.GetComponentData<PrefabRef>(entity);if(prefab.m_Prefab==target)return false;prefab.m_Prefab=target;EntityManager.SetComponentData(entity,prefab);
   if(!EntityManager.HasComponent<Game.Common.Updated>(entity))EntityManager.AddComponent<Game.Common.Updated>(entity);
   if(!EntityManager.HasComponent<Game.Common.BatchesUpdated>(entity))EntityManager.AddComponent<Game.Common.BatchesUpdated>(entity);return true;
  }
  int CompareNodePositions(Entity a,Entity b){
   var pa=EntityManager.GetComponentData<Game.Net.Node>(a).m_Position;var pb=EntityManager.GetComponentData<Game.Net.Node>(b).m_Position;
   int c=pa.x.CompareTo(pb.x);if(c==0)c=pa.z.CompareTo(pb.z);if(c==0)c=pa.y.CompareTo(pb.y);return c;
  }
  static float3 CurveTangent(Bezier4x3 curve,float t){
   float u=1-t;var tangent=3*u*u*(curve.b-curve.a)+6*u*t*(curve.c-curve.b)+3*t*t*(curve.d-curve.c);
   if(math.lengthsq(tangent.xz)<.000001f)tangent=curve.d-curve.a;return tangent;
  }
  static float ProjectToCurve(Bezier4x3 curve,float3 point){
   float best=0,bestDistance=float.MaxValue;
   for(int i=0;i<=64;i++){float t=i/64f;float d=math.lengthsq((MathUtils.Position(curve,t)-point).xz);if(d<bestDistance){bestDistance=d;best=t;}}
   float low=math.max(0,best-1/64f),high=math.min(1,best+1/64f);
   for(int i=0;i<16;i++){float a=math.lerp(low,high,1/3f),b=math.lerp(low,high,2/3f);if(math.lengthsq((MathUtils.Position(curve,a)-point).xz)<math.lengthsq((MathUtils.Position(curve,b)-point).xz))high=b;else low=a;}
   return (low+high)*.5f;
  }
 }
}
