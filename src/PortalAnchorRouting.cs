using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
namespace JPCatenaryPrototype {
 public sealed class PortalAnchor {public Entity pole,prefab;public ushort index;public float3 point;}
 public static class PortalAnchorRouting {
  public static bool Refresh(ref Game.Pathfind.PathNode path,Entity prefab,ref float3 point,Dictionary<int,List<PortalAnchor>> byPole){
   List<PortalAnchor> candidates;if(!byPole.TryGetValue(path.GetOwnerIndex(),out candidates))return false;
   PortalAnchor best=null;float distance=float.MaxValue;
   foreach(var a in candidates){if(a.prefab!=prefab)continue;float d=math.distancesq(point,a.point);if(d<distance){distance=d;best=a;}}
   if(best==null)return false;
   var target=new Game.Pathfind.PathNode(best.pole,best.index);
   if(distance<=.000001f&&path.Equals(target))return false;
   // Include the owner in equality: coincident old/new supports must still
   // redirect topology. Matching by lane index alone kept the hidden owner.
   point=best.point;path=target;return true;
  }
 }
}
