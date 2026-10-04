using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Game;
using Game.Modding;
using Game.Prefabs;
using Game.SceneFlow;
using Colossal.Localization;
using Colossal.Logging;
using Colossal.Mathematics;
using UnityEngine;
using Unity.Mathematics;
using Unity.Entities;

[assembly: AssemblyVersion("0.3.0.10")]
namespace JPCatenaryPrototype {
 public sealed class Mod : IMod {
  public const string TrackName="JPCatenary_SingleTrack_5m_Prototype";
  public const string DoubleTrackName="JPCatenary_DoubleTrack_5m_Prototype";
  public const string PoleName="JPCatenaryI8mPrototype";
  public const string OPoleName="JPCatenaryO8mPrototype";
  public const string AlternatingTrackName="JPCatenary_DoubleTrack_IO_5m_Prototype";
  public const string AlternatingSingleName="JPCatenary_SingleTrack_Twoway_IO_5m_Prototype";
  public const string AlternatingOnewayName="JPCatenary_SingleTrack_Oneway_IO_5m_Prototype";
  public static readonly ILog Log=LogManager.GetLogger(nameof(JPCatenaryPrototype));
  public static string DirectoryPath;
  public void OnLoad(UpdateSystem updates) {
   Colossal.IO.AssetDatabase.ExecutableAsset executable;
   if(!GameManager.instance.modManager.TryGetExecutableAsset(this,out executable))throw new InvalidOperationException("Could not locate prototype mod executable asset.");
   DirectoryPath=Path.GetDirectoryName(executable.path);
   foreach(var language in new[]{"ja-JP","en-US"}) {
    bool ja=language=="ja-JP";
    GameManager.instance.localizationManager.AddSource(language,new MemorySource(new Dictionary<string,string>{
     {"Assets.NAME["+TrackName+"]",ja?"日本型架線柱付き単線（5m・試作）":"JP catenary single track (5m prototype)"},
     {"Assets.DESCRIPTION["+TrackName+"]",ja?"I型8m柱・双方向単線。地上試験用。架線高はレール上面+5mを目標に設定。":"I-type 8m pole; bidirectional single track. Ground-level prototype; target contact height 5m above rail."},
     {"Assets.NAME["+DoubleTrackName+"]",ja?"日本型架線柱付き複線（5m・試作）":"JP catenary double track (5m prototype)"},
     {"Assets.DESCRIPTION["+DoubleTrackName+"]",ja?"I型8m柱を両外側に配置した複線。地上試験用。架線高はレール上面+5mを目標に設定。":"Double track with I-type 8m poles on both outer sides. Ground-level prototype; target contact height 5m above rail."},
     {"Assets.NAME["+AlternatingTrackName+"]",ja?"日本型架線柱付き複線（I/O交互・試作）":"JP catenary double track (alternating I/O prototype)"},
     {"Assets.DESCRIPTION["+AlternatingTrackName+"]",ja?"片側I/O交互、反対側O/I交互。地上試験用。交互順が両立しない接続では継ぎ目に例外が生じます。":"Alternating I/O with opposite sides. Ground prototype; conflicting junction/loop parity leaves a local seam."},
     {"Assets.NAME["+AlternatingSingleName+"]",ja?"日本型架線柱付き単線・両方向（I/O交互・試作）":"JP single track, two-way (alternating I/O prototype)"},
     {"Assets.DESCRIPTION["+AlternatingSingleName+"]",ja?"I/O交互架線柱の両方向単線。地上試験用・架線高約5m。":"Two-way single track with alternating I/O poles; ground prototype, 5m contact height."},
     {"Assets.NAME["+AlternatingOnewayName+"]",ja?"日本型架線柱付き単線・片方向（I/O交互・試作）":"JP single track, one-way (alternating I/O prototype)"},
     {"Assets.DESCRIPTION["+AlternatingOnewayName+"]",ja?"I/O交互架線柱の片方向単線。地上試験用・架線高約5m。":"One-way single track with alternating I/O poles; ground prototype, 5m contact height."}
    }));
   }
   updates.UpdateBefore<CatenarySystem,PrefabSystem>(SystemUpdatePhase.MainLoop);
   updates.UpdateBefore<CatenaryWireFinalizeSystem,Game.Rendering.RequiredBatchesSystem>(SystemUpdatePhase.ModificationEnd);
   updates.UpdateBefore<MixedJoinVisibilitySystem,Game.Rendering.PreCullingSystem>(SystemUpdatePhase.PreCulling);
   Log.Info("Loaded prototype 0.3.0.10. Railway-scoped, change-gated catenary correction.");
  }
  public void OnDispose() {}
 }

