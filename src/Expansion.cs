using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Reflection;
using Game;
using Game.Prefabs;
using Game.SceneFlow;
using Colossal.Localization;
using Colossal.Mathematics;
using Unity.Mathematics;
using UnityEngine;
using Unity.Entities;

namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  static readonly string[][] ExpansionPairs={new[]{"GeneralIO","GeneralI","GeneralO"},new[]{"ConcreteIO","ConcreteInward","ConcreteOutward"},new[]{"HairpinAB","HairpinA","HairpinB"},new[]{"SteelTubeAB","SteelTubeCentral","SteelTubeDirect"},new[]{"TrussAB","TrussOutward","TrussInward"},new[]{"TrussDualAB","TrussDualOutward","TrussDualInward"},new[]{"FullSteelAB","FullSteelCentral","FullSteelDirect"},new[]{"SingleFrameIO","SingleFrameInward","SingleFrameOutward"},new[]{"OriginalIO","OriginalI","OriginalO"}};
  string[][] activePairs;
  ExpansionCatalog expansionCatalog;
  Task expansionImport;
  string expansionPending;
  StaticObjectPrefab expansionExisting;
  const string HairpinRevision="horizontal-upper-arm-0206";
  static string ExpansionRevision(ExpansionModel m){return (m.common_wiring||m.single_common_wiring)?m.revision:m.key=="HairpinA"?HairpinRevision:null;}
  static bool NeedsExpansionRefresh(ExpansionModel m){var revision=ExpansionRevision(m);return !string.IsNullOrEmpty(revision)&&(!File.Exists(RevisionPath(m.name))||File.ReadAllText(RevisionPath(m.name)).Trim()!=revision);}
  bool expansionFailed,expansionBuilt;
  readonly List<TrackPrefab> expansionTracks=new List<TrackPrefab>();
  readonly List<PrefabBase> expansionPrefabs=new List<PrefabBase>();
  readonly HashSet<TrackPrefab> expansionLinked=new HashSet<TrackPrefab>();
  readonly NetLanePrefab[] expansionFeederChannels=new NetLanePrefab[4];
  readonly NetLanePrefab[] commonUpperChannels=new NetLanePrefab[5];
  static readonly string[] CommonUpperRoles={"FeederLeft","FeederRight","DistributionOuter","DistributionMiddle","DistributionInner"};
  void UpdateExpansion(PrefabBase[] all){
   if(expansionFailed)return;
   try {
    if(expansionBuilt){
     if(expansionLinked.Count==expansionTracks.Count)return;
     foreach(var p in expansionPrefabs){
      Entity e;if(!system.TryGetEntity(p,out e)||!EntityManager.Exists(e))return;
      if(p is StaticObjectPrefab && (!EntityManager.HasComponent<ObjectData>(e)||!EntityManager.GetComponentData<ObjectData>(e).m_Archetype.Valid))return;
      var deps=new List<PrefabBase>();p.GetDependencies(deps);foreach(var c in p.components)c.GetDependencies(deps);
      foreach(var d in deps){Entity de;if(d==null||!system.TryGetEntity(d,out de)||!EntityManager.Exists(de))throw new InvalidOperationException("Expansion missing dependency: "+p.name);}
     }
     foreach(var t in expansionTracks)if(!expansionLinked.Contains(t)&&LinkTrackMenu(t))expansionLinked.Add(t);
     return;
    }
    string catalogPath=Path.Combine(Mod.DirectoryPath,"ExpansionCatalog.json");
    if(!File.Exists(catalogPath))return;
    if(expansionCatalog==null){
     expansionCatalog=ExpansionCatalogReader.Read(File.ReadAllText(catalogPath),catalogPath);
     string selectionPath=Path.Combine(Mod.DirectoryPath,"ExpansionSelection.json");
     var selection=ExpansionCatalogReader.ReadSelection(File.ReadAllText(selectionPath));
     if(selection.families==null||selection.families.Length==0||selection.families.Distinct().Count()!=selection.families.Length||selection.families.Any(k=>!ExpansionPairs.Any(p=>p[0]==k)))throw new InvalidOperationException("Invalid expansion family selection");
     activePairs=ExpansionPairs.Where(p=>selection.families.Contains(p[0])).ToArray();
     Mod.Log.Info("EXPANSION_CATALOG_LOADED parser=Newtonsoft models="+expansionCatalog.models.Length+" uniqueNames="+expansionCatalog.models.Select(m=>m.name).Distinct().Count()+" families="+string.Join(",",selection.families)+" path="+catalogPath);
    }
    if(expansionImport!=null){
     if(!expansionImport.IsCompleted)return;
     if(expansionImport.IsFaulted)throw expansionImport.Exception;
     if(expansionImport.IsCanceled)throw new InvalidOperationException("Expansion import cancelled");
     int count=all.OfType<StaticObjectPrefab>().Count(p=>p.name==expansionPending);
     if(count!=1)throw new InvalidOperationException("Expansion import registration count="+count+" for "+expansionPending+". Check AssetPipeline.log: a completed import task may contain skipped assets.");
     if(expansionExisting!=null){
      var updated=all.OfType<StaticObjectPrefab>().Single(p=>p.name==expansionPending);
      if(!ReferenceEquals(updated,expansionExisting))throw new InvalidOperationException("Expansion refresh replaced existing wrapper: "+expansionPending);
      var factory=expansionImport.GetType().GetProperty("Result").GetValue(expansionImport);
      var refreshed=((IEnumerable<PrefabBase>)factory.GetType().GetProperty("prefabs").GetValue(factory)).OfType<RenderPrefab>().ToArray();
      if(updated.m_Meshes.Any(m=>!refreshed.Any(r=>ReferenceEquals(r,m.m_Mesh))))throw new InvalidOperationException("Expansion refresh did not reuse mesh references: "+expansionPending);
      system.AddOrUpdatePrefab(updated);
     }
     var completedModel=expansionCatalog.models.Single(m=>m.name==expansionPending);
     var completedRevision=ExpansionRevision(completedModel);
     if(!string.IsNullOrEmpty(completedRevision))File.WriteAllText(RevisionPath(expansionPending),completedRevision);
     Mod.Log.Info("EXPANSION_IMPORTED "+expansionPending);
     expansionImport=null;expansionPending=null;expansionExisting=null;return;
    }
    foreach(var m in expansionCatalog.models){
     if(!activePairs.Any(p=>p[1]==m.key||p[2]==m.key||p[1]+"Double"==m.key||p[2]+"Double"==m.key))continue;
     int count=all.OfType<StaticObjectPrefab>().Count(p=>p.name==m.name);
     if(count>1)throw new InvalidOperationException("Duplicate expansion model "+m.name);
     if(count==1&&!NeedsExpansionRefresh(m))continue;
     if(GameManager.instance.gameMode!=GameMode.Editor)return;
     string root=Path.Combine(Mod.DirectoryPath,"ProjectFiles");
     if(!File.Exists(Path.Combine(root,m.name,m.name+".fbx")))throw new FileNotFoundException("Expansion model source missing: "+m.name);
     foreach(var file in Directory.GetFiles(Path.Combine(root,m.name))){
      if(Path.GetExtension(file)==".json")continue;
      string theme,assetName,material,suffix;int level,lod;int2 lot;Colossal.AssetPipeline.Module module;
      Colossal.AssetPipeline.AssetUtils.ParseName(Path.GetFileNameWithoutExtension(file),out theme,out assetName,out level,out lot,out module,out lod,out material,out suffix);
      if(assetName!=m.name)throw new InvalidOperationException("Expansion import filename resolves to wrong asset: "+file+" -> "+assetName);
     }
     var method=typeof(Game.UI.Editor.AssetImportPanel).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(x=>x.Name=="ImportAssets"&&x.GetParameters().Length==8);
     expansionExisting=count==1?all.OfType<StaticObjectPrefab>().Single(p=>p.name==m.name):null;
     string preset=expansionExisting==null?"StaticObject":"None";
     var args=new object[]{root,Path.Combine(root,m.name),"JP-Catenary-Expansion-20261002-"+m.key+"-v1",Enum.Parse(method.GetParameters()[3].ParameterType,preset),null,World,Mod.Log,null};
     var last=method.GetParameters()[7].ParameterType;if(last.IsValueType)args[7]=Activator.CreateInstance(last);
     expansionPending=m.name;Mod.Log.Info("EXPANSION_IMPORT_BEGIN "+m.name+" preset="+preset);expansionImport=(Task)method.Invoke(null,args);return;
    }
    var originalState=all.Where(p=>p is TrackPrefab||p is NetSectionPrefab||p is NetPiecePrefab||p is NetLanePrefab||p is StaticObjectPrefab).ToDictionary(p=>p,p=>Fingerprint(p));
    var labels=new Dictionary<string,string>();int priority=9600;
    for(int i=0;i<expansionFeederChannels.Length;i++){
     var channel=(NetLanePrefab)feederLane.Clone("JPC2_SharedFeederChannel"+i);
     if(!system.AddPrefab(channel))throw new InvalidOperationException("Shared feeder registration rejected: "+i);
     expansionFeederChannels[i]=channel;expansionPrefabs.Add(channel);
    }
    for(int i=0;i<commonUpperChannels.Length;i++){
     var channel=(NetLanePrefab)feederLane.Clone("JPC3_"+CommonUpperRoles[i]);
     if(!system.AddPrefab(channel))throw new InvalidOperationException("Common wire registration rejected: "+CommonUpperRoles[i]);
     commonUpperChannels[i]=channel;expansionPrefabs.Add(channel);
    }
    foreach(var pair in activePairs){
     var m=expansionCatalog.models.Single(x=>x.key==pair[1]);var alternate=expansionCatalog.models.Single(x=>x.key==pair[2]);
     var imported=all.OfType<StaticObjectPrefab>().Single(p=>p.name==m.name);var importedB=all.OfType<StaticObjectPrefab>().Single(p=>p.name==alternate.name);
     var builder=new ExpansionBuilder(pair[0],m,alternate,imported,importedB,pole,feederLane,expansionFeederChannels);
     var familyTracks=new List<TrackPrefab>();
     if(!m.portal&&pair[0]!="OriginalIO"){
      familyTracks.Add(builder.Build(alternatingSingleTrack,"Twoway",true,false,priority++,labels));
      familyTracks.Add(builder.Build(alternatingOnewayTrack,"Oneway",true,false,priority++,labels));
     }
     // Retain original support identities for saved games and single tracks.
     // Double tracks use a complete support row with role-specific upper wires.
     if(familyTracks.Count>0)builder.Validate();
     foreach(var p in builder.added)if(!system.AddPrefab(p))throw new InvalidOperationException("Expansion registration rejected: "+p.name);
     expansionPrefabs.AddRange(builder.added);
     expansionTracks.AddRange(familyTracks);
     if(familyTracks.Count>0)expansionFamilies.Add(new AlternatingFamily{key=pair[0]+"Single",a=builder.networkPole,b=builder.alternatePole,tracks=familyTracks.ToArray(),portal=false,commonWiring=m.single_common_wiring,projectionAnchor=new float3(Reach,ContactLocal,0)});
     var dm=expansionCatalog.models.Single(x=>x.key==pair[1]+"Double");var db=expansionCatalog.models.Single(x=>x.key==pair[2]+"Double");
     var doubles=new ExpansionBuilder(pair[0],dm,db,all.OfType<StaticObjectPrefab>().Single(p=>p.name==dm.name),all.OfType<StaticObjectPrefab>().Single(p=>p.name==db.name),pole,feederLane,commonUpperChannels);
     var paired=doubles.Build(doubleTrack,"Double",false,true,priority++,labels);doubles.Validate();
     foreach(var p in doubles.added)if(!system.AddPrefab(p))throw new InvalidOperationException("Common double registration rejected: "+p.name);
     expansionPrefabs.AddRange(doubles.added);expansionTracks.Add(paired);
     expansionFamilies.Add(new AlternatingFamily{key=pair[0],a=doubles.networkPole,b=doubles.alternatePole,tracks=new[]{paired},portal=true,commonWiring=true,projectionAnchor=new float3(0,ContactLocal,0)});
     Mod.Log.Info("EXPANSION_REGISTERED "+dm.key+" commonWiring=true feeders=2 distribution=3 prefabs="+doubles.added.Count);
    }
    foreach(var kv in originalState)if(Fingerprint(kv.Key)!=kv.Value)throw new InvalidOperationException("Expansion mutated existing prefab: "+kv.Key.name);
    int expectedTracks=activePairs.Sum(p=>expansionCatalog.models.Single(m=>m.key==p[1]).portal||p[0]=="OriginalIO"?1:3);
    if(expansionTracks.Count!=expectedTracks)throw new InvalidOperationException("Expansion track count mismatch");
    foreach(string language in new[]{"ja-JP","en-US"})GameManager.instance.localizationManager.AddSource(language,new MemorySource(labels));
    expansionBuilt=true;Mod.Log.Info("EXPANSION_READY models="+(activePairs.Length*4)+" alternatingFamilies="+expansionFamilies.Count+" tracks="+expansionTracks.Count+" existingTracks=5 contactAboveRail=5m commonUpperRoles=5; runtime placement pending");
   }catch(Exception e){expansionFailed=true;Mod.Log.Error(e,"Expansion stopped; existing five prototype tracks remain available.");}
  }

  sealed class ExpansionBuilder {
   readonly ExpansionModel model;
   public readonly StaticObjectPrefab networkPole,alternatePole;
   readonly string familyKey;
   readonly NetLanePrefab[] feeders;
   readonly float3[] contact,messenger,feed;
   public readonly List<PrefabBase> added=new List<PrefabBase>();
   Dictionary<PrefabBase,PrefabBase> cache;
   bool single,doubleLayout,left,oneway;
   string prefix;
   readonly List<string> placementChecks=new List<string>();
   public ExpansionBuilder(string key,ExpansionModel m,ExpansionModel b,StaticObjectPrefab imported,StaticObjectPrefab importedB,StaticObjectPrefab template,NetLanePrefab feederTemplate,NetLanePrefab[] channels){
    familyKey=key;
    model=m;contact=m.contacts.Select(p=>p.Value).ToArray();messenger=m.messengers.Select(p=>p.Value).ToArray();feed=m.UpperAnchors.Select(p=>p.Value).ToArray();
    if(contact.Length!=(m.portal?2:1)||messenger.Length!=contact.Length||feed.Length==0)throw new InvalidOperationException("Missing anchors: "+m.key);
    if(contact.Any(p=>math.abs(p.y-ContactLocal)>.001f))throw new InvalidOperationException("Contact height mismatch: "+m.key);
    networkPole=(StaticObjectPrefab)template.Clone(m.name+"_Network");networkPole.m_Meshes=imported.m_Meshes;
    networkPole.Remove<UIObject>();networkPole.Remove<PlaceableObject>();
    var oldAnchors=template.GetComponent<ObjectSubLanes>().m_SubLanes;
    var cLane=oldAnchors.Single(a=>a.m_LanePrefab.name=="Train Contact Cable Lane").m_LanePrefab;
    var mLane=oldAnchors.Single(a=>a.m_LanePrefab.name=="Train Catenary Cable Lane").m_LanePrefab;
    var anchors=new List<ObjectSubLaneInfo>();int node=0;
    foreach(var p in contact)anchors.Add(Anchor(cLane,p,node++));
    foreach(var p in messenger)anchors.Add(Anchor(mLane,p,node++));
    feeders=new NetLanePrefab[feed.Length];
    if(feed.Length>channels.Length||channels.Any(c=>c==null))throw new InvalidOperationException("Shared feeder channels unavailable: "+key);
    for(int i=0;i<feed.Length;i++){
     // Keep legacy prefab identities registered for existing saves, but use
     // common channel identities for actual wire/anchor compatibility.
     added.Add((NetLanePrefab)feederTemplate.Clone("JPC2_"+m.key+"_Feeder"+i));
     feeders[i]=channels[i];anchors.Add(Anchor(feeders[i],feed[i],node++));
    }
    networkPole.GetComponent<ObjectSubLanes>().m_SubLanes=anchors.ToArray();networkPole.GetComponent<UtilityObject>().m_UtilityPosition=contact[0];added.Add(networkPole);
    if(b.portal!=m.portal)throw new InvalidOperationException("Alternation layout mismatch: "+key);
    foreach(var pair in new[]{new[]{m.contacts,b.contacts},new[]{m.messengers,b.messengers},new[]{m.UpperAnchors,b.UpperAnchors}}){
     if(pair[0].Length!=pair[1].Length)throw new InvalidOperationException("Alternation anchor count mismatch: "+key);
     for(int i=0;i<pair[0].Length;i++)if(math.distance(pair[0][i].Value,pair[1][i].Value)>.01f)throw new InvalidOperationException("Alternation anchor position mismatch: "+key);
    }
    alternatePole=(StaticObjectPrefab)networkPole.Clone(b.name+"_Network");alternatePole.m_Meshes=importedB.m_Meshes;
    var alternateAnchors=new List<ObjectSubLaneInfo>();node=0;
    foreach(var p in b.contacts)alternateAnchors.Add(Anchor(cLane,p.Value,node++));
    foreach(var p in b.messengers)alternateAnchors.Add(Anchor(mLane,p.Value,node++));
    for(int i=0;i<feed.Length;i++)alternateAnchors.Add(Anchor(feeders[i],b.UpperAnchors[i].Value,node++));
    alternatePole.GetComponent<ObjectSubLanes>().m_SubLanes=alternateAnchors.ToArray();alternatePole.GetComponent<UtilityObject>().m_UtilityPosition=b.contacts[0].Value;added.Add(alternatePole);
   }
   static ObjectSubLaneInfo Anchor(NetLanePrefab lane,float3 p,int index){return new ObjectSubLaneInfo{m_LanePrefab=lane,m_BezierCurve=new Bezier4x3(p,p,p,p),m_NodeIndex=new int2(index),m_ParentMesh=new int2(0)};}
   public TrackPrefab Build(TrackPrefab source,string kind,bool isSingle,bool isDouble,int priority,Dictionary<string,string> labels){
    single=isSingle;doubleLayout=isDouble;oneway=kind=="Oneway";prefix="JPC2_"+familyKey+"_"+kind+"_";
    var result=(TrackPrefab)source.Clone(prefix+"Track");
    if(oneway)result.m_InvertMode=CatenaryPlacement.SingleOnewayComposition;
    var sourceChecks=new Dictionary<PrefabBase,string>();
    var leftCache=new Dictionary<PrefabBase,PrefabBase>();var rightCache=new Dictionary<PrefabBase,PrefabBase>();
    foreach(var section in result.m_Sections){
     left=single||section.m_Invert;
     // A lane's auxiliary offset is in network coordinates, so inverted and
     // non-inverted sections must never reuse the same edited lane instance.
     cache=left?leftCache:rightCache;
     section.m_Section=CopySection(section.m_Section,left?"L":"R",sourceChecks);
    }
    foreach(var p in sourceChecks)if(Fingerprint(p.Key)!=p.Value)throw new InvalidOperationException("Source changed while cloning expansion: "+p.Key.name);
    result.Remove<Unlockable>();result.Remove<UnlockOnBuild>();
    var ui=result.GetComponent<UIObject>();ui.m_Priority=priority;ui.m_IsDebugObject=false;
    TrackIcons.Apply(result);
    labels["Assets.NAME["+result.name+"]"]="JPC2 "+familyKey+" / "+kind+" (5m)";
    labels["Assets.DESCRIPTION["+result.name+"]"]=model.common_wiring?"共通配線の複線架線。配電線は片側3本、き電線は左右1本ずつ。トロリ線はレール上5m。":"Photo model prototype with feeder wires. Contact wire 5m above rail. Ground and elevated test; junctions under development.";
    added.Add(result);return result;
   }
   NetSectionPrefab CopySection(NetSectionPrefab src,string side,Dictionary<PrefabBase,string> checks){
    if(src==null)return null;PrefabBase old;if(cache.TryGetValue(src,out old))return (NetSectionPrefab)old;
    checks[src]=Fingerprint(src);var dst=(NetSectionPrefab)src.Clone(prefix+side+"_"+src.name);cache[src]=dst;
    if(dst.m_SubSections!=null)foreach(var sub in dst.m_SubSections)sub.m_Section=CopySection(sub.m_Section,side,checks);
    if(dst.m_Pieces!=null)foreach(var part in dst.m_Pieces)part.m_Piece=CopyPiece(part.m_Piece,side,checks);
    added.Add(dst);return dst;
   }
   NetPiecePrefab CopyPiece(NetPiecePrefab src,string side,Dictionary<PrefabBase,string> checks){
    if(src==null)return null;PrefabBase old;if(cache.TryGetValue(src,out old))return (NetPiecePrefab)old;
    checks[src]=Fingerprint(src);var dst=(NetPiecePrefab)src.Clone(prefix+side+"_"+src.name);cache[src]=dst;
    var lanes=dst.GetComponent<NetPieceLanes>();
    if(lanes!=null)foreach(var l in lanes.m_Lanes)if(l.m_Lane!=null&&l.m_Lane.GetComponent<AuxiliaryLanes>()!=null)l.m_Lane=CopyLane(l.m_Lane,side,checks);
    var objects=dst.GetComponent<NetPieceObjects>();
    if(objects!=null&&objects.m_PieceObjects!=null){
     var keep=new List<NetPieceObjectInfo>();
     foreach(var o in objects.m_PieceObjects){
      if(o.m_Object!=null&&o.m_Object.name.StartsWith("JPCatenary_")&&o.m_Object.name.EndsWith("NetworkPole")){
       if(model.portal&&left)continue;
       o.m_Object=networkPole;
       CatenaryPlacement.MixedJoinEndpoint(o);
       if(oneway)CatenaryPlacement.SingleOnewayLeft(o,Reach,PoleY);
       if(model.portal){o.m_Position=new float3(-2,PoleY,0);o.m_Rotation=quaternion.identity;CatenaryPlacement.PortalOrientation(o);}
       placementChecks.Add(dst.name);
      }
      keep.Add(o);
     }
     objects.m_PieceObjects=keep.ToArray();
    }
    added.Add(dst);return dst;
   }
   NetLanePrefab CopyLane(NetLanePrefab src,string side,Dictionary<PrefabBase,string> checks){
    PrefabBase old;if(cache.TryGetValue(src,out old))return (NetLanePrefab)old;
    checks[src]=Fingerprint(src);var dst=(NetLanePrefab)src.Clone(prefix+side+"_"+src.name);cache[src]=dst;
    var aux=dst.GetComponent<AuxiliaryLanes>();var list=new List<AuxiliaryLaneInfo>();
    int mi=model.portal?(left?0:1):0;
    foreach(var a in aux.m_AuxiliaryLanes){
     if(a.m_Lane.name=="JPCatenary_Feeder_Cable_Lane")continue;
     if(a.m_Lane.name=="Train Contact Cable Lane"||a.m_Lane.name=="Train Catenary Cable Lane")CatenaryPlacement.RailWireSpan(a,model.portal||model.single_common_wiring);
     if(a.m_Lane.name=="Train Contact Cable Lane")a.m_Position.y=ContactY;
     if(a.m_Lane.name=="Train Catenary Cable Lane"){
      a.m_Position.y=PoleY+messenger[mi].y;
      a.m_Position.x=model.portal?messenger[mi].x-(left?-2:2):(left?messenger[mi].x-Reach:Reach-messenger[mi].x);
     }
     list.Add(a);
    }
    if(!model.portal||!left)for(int i=0;i<feed.Length;i++){
     float x=model.portal?feed[i].x-2:(left?feed[i].x-Reach:Reach-feed[i].x);
     var feeder=new AuxiliaryLaneInfo{m_Lane=feeders[i],m_Position=new float3(x,PoleY+feed[i].y,0),m_RequireAll=new NetPieceRequirements[0],m_RequireAny=new[]{NetPieceRequirements.Edge,NetPieceRequirements.Intersection,NetPieceRequirements.DeadEnd,NetPieceRequirements.LevelCrossing},m_RequireNone=CatenaryPlacement.WireExclusions(),m_Spacing=new float3(0,0,50),m_FindAnchor=true};
     CatenaryPlacement.FeederSpan(feeder);
     if(model.common_wiring||model.single_common_wiring)feeder.m_FindAnchor=false; // Align by support row and explicit role; never consume another role's native anchor.
     list.Add(feeder);
    }
    aux.m_AuxiliaryLanes=list.ToArray();added.Add(dst);return dst;
   }
   public void Validate(){
    if(placementChecks.Count==0)throw new InvalidOperationException("No pole placements for "+model.key);
    if(added.Select(p=>p.name).Distinct().Count()!=added.Count)throw new InvalidOperationException("Duplicate generated names for "+model.key);
   }
  }
 }
}

