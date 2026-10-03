using System;
using System.Collections.Generic;
using Game.Prefabs;
namespace JPCatenaryPrototype {
 internal static class TrackIcons {
  static readonly Dictionary<string,string> Icons=new Dictionary<string,string>(StringComparer.Ordinal){
   {"JPC2_GeneralIO_Twoway_Track","assetdb://global/9aef877b83265b42ab51c675372a4d94"},
   {"JPC2_GeneralIO_Oneway_Track","assetdb://global/259fe1001c3f5d07a517cee154d5dbcc"},
   {"JPC2_GeneralIO_Double_Track","assetdb://global/6089ff62b7025da68c2646d9d0d5e1c6"},
   {"JPC2_ConcreteIO_Twoway_Track","assetdb://global/80219fc8f8065a878f45eba1daf904da"},
   {"JPC2_ConcreteIO_Oneway_Track","assetdb://global/f069bb02d99f5a9388a2129bdd564a34"},
   {"JPC2_ConcreteIO_Double_Track","assetdb://global/61f85adbbb0b51a2b8624ea49d32d9e4"},
   {"JPC2_HairpinAB_Twoway_Track","assetdb://global/64e8e3079bf152fe902bba4b676b0332"},
   {"JPC2_HairpinAB_Oneway_Track","assetdb://global/eea7154107b0542095f18747420c3c66"},
   {"JPC2_HairpinAB_Double_Track","assetdb://global/38b45c092328539592372c4aba34b2aa"},
   {"JPC2_SteelTubeAB_Double_Track","assetdb://global/7f54488d23be5fac8ee8d51c1e3309fa"},
   {"JPC2_TrussAB_Double_Track","assetdb://global/fd861e7a44fb5a5392e4e707037cd2f1"},
   {"JPC2_TrussDualAB_Double_Track","assetdb://global/96e5a54cd2ef506ea8bf06952b868661"},
   {"JPC2_FullSteelAB_Double_Track","assetdb://global/8cc82ba8f5e7561f890518030e42c5ba"},
   {"JPC2_SingleFrameIO_Twoway_Track","assetdb://global/d7e28eb093e8570fa029f01cfba4b2d0"},
   {"JPC2_SingleFrameIO_Oneway_Track","assetdb://global/0db98f87b1c85946a5d93fcb6d90d854"},
   {"JPC2_SingleFrameIO_Double_Track","assetdb://global/ecd1cd361cc855158c0e8c6398a33ec7"},
   {"JPC2_OriginalIO_Double_Track","assetdb://global/b79cd399fd675194a5734a2eee1904f4"},
   {"JPCatenary_SingleTrack_5m_Prototype","assetdb://global/d0b62377f404559c957220e7d58d8027"},
   {"JPCatenary_DoubleTrack_5m_Prototype","assetdb://global/60aa93e53d095df6a111bb9b82c59e68"},
   {"JPCatenary_DoubleTrack_IO_5m_Prototype","assetdb://global/c4a575be61e65fbca4a5292676b3fea7"},
   {"JPCatenary_SingleTrack_Twoway_IO_5m_Prototype","assetdb://global/5b9e91a5c80e595fb6049bd96a40c626"},
   {"JPCatenary_SingleTrack_Oneway_IO_5m_Prototype","assetdb://global/737d7bb029185e9d8452b3076ef49493"},
  };
  internal static void Apply(TrackPrefab track){
   string icon;
   if(!Icons.TryGetValue(track.name,out icon))throw new InvalidOperationException("Track icon mapping missing: "+track.name);
   track.GetComponent<UIObject>().m_Icon=icon;
   Mod.Log.Info("TRACK_ICON_ASSIGNED "+track.name+" "+icon);
  }
 }
}
