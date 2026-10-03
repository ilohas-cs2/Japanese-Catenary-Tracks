using System;
using System.Linq;
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Game.Prefabs;
namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  string mixedFeederAudit="";
  string mixedObjectWireAudit="";
  void AuditMixedPortalFeeders(List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors,Dictionary<Entity,PortalEdge> routes,Dictionary<Entity,List<PortalEdge>> nodes,Dictionary<Entity,int> degree,HashSet<Entity> feeders){
   var joins=nodes.Where(n=>degree[n.Key]==2&&n.Value.Count==2&&n.Value[0].family!=n.Value[1].family).OrderBy(n=>n.Key.Index).ToArray();
   if(joins.Length==0){mixedFeederAudit="";return;}
   AuditPortalObjects(poses,anchors);
   var caps=new Dictionary<Entity,List<string>>();var nodeWires=new Dictionary<Entity,int>();var suppressed=new Dictionary<Entity,int>();
   foreach(var j in joins){caps[j.Key]=new List<string>();nodeWires[j.Key]=0;suppressed[j.Key]=0;}
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=q.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;if(!feeders.Contains(prefab))continue;
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    if(nodeWires.ContainsKey(owner)){nodeWires[owner]++;if(EntityManager.HasComponent<MixedJoinHidden>(e))suppressed[owner]++;}
    PortalEdge edge;if(!routes.TryGetValue(owner,out edge)||!EntityManager.HasComponent<Game.Net.EdgeLane>(e))continue;
    var delta=EntityManager.GetComponentData<Game.Net.EdgeLane>(e).m_EdgeDelta;
    var curve=EntityManager.GetComponentData<Game.Net.Curve>(e).m_Bezier;
    foreach(bool atStart in new[]{true,false}){
     if(atStart?delta.x>.00001f:delta.y<.99999f)continue;
     var node=atStart?edge.edge.m_Start:edge.edge.m_End;if(!caps.ContainsKey(node))continue;
     var point=atStart?curve.a:curve.d;
     var support=poses.Where(p=>p.owner==node&&anchors.ContainsKey(p.entity)).SelectMany(p=>anchors[p.entity]).Where(a=>a.prefab==prefab).ToArray();
     string gap=support.Length==0?"no-support":support.Min(a=>math.distance(a.point,point)).ToString("F3",System.Globalization.CultureInfo.InvariantCulture);
     caps[node].Add("edge"+owner.Index+"/channel"+prefab.Index+"/lane"+e.Index+":gap="+gap+",hidden="+EntityManager.HasComponent<Game.Tools.Hidden>(e)+",length="+math.distance(curve.a,curve.d).ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
    }
   }
   string report=string.Join("; ",joins.Select(j=>"node"+j.Key.Index+" "+string.Join("/",j.Value.Select(e=>e.family.key))+" supports="+poses.Count(p=>p.owner==j.Key&&anchors.ContainsKey(p.entity))+" nodeWires="+nodeWires[j.Key]+" suppressedNodeWires="+suppressed[j.Key]+" caps=["+string.Join(" | ",caps[j.Key].OrderBy(s=>s))+"]"));
   if(report==mixedFeederAudit)return;
   mixedFeederAudit=report;Mod.Log.Info("PORTAL_MIXED_FEEDERS "+report);
  }
  // Endpoint agreement alone cannot diagnose orphan/duplicate object lanes
  // or a hidden support. Log those separately, without changing their state.
  void AuditPortalObjects(List<PortalPose> poses,Dictionary<Entity,List<PortalAnchor>> anchors){
   var poleIds=new HashSet<Entity>(poses.Select(p=>p.entity));
   var rows=new List<string>();
   foreach(var p in poses.OrderBy(p=>p.entity.Index)){
    var forward=math.mul(p.pose.m_Rotation,new float3(0,0,1));
    rows.Add("pole="+p.entity.Index+" owner="+p.owner.Index+" family="+p.family.key+" pos="+AuditPoint(p.pose.m_Position)+" forward="+AuditPoint(forward)+" supported="+anchors.ContainsKey(p.entity)+" "+AuditVisibility(p.entity));
   }
   var q=GetEntityQuery(ComponentType.ReadOnly<Game.Net.Lane>(),ComponentType.ReadOnly<Game.Net.Curve>(),ComponentType.ReadOnly<Game.Common.Owner>(),ComponentType.ReadOnly<PrefabRef>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   using(var entities=q.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities){
    var owner=EntityManager.GetComponentData<Game.Common.Owner>(e).m_Owner;
    for(int i=0;i<4&&!poleIds.Contains(owner)&&EntityManager.HasComponent<Game.Common.Owner>(owner);i++)owner=EntityManager.GetComponentData<Game.Common.Owner>(owner).m_Owner;
    if(!poleIds.Contains(owner))continue;
    var prefab=EntityManager.GetComponentData<PrefabRef>(e).m_Prefab;
    if(!EntityManager.HasComponent<UtilityLaneData>(prefab))continue;
    var curve=EntityManager.GetComponentData<Game.Net.Curve>(e);
    if(curve.m_Length<.1f)continue;
    rows.Add("objectWire="+e.Index+" pole="+owner.Index+" channel="+prefab.Index+" length="+curve.m_Length.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" a="+AuditPoint(curve.m_Bezier.a)+" b="+AuditPoint(curve.m_Bezier.b)+" c="+AuditPoint(curve.m_Bezier.c)+" d="+AuditPoint(curve.m_Bezier.d)+" "+AuditVisibility(e));
   }
   rows.Sort(StringComparer.Ordinal);var report=string.Join("; ",rows);
   if(report==mixedObjectWireAudit)return;
   mixedObjectWireAudit=report;
   Mod.Log.Info("PORTAL_OBJECT_AUDIT "+report);
  }
  string AuditVisibility(Entity e){
   string mask=EntityManager.HasComponent<Game.Rendering.CullingInfo>(e)?EntityManager.GetComponentData<Game.Rendering.CullingInfo>(e).m_Mask.ToString():"absent";
   return "hidden="+EntityManager.HasComponent<Game.Tools.Hidden>(e)+" selectedHidden="+EntityManager.HasComponent<MixedJoinHidden>(e)+" overridden="+EntityManager.HasComponent<Game.Common.Overridden>(e)+" mask="+mask;
  }
  static string AuditPoint(float3 p){return p.x.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+","+p.y.ToString("F2",System.Globalization.CultureInfo.InvariantCulture)+","+p.z.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);}
 }
}
