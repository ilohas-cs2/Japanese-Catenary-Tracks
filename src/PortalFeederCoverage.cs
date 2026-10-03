using Unity.Mathematics;
namespace JPCatenaryPrototype {
 public static class PortalFeederCoverage {
  // Require one live span from each incident edge, ending at the same
  // selected insulator. Ambiguity or a missing span must retain the connector.
  public static bool Complete(float3 support,float3[] first,float3[] second){
   return Complete(new[]{support},first,second);
  }
  // Contact and messenger prefabs each have two supports. Each edge must
  // cover BOTH physical rails bijectively; two endpoints on one rail do not
  // constitute coverage of the other rail. Enumeration/direction is irrelevant.
  public static bool Complete(float3[] supports,float3[] first,float3[] second){
   return supports.Length>0&&Covers(supports,first)&&Covers(supports,second);
  }
  static bool Covers(float3[] supports,float3[] ends){
   if(ends.Length!=supports.Length)return false;
   var used=new bool[ends.Length];
   foreach(var support in supports){
    if(!math.all(math.isfinite(support)))return false;
    int found=-1;
    for(int i=0;i<ends.Length;i++){
     if(!math.all(math.isfinite(ends[i])))return false;
     if(math.distancesq(support,ends[i])>.0001f)continue;
     if(found>=0||used[i])return false;
     found=i;
    }
    if(found<0)return false;used[found]=true;
   }
   return true;
  }
 }
}