 public partial class CatenarySystem : GameSystemBase {
  // Rail mesh upper bound from the installed 1.6.2f1; final tread/contact-surface QA remains.
  const float RailTop=.054437995f, ContactLocal=5.2f, MessengerLocal=6.16f, Reach=3.675f;
  const float ContactY=RailTop+5f, MessengerY=ContactY+.96f, PoleY=ContactY-ContactLocal;
  const float FeederLocalX=-.66f,FeederLocalY=7.694f,FeederY=PoleY+FeederLocalY;
  const string ModelRevision="white-porcelain-018";
  PrefabSystem system;
  readonly Dictionary<PrefabBase,PrefabBase> copies=new Dictionary<PrefabBase,PrefabBase>();
  readonly List<PrefabBase> registration=new List<PrefabBase>();
  StaticObjectPrefab pole,placementPole,alternatingI,alternatingO;
  NetLanePrefab feederLane;
  TrackPrefab track,doubleTrack,alternatingTrack,alternatingSingleTrack,alternatingOnewayTrack;
  string copyPrefix="JPCP_";
  readonly HashSet<string> attemptedImports=new HashSet<string>();
  bool failed,linked,singleLayout;
  Task importTask;
  string pendingImportName;
  StaticObjectPrefab pendingExistingModel;
  int frames,laneCount,poleCount;
  readonly Dictionary<Entity,int> readinessChecks=new Dictionary<Entity,int>();
  static readonly FieldInfo PrefabList=typeof(PrefabSystem).GetField("m_Prefabs",BindingFlags.NonPublic|BindingFlags.Instance);
  protected override void OnCreate(){base.OnCreate();system=World.GetOrCreateSystemManaged<PrefabSystem>();}
  protected override void OnGameLoadingComplete(Colossal.Serialization.Entities.Purpose purpose,GameMode mode){base.OnGameLoadingComplete(purpose,mode);ResetWorkScope();linked=false;readinessChecks.Clear();expansionLinked.Clear();mixedReady=false;mixedOrder=0;mixedFailed=false;mixedStatus="";portalAlignmentFailed=false;portalWireStatus="";mixedFeederAudit="";redundantWireStatus="";}
  protected override void OnUpdate(){
   if(++frames%30!=1 || failed || GameManager.instance.isGameLoading)return;
   if(track!=null&&linked&&expansionBuilt&&expansionLinked.Count==expansionTracks.Count)return;
   try {
    var all=((IEnumerable<PrefabBase>)PrefabList.GetValue(system)).ToArray();
    var original=all.OfType<TrackPrefab>().FirstOrDefault(p=>p.name=="Twoway Train Track");
    if(original==null)return;
    var originalDouble=all.OfType<TrackPrefab>().FirstOrDefault(p=>p.name=="Double Train Track");
    if(originalDouble==null)return;
    var originalOneway=all.OfType<TrackPrefab>().FirstOrDefault(p=>p.name=="Oneway Train Track");
    if(originalOneway==null)return;
    if(track!=null){LinkMenu();if(linked){UpdateExpansion(all);}return;}
    if(importTask!=null){
     if(!importTask.IsCompleted)return;
     if(importTask.IsFaulted)throw importTask.Exception;
     if(importTask.IsCanceled)throw new InvalidOperationException("Model import cancelled.");
     var updatedModels=all.OfType<StaticObjectPrefab>().Where(p=>p.name==pendingImportName).ToArray();
     if(updatedModels.Length!=1)throw new InvalidOperationException("Updated model registration count="+updatedModels.Length+": "+pendingImportName);
     if(pendingExistingModel!=null){
      if(!ReferenceEquals(updatedModels[0],pendingExistingModel))throw new InvalidOperationException("Existing model instance replaced: "+pendingImportName);
      // Preset None refreshes the existing RenderPrefab in place. Ensure the
      // importer returned precisely the meshes already referenced by this pole.
      var factory=importTask.GetType().GetProperty("Result").GetValue(importTask);
      var refreshed=(IEnumerable<PrefabBase>)factory.GetType().GetProperty("prefabs").GetValue(factory);
      var refreshedMeshes=refreshed.OfType<RenderPrefab>().ToArray();
      if(pendingExistingModel.m_Meshes.Any(m=>!refreshedMeshes.Any(r=>ReferenceEquals(r,m.m_Mesh))))
       throw new InvalidOperationException("Model refresh did not reuse existing mesh references: "+pendingImportName);
      system.AddOrUpdatePrefab(pendingExistingModel);
     }
     File.WriteAllText(RevisionPath(pendingImportName),ModelRevision);
     Mod.Log.Info("MODEL_UPDATED "+pendingImportName+" revision="+ModelRevision);
     importTask=null;pendingImportName=null;pendingExistingModel=null;return;
    }
    var imported=all.OfType<StaticObjectPrefab>().Where(p=>p.name==Mod.PoleName).ToArray();
    if(imported.Length>1)throw new InvalidOperationException("Duplicate pole prefabs; refusing ambiguous registration.");
    var importedO=all.OfType<StaticObjectPrefab>().Where(p=>p.name==Mod.OPoleName).ToArray();
    if(importedO.Length>1)throw new InvalidOperationException("Duplicate O pole prefabs.");
    if(imported.Length==0||importedO.Length==0||NeedsModelUpdate(Mod.PoleName)||NeedsModelUpdate(Mod.OPoleName)){
     var missing=imported.Length==0||NeedsModelUpdate(Mod.PoleName)?Mod.PoleName:Mod.OPoleName;
     if(attemptedImports.Contains(missing))throw new InvalidOperationException("Pole import did not register "+missing+". Check AssetPipeline.log.");
     if(GameManager.instance.gameMode!=GameMode.Editor || GameManager.instance.isGameLoading)return;
     attemptedImports.Add(missing);
     string root=Path.Combine(Mod.DirectoryPath,"ProjectFiles");
     if(!File.Exists(Path.Combine(root,missing,missing+".fbx")))throw new FileNotFoundException("Prototype source FBX not found",root);
     var method=typeof(Game.UI.Editor.AssetImportPanel).GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static).Single(m=>m.Name=="ImportAssets"&&m.GetParameters().Length==8);
     var seed=missing==Mod.PoleName?"JP-Catenary-Prototype-20261001-v1":"JP-Catenary-O-Prototype-20261001-v1";
     pendingExistingModel=(missing==Mod.PoleName?imported:importedO).SingleOrDefault();
     // StaticObject preset always creates and AddPrefab-registers a new wrapper,
     // even when its CID already exists. For an existing pole import only its
     // render assets (None), preserving its wrapper, CID and mesh references.
     string presetName=pendingExistingModel==null?"StaticObject":"None";
     var args=new object[]{root,Path.Combine(root,missing),seed,Enum.Parse(method.GetParameters()[3].ParameterType,presetName),null,World,Mod.Log,null};
     var last=method.GetParameters()[7].ParameterType;if(last.IsValueType)args[7]=Activator.CreateInstance(last);
     Mod.Log.Info("IMPORT_BEGIN "+missing+" preset="+presetName+" reuseExisting="+(pendingExistingModel!=null));
     pendingImportName=missing;
     importTask=(Task)method.Invoke(null,args);return;
    }
    Build(original,originalDouble,originalOneway,imported[0],importedO[0],all);
   } catch(Exception e){failed=true;Mod.Log.Error(e,"Prototype stopped; original network data was not intentionally modified.");}
  }
  static string RevisionPath(string name){return Path.Combine(Mod.DirectoryPath,name+".model-revision.txt");}
  static bool NeedsModelUpdate(string name){string path=RevisionPath(name);return !File.Exists(path)||File.ReadAllText(path).Trim()!=ModelRevision;}
  void Build(TrackPrefab original,TrackPrefab originalDouble,TrackPrefab originalOneway,StaticObjectPrefab imported,StaticObjectPrefab importedO,PrefabBase[] all){
   var stockPole=all.OfType<StaticObjectPrefab>().Single(p=>p.name=="TrainPowerPole01");
   // Unity's PrefabBase.Clone copies components and serialized nested configuration objects.
   var sourceJson=all.Where(p=>p is TrackPrefab||p is NetSectionPrefab||p is NetPiecePrefab||p is NetLanePrefab||p==stockPole||p==imported||p==importedO)
    .ToDictionary(p=>p,p=>Fingerprint(p));
   // An independent utility lane keeps the feeder clear of the contact wire and
   // gives it its own anchor. It uses cable geometry without dropper connectors.
   feederLane=(NetLanePrefab)all.OfType<NetLanePrefab>().Single(p=>p.name=="Train Contact Cable Lane").Clone("JPCatenary_Feeder_Cable_Lane");
   var feederUtility=feederLane.GetComponent<UtilityLane>();
   feederUtility.m_UtilityType=Game.Net.UtilityTypes.Catenary;
   feederUtility.m_Hanging=.006f;
   feederUtility.m_LocalConnectionLane=null;feederUtility.m_LocalConnectionLane2=null;feederUtility.m_NodeObject=null;
   registration.Add(feederLane);
   pole=(StaticObjectPrefab)imported.Clone("JPCatenary_I8m_NetworkPole");
   // The actual CS2 importer mirrors FBX X: saved RenderPrefab bounds are
   // [-0.820, +4.183], so the contact tip is +3.675 in game-local coordinates.
   var render=pole.m_Meshes[0].m_Mesh as RenderPrefab;
   if(render==null||render.bounds.max.x<4||render.bounds.min.x< -1)
    throw new InvalidOperationException("Unexpected imported pole orientation; refusing mismatched anchors.");
   pole.Remove<UIObject>();pole.Remove<PlaceableObject>();
   var standing=pole.AddOrGetComponent<StandingObject>();standing.m_LegSize=new float3(0);standing.m_LegGap=new float2(0);standing.m_CircularLeg=false;
   var anchors=pole.AddComponentFrom(stockPole.GetComponent<ObjectSubLanes>());
   foreach(var anchor in anchors.m_SubLanes){
    bool contact=anchor.m_LanePrefab.name=="Train Contact Cable Lane";
    var point=new float3(Reach,contact?ContactLocal:MessengerLocal,0);
    anchor.m_BezierCurve=new Bezier4x3(point,point,point,point);
   }
   var feederPoint=new float3(FeederLocalX,FeederLocalY,0);
   anchors.m_SubLanes=anchors.m_SubLanes.Concat(new[]{new ObjectSubLaneInfo {
    m_LanePrefab=feederLane,m_BezierCurve=new Bezier4x3(feederPoint,feederPoint,feederPoint,feederPoint),
    m_NodeIndex=new int2(2),m_ParentMesh=new int2(0)
   }}).ToArray();
   var utility=pole.AddComponentFrom(stockPole.GetComponent<UtilityObject>());utility.m_UtilityPosition=new float3(Reach,ContactLocal,0);
   registration.Add(pole);
   placementPole=pole;
   track=(TrackPrefab)original.Clone(Mod.TrackName);
   for(int i=0;i<track.m_Sections.Length;i++)track.m_Sections[i].m_Section=CopySection(track.m_Sections[i].m_Section);
   track.Remove<Unlockable>();track.Remove<UnlockOnBuild>();
   var ui=track.AddOrGetComponent<UIObject>();ui.m_Priority=9500;ui.m_IsDebugObject=false;
   if(ui.m_Group==null)throw new InvalidOperationException("Vanilla train menu category was unavailable.");
   registration.Add(track);
   // Keep the existing single-track IDs and configuration. Shared cloned shoulders
   // have already had vanilla poles removed; each double-track lane gets its own pole.
   var runningSections=originalDouble.m_Sections.Where(s=>s.m_Section.name=="Train Track Section 4").ToArray();
   if(runningSections.Length!=2||runningSections.Count(s=>s.m_Invert)!=1)
    throw new InvalidOperationException("Unexpected vanilla double-track section layout.");
   doubleTrack=(TrackPrefab)originalDouble.Clone(Mod.DoubleTrackName);
   for(int i=0;i<doubleTrack.m_Sections.Length;i++)doubleTrack.m_Sections[i].m_Section=CopySection(doubleTrack.m_Sections[i].m_Section);
   ConfigureDoubleTrackFeeders(doubleTrack);
   doubleTrack.Remove<Unlockable>();doubleTrack.Remove<UnlockOnBuild>();
   var doubleUi=doubleTrack.AddOrGetComponent<UIObject>();doubleUi.m_Priority=9510;doubleUi.m_IsDebugObject=false;
   if(doubleUi.m_Group==null)throw new InvalidOperationException("Double-track menu category was unavailable.");
   if(laneCount<2||poleCount!=2)throw new InvalidOperationException("Expected single and double-track lane/pole configurations.");
   registration.Add(doubleTrack);
   var oRender=importedO.m_Meshes[0].m_Mesh as RenderPrefab;
   if(oRender==null||oRender.bounds.max.x<5||oRender.bounds.min.x< -1||math.abs(oRender.bounds.max.y-8)>.01f)
    throw new InvalidOperationException("Unexpected O-pole imported bounds.");
   alternatingI=(StaticObjectPrefab)pole.Clone("JPCatenary_IO_I8m_NetworkPole");
   alternatingO=(StaticObjectPrefab)pole.Clone("JPCatenary_IO_O8m_NetworkPole");
   // Both models use identical contact datums and components. Only geometry differs.
   alternatingO.m_Meshes=importedO.m_Meshes;
   registration.Add(alternatingI);registration.Add(alternatingO);
   copies.Clear();copyPrefix="JPCPA_";placementPole=alternatingI;
   alternatingTrack=(TrackPrefab)originalDouble.Clone(Mod.AlternatingTrackName);
   for(int i=0;i<alternatingTrack.m_Sections.Length;i++)alternatingTrack.m_Sections[i].m_Section=CopySection(alternatingTrack.m_Sections[i].m_Section);
   ConfigureDoubleTrackFeeders(alternatingTrack);
   alternatingTrack.Remove<Unlockable>();alternatingTrack.Remove<UnlockOnBuild>();
   var alternatingUi=alternatingTrack.GetComponent<UIObject>();alternatingUi.m_Priority=9520;alternatingUi.m_IsDebugObject=false;
   registration.Add(alternatingTrack);
   singleLayout=true;
   alternatingSingleTrack=BuildAlternatingSingle(original,Mod.AlternatingSingleName,"JPCPAT_",9530);
   alternatingOnewayTrack=BuildAlternatingSingle(originalOneway,Mod.AlternatingOnewayName,"JPCPAO_",9540);
   foreach(var iconTrack in registration.OfType<TrackPrefab>())TrackIcons.Apply(iconTrack);
   foreach(var entry in sourceJson)if(Fingerprint(entry.Key)!=entry.Value)throw new InvalidOperationException("Source mutation detected: "+entry.Key.name);
   foreach(var prefab in registration)if(!system.AddPrefab(prefab))throw new InvalidOperationException("Prefab registration rejected: "+prefab.name);
   Mod.Log.Info("REGISTERED five tracks including "+Mod.AlternatingSingleName+" + "+Mod.AlternatingOnewayName+"; prefabs="+registration.Count+" lanes="+laneCount+" polePieces="+poleCount+" contactY="+ContactY+" poleY="+PoleY+"; source fingerprints unchanged.");
  }
  TrackPrefab BuildAlternatingSingle(TrackPrefab source,string name,string prefix,int priority){
   copies.Clear();copyPrefix=prefix;
   int polesBefore=poleCount;
   var result=(TrackPrefab)source.Clone(name);
   if(name==Mod.AlternatingOnewayName)result.m_InvertMode=CatenaryPlacement.SingleOnewayComposition;
   for(int i=0;i<result.m_Sections.Length;i++)result.m_Sections[i].m_Section=CopySection(result.m_Sections[i].m_Section);
   result.Remove<Unlockable>();result.Remove<UnlockOnBuild>();
   var ui=result.GetComponent<UIObject>();if(ui==null||ui.m_Group==null)throw new InvalidOperationException("Single-track menu missing: "+name);
   ui.m_Priority=priority;ui.m_IsDebugObject=false;
   if(poleCount!=polesBefore+1)throw new InvalidOperationException("Expected one single-track pole-bearing piece: "+name);
   registration.Add(result);return result;
  }
  static string Fingerprint(PrefabBase p){return JsonUtility.ToJson(p)+string.Join("|",p.components.Select(c=>JsonUtility.ToJson(c)));}
  void ConfigureDoubleTrackFeeders(TrackPrefab target){
   // LaneSystem.CreateEdgeLanes adds AuxiliaryNetLane.position.xy directly to
   // NetCompositionLane.position.xy. Section inversion flips the latter and
   // the pole objects, but NOT the auxiliary offset. Reusing one lane prefab on
   // both sides therefore put feeders at +6.335 and +2.335 instead of +/-6.335.
   var before=registration.ToDictionary(p=>p,p=>Fingerprint(p));
   int changed=0;
   foreach(var section in target.m_Sections){
    if(section.m_Section.name!=copyPrefix+"Train Track Section 4")continue;
    section.m_Section=ConfigureFeederSection(section.m_Section,section.m_Invert,new Dictionary<PrefabBase,PrefabBase>(),ref changed);
   }
   if(changed==0)throw new InvalidOperationException("No double-track feeder configuration found: "+target.name);
   foreach(var entry in before)if(Fingerprint(entry.Key)!=entry.Value)throw new InvalidOperationException("Mirroring changed an existing prefab: "+entry.Key.name);
   Mod.Log.Info("FEEDER_SIDES "+target.name+" left=-6.335 right=6.335 configuredLanePrefabs="+changed);
  }
  NetSectionPrefab ConfigureFeederSection(NetSectionPrefab source,bool leftSide,Dictionary<PrefabBase,PrefabBase> mirrored,ref int changed){
   PrefabBase found;if(mirrored.TryGetValue(source,out found))return (NetSectionPrefab)found;
   string suffix=leftSide?"_FeederLeft":"_FeederRight";
   var result=(NetSectionPrefab)source.Clone(source.name+suffix);mirrored.Add(source,result);
   if(result.m_SubSections!=null)foreach(var sub in result.m_SubSections)sub.m_Section=ConfigureFeederSection(sub.m_Section,leftSide,mirrored,ref changed);
   if(result.m_Pieces!=null)foreach(var piece in result.m_Pieces){
    var pieceSource=piece.m_Piece;
    if(pieceSource==null||pieceSource.GetComponent<NetPieceLanes>()==null)continue;
    if(!mirrored.TryGetValue(pieceSource,out found)){
     var pieceCopy=(NetPiecePrefab)pieceSource.Clone(pieceSource.name+suffix);mirrored.Add(pieceSource,pieceCopy);
     foreach(var lane in pieceCopy.GetComponent<NetPieceLanes>().m_Lanes){
      var laneSource=lane.m_Lane;
      var aux=laneSource==null?null:laneSource.GetComponent<AuxiliaryLanes>();
      if(aux==null||!aux.m_AuxiliaryLanes.Any(a=>a.m_Lane==feederLane))continue;
      PrefabBase laneCopy;
      if(!mirrored.TryGetValue(laneSource,out laneCopy)){
       laneCopy=laneSource.Clone(laneSource.name+suffix);mirrored.Add(laneSource,laneCopy);
       foreach(var cable in laneCopy.GetComponent<AuxiliaryLanes>().m_AuxiliaryLanes.Where(a=>a.m_Lane==feederLane)){
        if(math.abs(math.abs(cable.m_Position.x)-(Reach-FeederLocalX))>.0001f)throw new InvalidOperationException("Unexpected feeder offset magnitude: "+laneSource.name);
        // Station/intersection variants can use Twoway lanes initially set to -X.
        // Set the target side explicitly; lane travel direction does not define it.
        cable.m_Position.x=(leftSide?-1:1)*(Reach-FeederLocalX);
       }
       changed++;registration.Add(laneCopy);
      }
      lane.m_Lane=(NetLanePrefab)laneCopy;
     }
     registration.Add(pieceCopy);found=pieceCopy;
    }
    piece.m_Piece=(NetPiecePrefab)found;
   }
   registration.Add(result);return result;
  }
  NetSectionPrefab CopySection(NetSectionPrefab src){
   if(src==null)return null;PrefabBase found;if(copies.TryGetValue(src,out found))return (NetSectionPrefab)found;
   var dst=(NetSectionPrefab)src.Clone(copyPrefix+src.name);copies[src]=dst;
   if(dst.m_SubSections!=null)foreach(var sub in dst.m_SubSections)sub.m_Section=CopySection(sub.m_Section);
   if(dst.m_Pieces!=null)foreach(var piece in dst.m_Pieces)piece.m_Piece=CopyPiece(piece.m_Piece);
   registration.Add(dst);return dst;
  }
  NetPiecePrefab CopyPiece(NetPiecePrefab src){
   if(src==null)return null;PrefabBase found;if(copies.TryGetValue(src,out found))return (NetPiecePrefab)found;
   var dst=(NetPiecePrefab)src.Clone(copyPrefix+src.name);copies[src]=dst;
   var objects=dst.GetComponent<NetPieceObjects>();
   if(objects!=null&&objects.m_PieceObjects!=null)objects.m_PieceObjects=objects.m_PieceObjects.Where(x=>x.m_Object==null||x.m_Object.name!="TrainPowerPole01").ToArray();
   var lanes=dst.GetComponent<NetPieceLanes>();
   bool carriesTrain=false;
   if(lanes!=null&&lanes.m_Lanes!=null)foreach(var item in lanes.m_Lanes){
    if(item.m_Lane!=null&&item.m_Lane.GetComponent<AuxiliaryLanes>()!=null){item.m_Lane=CopyLane(item.m_Lane);carriesTrain=true;}
   }
   bool doublePiece=src.name=="Train Track Piece 4";
   if(carriesTrain && (src.name=="Train Track Twoway Piece 4"||doublePiece)){
    // The un-inverted double-track section sits on the right. The new single
    // tracks instead keep their one pole on -X, irrespective of travel direction.
    bool rightSide=doublePiece&&!singleLayout;
    var poleX=rightSide?Reach:-Reach;
    var poleRotation=rightSide?quaternion.RotateY(math.PI):quaternion.identity;
    var contact=new float3(poleX,PoleY,0)+math.mul(poleRotation,new float3(Reach,ContactLocal,0));
    if(math.distance(contact,new float3(0,ContactY,0))>.0001f)
     throw new InvalidOperationException("Pole anchor does not meet the track center: "+src.name);
    objects=dst.AddOrGetComponent<NetPieceObjects>();
    var list=objects.m_PieceObjects==null?new List<NetPieceObjectInfo>():objects.m_PieceObjects.ToList();
    list.Add(new NetPieceObjectInfo {
     m_Object=placementPole,m_Position=new float3(poleX,PoleY,0),m_Offset=new float3(0),m_Rotation=poleRotation,
     m_Probability=100,m_MinLength=8,m_CurveOffsetRange=new float2(.5f),m_Spacing=new float3(8,0,50),m_UseCurveRotation=new float2(0,1),
     m_FlipWhenInverted=true,m_EvenSpacing=false,m_SpacingOverride=false
    });
     // Vanilla separates ordinary spans from nodes. On a dead-end cap,
     // midpoint/even spacing put one pole on the cap's turning curve.
     // Use its node placement pattern so the two track sides keep separate anchors.
     list.Add(new NetPieceObjectInfo {
      m_Object=placementPole,m_Position=new float3(poleX,PoleY,0),m_Offset=new float3(0),m_Rotation=poleRotation,
      m_Probability=100,m_MinLength=0,m_CurveOffsetRange=new float2(0),m_Spacing=new float3(8,0,50),m_UseCurveRotation=new float2(0,1),
      m_FlipWhenInverted=true,m_EvenSpacing=false,m_SpacingOverride=false
     });
    CatenaryPlacement.Span(list[list.Count-2]);
    CatenaryPlacement.Endpoint(list[list.Count-1]);
    if(copyPrefix=="JPCPAO_"){
     CatenaryPlacement.SingleOnewayLeft(list[list.Count-2],Reach,PoleY);
     CatenaryPlacement.SingleOnewayLeft(list[list.Count-1],Reach,PoleY);
    }
    objects.m_PieceObjects=list.ToArray();poleCount++;
   }
   registration.Add(dst);return dst;
  }
  NetLanePrefab CopyLane(NetLanePrefab src){
   PrefabBase found;if(copies.TryGetValue(src,out found))return (NetLanePrefab)found;
   var dst=(NetLanePrefab)src.Clone(copyPrefix+src.name);copies[src]=dst;
   var aux=dst.GetComponent<AuxiliaryLanes>();
   foreach(var item in aux.m_AuxiliaryLanes){
    if(item.m_Lane.name=="Train Contact Cable Lane")item.m_Position.y=ContactY;
    else if(item.m_Lane.name=="Train Catenary Cable Lane")item.m_Position.y=MessengerY;
   }
   bool leftPole=singleLayout||src.name=="Twoway Train Lane 4";
   float feederX=(leftPole?-1:1)*(Reach-FeederLocalX);
   aux.m_AuxiliaryLanes=aux.m_AuxiliaryLanes.Concat(new[]{new AuxiliaryLaneInfo {
    m_Lane=feederLane,m_Position=new float3(feederX,FeederY,0),
    m_RequireAll=new NetPieceRequirements[0],
    m_RequireAny=new[]{NetPieceRequirements.Edge,NetPieceRequirements.Intersection,NetPieceRequirements.DeadEnd,NetPieceRequirements.LevelCrossing},
    m_RequireNone=CatenaryPlacement.WireExclusions(),
    m_Spacing=new float3(0,0,50),m_EvenSpacing=true,m_FindAnchor=true
   }}).ToArray();
   Mod.Log.Info("FEEDER_LANE "+dst.name+" x="+feederX+" y="+FeederY+" poleAnchor="+new float3(FeederLocalX,FeederLocalY,0));
   laneCount++;registration.Add(dst);return dst;
  }
  void LinkMenu(){
   if(linked||GameManager.instance.isGameLoading)return;
   if(!LinkTrackMenu(track)||!LinkTrackMenu(doubleTrack)||!LinkTrackMenu(alternatingTrack)||!LinkTrackMenu(alternatingSingleTrack)||!LinkTrackMenu(alternatingOnewayTrack))return;
   linked=true;
  }
  bool LinkTrackMenu(TrackPrefab target){
   Entity entity;if(!system.TryGetEntity(target,out entity))return false;
   if(!Ready(entity))return false;
   var ui=target.GetComponent<UIObject>();Entity group;if(!system.TryGetEntity(ui.m_Group,out group))return false;
   var method=typeof(EntityManager).GetMethods().Single(m=>m.Name=="GetBuffer"&&m.IsGenericMethodDefinition&&m.GetParameters().Length==2&&m.GetParameters()[0].ParameterType==typeof(Entity));
   var buffer=method.MakeGenericMethod(typeof(UIGroupElement)).Invoke(EntityManager,new object[]{group,true});
   var type=buffer.GetType();int count=(int)type.GetProperty("Length").GetValue(buffer,null);bool exists=false;
   for(int i=0;i<count;i++)if(((UIGroupElement)type.GetProperty("Item").GetValue(buffer,new object[]{i})).m_Prefab==entity)exists=true;
   if(!exists)ui.m_Group.AddElement(EntityManager,entity);
   Mod.Log.Info("MENU_READY "+target.name+" entity="+entity+" group="+ui.m_Group.name);return true;
  }
  bool Ready(Entity entity){
   var net=EntityManager.GetComponentData<NetData>(entity);
   var geo=EntityManager.GetComponentData<NetGeometryData>(entity);
   var objectData=EntityManager.GetComponentData<ObjectData>(system.GetEntity(pole));
   bool ready=net.m_NodeArchetype.Valid&&net.m_EdgeArchetype.Valid&&geo.m_NodeCompositionArchetype.Valid&&geo.m_EdgeCompositionArchetype.Valid&&objectData.m_Archetype.Valid&&geo.m_DefaultWidth>0;
   string detail="node="+net.m_NodeArchetype.Valid+" edge="+net.m_EdgeArchetype.Valid+" nodeComposition="+geo.m_NodeCompositionArchetype.Valid+" edgeComposition="+geo.m_EdgeCompositionArchetype.Valid+" pole="+objectData.m_Archetype.Valid+" width="+geo.m_DefaultWidth;
   int checks;readinessChecks.TryGetValue(entity,out checks);readinessChecks[entity]=++checks;
   if(checks==1||ready)Mod.Log.Info("READINESS "+detail);
   if(!ready){if(checks>10)throw new InvalidOperationException("Network initialization incomplete: "+detail);return false;}
   foreach(var prefab in registration){
    var dependencies=new List<PrefabBase>();prefab.GetDependencies(dependencies);
    foreach(var component in prefab.components)component.GetDependencies(dependencies);
    foreach(var dependency in dependencies){Entity dep;if(dependency==null||!system.TryGetEntity(dependency,out dep)||!EntityManager.Exists(dep))throw new InvalidOperationException("Missing dependency of "+prefab.name+": "+(dependency==null?"null":dependency.name));}
   }
   Mod.Log.Info("DEPENDENCIES_READY "+registration.Count+" registered prefabs");return true;
  }
 }
}
