using System;
using System.Linq;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace JPCatenaryPrototype {
 [Serializable] public class ExpansionPoint {public float x,y,z; [JsonIgnore] public float3 Value {get{return new float3(x,y,z);}}}
 [Serializable] public class ExpansionModel {public string key,name,revision;public bool portal,common_wiring,single_common_wiring;public ExpansionPoint[] contacts,messengers,feeder_points,distribution_points;
  [JsonIgnore] public ExpansionPoint[] UpperAnchors {get{return common_wiring||single_common_wiring?feeder_points.Concat(distribution_points).ToArray():feeder_points;}}
 }
 [Serializable] public class ExpansionCatalog {public ExpansionModel[] models;}
 [Serializable] public class ExpansionSelection {public string[] families;}
 public static class ExpansionCatalogReader {
  // Mod-owned DTOs are read with the game's managed JSON library. This does
  // not depend on Unity's player serialization metadata for late-loaded types.
  static readonly JsonSerializerSettings Settings=new JsonSerializerSettings{TypeNameHandling=TypeNameHandling.None,MissingMemberHandling=MissingMemberHandling.Ignore};
  public static ExpansionCatalog Read(string json,string source){
   var catalog=JsonConvert.DeserializeObject<ExpansionCatalog>(json,Settings);
   int count=catalog==null||catalog.models==null?0:catalog.models.Length;
   if(count!=36)throw new InvalidOperationException("Expansion catalog model count="+count+" expected=36 path="+source);
   if(catalog.models.Any(m=>m==null||string.IsNullOrWhiteSpace(m.key)||string.IsNullOrWhiteSpace(m.name)))throw new InvalidOperationException("Expansion catalog has an empty model/key/name: "+source);
   int names=catalog.models.Select(m=>m.name).Distinct().Count(),keys=catalog.models.Select(m=>m.key).Distinct().Count();
   if(names!=count||keys!=count)throw new InvalidOperationException("Expansion catalog duplicate identity: models="+count+" names="+names+" keys="+keys+" path="+source);
   foreach(var m in catalog.models){
    int expected=m.portal?2:1;
    if(m.contacts==null||m.contacts.Length!=expected||m.messengers==null||m.messengers.Length!=expected||m.feeder_points==null||m.feeder_points.Length==0)throw new InvalidOperationException("Expansion catalog missing anchors: "+m.key);
    if(m.common_wiring&&(!m.portal||m.feeder_points.Length!=2||m.distribution_points==null||m.distribution_points.Length!=3))throw new InvalidOperationException("Common wiring requires two feeders and three distribution anchors: "+m.key);
    if(m.single_common_wiring&&(m.portal||m.common_wiring||m.feeder_points.Length!=1||m.distribution_points==null||m.distribution_points.Length!=3||string.IsNullOrEmpty(m.revision)))throw new InvalidOperationException("Single wiring requires one feeder and three distribution anchors with a revision: "+m.key);
    foreach(var p in m.contacts.Concat(m.messengers).Concat(m.UpperAnchors))if(p==null||!Finite(p.x)||!Finite(p.y)||!Finite(p.z))throw new InvalidOperationException("Expansion catalog invalid anchor: "+m.key);
    if(m.common_wiring&&(m.feeder_points[0].x>=0||m.feeder_points[1].x<=0||m.distribution_points.Any(p=>p.x>=0)))throw new InvalidOperationException("Common wiring side mismatch: "+m.key);
    if(m.contacts.Any(p=>Math.Abs(p.y-5.2f)>.001f))throw new InvalidOperationException("Expansion catalog contact height: "+m.key);
   }
   return catalog;
  }
  public static ExpansionSelection ReadSelection(string json){
   var result=JsonConvert.DeserializeObject<ExpansionSelection>(json,Settings);
   if(result==null)throw new InvalidOperationException("Expansion selection is null");
   return result;
  }
  static bool Finite(float v){return !float.IsNaN(v)&&!float.IsInfinity(v);}
 }
}

