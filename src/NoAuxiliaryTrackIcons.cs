using System;
using System.Collections.Generic;
using Game.Prefabs;
namespace JPCatenaryPrototype {
 internal static class NoAuxiliaryTrackIcons {
  static readonly Dictionary<string,string> Icons=new Dictionary<string,string>(StringComparer.Ordinal){
   {"JPC2_GeneralIONoAux_Twoway_Track","assetdb://global/4593479b946a5cefadd2311eaafcabc1"},
   {"JPC2_GeneralIONoAux_Oneway_Track","assetdb://global/46e4bae49b2e553381a49d367bc3fdd2"},
   {"JPC2_GeneralIONoAux_Double_Track","assetdb://global/f5cafa5d060f5957a482c58e1e406b36"},
   {"JPC2_ConcreteIONoAux_Twoway_Track","assetdb://global/73b957a7633b50c3be8060aa53e25625"},
   {"JPC2_ConcreteIONoAux_Oneway_Track","assetdb://global/91c8de9d13bd5ac9b65621377627b749"},
   {"JPC2_ConcreteIONoAux_Double_Track","assetdb://global/3798e0dc9a5c5193b6b5a1a8f0a25462"},
   {"JPC2_HairpinABNoAux_Twoway_Track","assetdb://global/efe05af01bb7537193af2e35cf486da2"},
   {"JPC2_HairpinABNoAux_Oneway_Track","assetdb://global/e394b09ddcda5d7cbcca0cbd65ae937a"},
   {"JPC2_HairpinABNoAux_Double_Track","assetdb://global/7c6661dac7f45e218aa18f35e0d221f5"},
   {"JPC2_SteelTubeABNoAux_Double_Track","assetdb://global/dc499a464d70509582a97d9f7872148b"},
   {"JPC2_TrussABNoAux_Double_Track","assetdb://global/a9ad0732dd5e5324a45d80239e9a15c9"},
   {"JPC2_TrussDualABNoAux_Double_Track","assetdb://global/e69074c6160c502292d99c624a43b24b"},
   {"JPC2_FullSteelABNoAux_Double_Track","assetdb://global/3e9fa69834615c3a984319cf9285c57b"},
   {"JPC2_SingleFrameIONoAux_Twoway_Track","assetdb://global/6a85c6ca225352b28fc694f61f1d1ca5"},
   {"JPC2_SingleFrameIONoAux_Oneway_Track","assetdb://global/633b79b8bc1550f3ba47aafd9d3d3756"},
   {"JPC2_SingleFrameIONoAux_Double_Track","assetdb://global/4b7a9a8226135d9d84d5cb87ba67c95c"},
   {"JPC2_OriginalIONoAux_Double_Track","assetdb://global/4bda2b22692a51daa52163b4292fa059"},
   {"JPC2_ClassicIONoAux_Twoway_Track","assetdb://global/7e5146d8b5445781860f7deed4144e62"},
   {"JPC2_ClassicIONoAux_Oneway_Track","assetdb://global/448a1120d5bd51d0aea6e7ffddf8f4e6"},
   {"JPC2_ClassicIONoAux_Double_Track","assetdb://global/7acbe04b7bda54efaa57a66a58dbbd63"},
   {"JPC2_ClassicINoAux_Twoway_Track","assetdb://global/5e93d47fb8af5830a24de4ea1d792708"},
   {"JPC2_ClassicINoAux_Double_Track","assetdb://global/1bce5318e02954e0b34e56be8d614330"},
  };
  internal static void Apply(TrackPrefab track){
   string icon;if(!Icons.TryGetValue(track.name,out icon))throw new InvalidOperationException("No-auxiliary icon missing: "+track.name);
   track.GetComponent<UIObject>().m_Icon=icon;
  }
 }
}
