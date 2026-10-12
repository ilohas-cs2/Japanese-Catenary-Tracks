using System;
using System.Linq;
using System.Collections.Generic;
using Game.Prefabs;
using Unity.Mathematics;

namespace JPCatenaryPrototype {
 public partial class CatenarySystem {
  int noAuxiliaryTrackCount;
  void RegisterNoAuxBuilder(ExpansionBuilder builder,List<TrackPrefab> tracks,string key,bool portal,TrackPrefab paired=null){
   builder.Validate();
   foreach(var p in builder.added)if(!system.AddPrefab(p))throw new InvalidOperationException("No-auxiliary registration rejected: "+p.name);
   expansionPrefabs.AddRange(builder.added);expansionTracks.AddRange(tracks);noAuxiliaryTrackCount+=tracks.Count;
   expansionFamilies.Add(new AlternatingFamily{key=key,a=builder.networkPole,b=builder.alternatePole,tracks=tracks.ToArray(),pairedTrack=paired,portal=portal,commonWiring=true,noAuxiliary=true,projectionAnchor=new float3(portal?0:Reach,ContactLocal,0)});
  }
  void BuildNoAuxiliaryTracks(PrefabBase[] all,Dictionary<string,string> labels,ref int priority){
   if(!expansionCatalog.models.Any(m=>m.no_auxiliary))return;
   foreach(var pair in activePairs){
    string key=pair[0]+"NoAux";
    if(pair[0]!="OriginalIO"&&!expansionCatalog.models.Single(m=>m.key==pair[1]).portal){
     var a=expansionCatalog.models.Single(m=>m.key==pair[1]+"NoAux");var b=expansionCatalog.models.Single(m=>m.key==pair[2]+"NoAux");
     var builder=new ExpansionBuilder(key,a,b,all.OfType<StaticObjectPrefab>().Single(p=>p.name==a.name),all.OfType<StaticObjectPrefab>().Single(p=>p.name==b.name),pole,feederLane,commonUpperChannels);
     var tracks=new List<TrackPrefab>{builder.Build(alternatingSingleTrack,"Twoway",true,false,priority++,labels),builder.Build(alternatingOnewayTrack,"Oneway",true,false,priority++,labels)};
     RegisterNoAuxBuilder(builder,tracks,key+"Single",false);
    }
    var da=expansionCatalog.models.Single(m=>m.key==pair[1]+"DoubleNoAux");var db=expansionCatalog.models.Single(m=>m.key==pair[2]+"DoubleNoAux");
    var doubles=new ExpansionBuilder(key,da,db,all.OfType<StaticObjectPrefab>().Single(p=>p.name==da.name),all.OfType<StaticObjectPrefab>().Single(p=>p.name==db.name),pole,feederLane,commonUpperChannels);
    var dt=doubles.Build(doubleTrack,"Double",false,true,priority++,labels);
    RegisterNoAuxBuilder(doubles,new List<TrackPrefab>{dt},key,true);
   }
   // The original five menu entries predate the expansion families. Keep
   // each existing identity and add corresponding, separately named tracks.
   if(activePairs.Any(p=>p[0]=="OriginalIO")){
    var a=expansionCatalog.models.Single(m=>m.key=="OriginalINoAux");var b=expansionCatalog.models.Single(m=>m.key=="OriginalONoAux");
    var ia=all.OfType<StaticObjectPrefab>().Single(p=>p.name==a.name);var ib=all.OfType<StaticObjectPrefab>().Single(p=>p.name==b.name);
    var alternate=new ExpansionBuilder("ClassicIONoAux",a,b,ia,ib,pole,feederLane,commonUpperChannels);
    var tw=alternate.Build(alternatingSingleTrack,"Twoway",true,false,priority++,labels);
    var ow=alternate.Build(alternatingOnewayTrack,"Oneway",true,false,priority++,labels);
    var dt=alternate.Build(doubleTrack,"Double",false,true,priority++,labels);
    RegisterNoAuxBuilder(alternate,new List<TrackPrefab>{tw,ow,dt},"ClassicIONoAux",false,dt);
    var fixedA=FixedNoAuxModel(a,"FixedI");var fixedB=FixedNoAuxModel(a,"FixedIB");
    var fixedBuilder=new ExpansionBuilder("ClassicINoAux",fixedA,fixedB,ia,ia,pole,feederLane,commonUpperChannels);
    var st=fixedBuilder.Build(track,"Twoway",true,false,priority++,labels);
    var fd=fixedBuilder.Build(doubleTrack,"Double",false,true,priority++,labels);
    RegisterNoAuxBuilder(fixedBuilder,new List<TrackPrefab>{st,fd},"ClassicINoAux",false,fd);
   }
  }
  static ExpansionModel FixedNoAuxModel(ExpansionModel src,string suffix){return new ExpansionModel{key=src.key+suffix,name=src.name+suffix,portal=false,no_auxiliary=true,revision=src.revision,contacts=src.contacts,messengers=src.messengers,feeder_points=new ExpansionPoint[0],distribution_points=new ExpansionPoint[0]};}
 }
}
