using Game.Prefabs;
namespace JPCatenaryPrototype {
 public static class CatenaryPlacement {
  // The stock track defaults to InvertLefthandTraffic. Its automatic section
  // mirror also moves attached poles. One-way catenary instead follows the
  // user-selected travel direction equally in both map traffic settings.
  public const CompositionInvertMode SingleOnewayComposition=CompositionInvertMode.KeepOriginal;
  public static void SingleOnewayLeft(NetPieceObjectInfo item,float reach,float height){
   // The native one-way running section is forward (+Z). -X is its left.
   // Let the game invert the entire placement when travel is reversed, but
   // do not follow a terminal cap's turning tangent to the opposite side.
   item.m_Position=new Unity.Mathematics.float3(-reach,height,0);
   item.m_Rotation=Unity.Mathematics.quaternion.identity;
   item.m_FlipWhenInverted=true;
   bool node=System.Array.IndexOf(item.m_RequireAll,NetPieceRequirements.Node)>=0;
   item.m_UseCurveRotation=new Unity.Mathematics.float2(0,node?0:1);
  }
  public static void Span(NetPieceObjectInfo item){
   // CS2 EvenSpacing subtracts one from the rounded span count because it
   // expects node-owned supports. We omit ordinary node supports, so use
   // cell-centered placement (CurveOffsetRange=.5) without that subtraction.
   item.m_EvenSpacing=false;
   // A transition node may have two half-compositions. Repeating-span
   // placement must not add one midpoint pole to each of those halves.
   item.m_RequireAll=new[]{NetPieceRequirements.Edge};
   item.m_RequireAny=new NetPieceRequirements[0];
   item.m_RequireNone=new[]{NetPieceRequirements.Node,NetPieceRequirements.Tunnel,NetPieceRequirements.Underground,NetPieceRequirements.Intersection,NetPieceRequirements.DeadEnd,NetPieceRequirements.LevelCrossing};
  }
  public static void Endpoint(NetPieceObjectInfo item){
   item.m_RequireAll=new[]{NetPieceRequirements.Node};
   // GetNodeFlags also marks a degree-two node as Intersection when the
   // connected track prefabs differ. Each family then creates its own
   // support at that join. Leave junction/type-change nodes unsupported
   // (as ordinary transition nodes already are); retain true terminals
   // and the existing level-crossing policy.
   item.m_RequireAny=new[]{NetPieceRequirements.DeadEnd,NetPieceRequirements.LevelCrossing};
   item.m_RequireNone=new[]{NetPieceRequirements.Tunnel,NetPieceRequirements.Underground};
  }
  public static void PortalOrientation(NetPieceObjectInfo item){
   // SecondaryObjectSystem interpolates from the incoming tangent to the
   // placement curve tangent. The latter turns around a dead-end cap.
   // Portal crossbeams should keep the incoming track frame at nodes;
   // interior spans still follow their local curve tangent.
   bool node=System.Array.IndexOf(item.m_RequireAll,NetPieceRequirements.Node)>=0;
   item.m_UseCurveRotation=new Unity.Mathematics.float2(0,node?0:1);
  }
  public static void MixedJoinEndpoint(NetPieceObjectInfo item){
   // Expansion nodes are reconciled across families at runtime. Keep both
   // native anchor sets, but display only the selected support family.
   if(System.Array.IndexOf(item.m_RequireAll,NetPieceRequirements.Node)>=0)
    item.m_RequireAny=new[]{NetPieceRequirements.Intersection,NetPieceRequirements.DeadEnd,NetPieceRequirements.LevelCrossing};
  }
  public static void FeederSpan(AuxiliaryLaneInfo item){
   // LaneSystem's non-even branch uses (i+.5)*length/count, exactly as
   // the cell-centered supports. EvenSpacing uses a different count/grid.
   item.m_EvenSpacing=false;
  }
  public static void RailWireSpan(AuxiliaryLaneInfo item,bool portal){
   FeederSpan(item);
   // Portal anchors are resolved by support row after native generation.
   // Native FindAnchor consumes terminal anchors while iterating all spans,
   // which can pull an interior endpoint back to the far terminal.
   if(portal)item.m_FindAnchor=false;
  }
  // Auxiliary lanes accept composition flags only. Underground is a section
  // flag and must not be passed here; Tunnel is the supported tunnel filter.
  public static NetPieceRequirements[] WireExclusions(){return new[]{NetPieceRequirements.Tunnel};}
 }
}
