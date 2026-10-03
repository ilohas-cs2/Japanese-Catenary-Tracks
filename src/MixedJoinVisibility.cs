using Unity.Entities;
using Colossal.Serialization.Entities;
namespace JPCatenaryPrototype {
 // Save ownership separately from the runtime selection, so changing the
 // winning family after a reload can restore only our own override flag.
 // A zero-payload tag must use the native empty-component serializer.
 public struct MixedJoinOverride : IComponentData,IEmptySerializable {}
 public static class MixedJoinVisibility {
  public static bool Apply(EntityManager manager,Entity pole,bool suppress){
   bool changed=false;
   bool selected=manager.HasComponent<MixedJoinHidden>(pole);
   bool owned=manager.HasComponent<MixedJoinOverride>(pole);
   // Leave temporary editor/tool Hidden flags alone. The native cleanup
   // releases any old non-persistent Hidden state after loading.
   if(suppress){
    if(!selected){manager.AddComponent<MixedJoinHidden>(pole);changed=true;}
    if(!manager.HasComponent<Game.Common.Overridden>(pole)){
     manager.AddComponent<Game.Common.Overridden>(pole);
     if(!owned)manager.AddComponent<MixedJoinOverride>(pole);
     changed=true;
    }
    // BatchesUpdated alone does not recompute the static object's layer
    // mask in InitializeCullingJob. Clear its render layers immediately;
    // Overridden keeps native full recalculations consistent with this.
    if(manager.HasComponent<Game.Rendering.CullingInfo>(pole)){
     var info=manager.GetComponentData<Game.Rendering.CullingInfo>(pole);
     if((info.m_Mask&Game.Common.BoundsMask.AllLayers)!=0){
      info.m_Mask&=~Game.Common.BoundsMask.AllLayers;
      manager.SetComponentData(pole,info);changed=true;
     }
    }
   }else{
    if(selected){manager.RemoveComponent<MixedJoinHidden>(pole);changed=true;}
    if(owned){
     if(manager.HasComponent<Game.Common.Overridden>(pole))manager.RemoveComponent<Game.Common.Overridden>(pole);
     manager.RemoveComponent<MixedJoinOverride>(pole);changed=true;
     // Restore the normal mask through native bounds/layer calculation.
     if(!manager.HasComponent<Game.Common.Updated>(pole))manager.AddComponent<Game.Common.Updated>(pole);
    }
   }
   // PreCulling publishes the new mask and refreshes render batches. Avoid
   // Updated while suppressing, since that reruns native overlap detection.
   if(changed&&!manager.HasComponent<Game.Common.BatchesUpdated>(pole))manager.AddComponent<Game.Common.BatchesUpdated>(pole);
   return changed;
  }
 }
 // Native overlap edits can revise Overridden in Modification5. Reapply our
 // selected suppression before PreCulling reads it, without per-frame writes
 // once the scene is unchanged. Includes redundant node feeder lanes; never
 // changes pole/line prefab identities, anchors or connection topology.
 public sealed class MixedJoinVisibilitySystem : Game.GameSystemBase {
  EntityQuery poles;
  protected override void OnCreate(){
   base.OnCreate();
   poles=GetEntityQuery(ComponentType.ReadOnly<MixedJoinHidden>(),ComponentType.Exclude<Game.Common.Deleted>(),ComponentType.Exclude<Game.Tools.Temp>());
   RequireForUpdate(poles);
  }
  protected override void OnUpdate(){
   using(var entities=poles.ToEntityArray(Unity.Collections.Allocator.Temp))foreach(var e in entities)MixedJoinVisibility.Apply(EntityManager,e,true);
  }
 }
}
