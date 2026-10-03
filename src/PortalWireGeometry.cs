using Unity.Mathematics;
using Colossal.Mathematics;
namespace JPCatenaryPrototype {
 public static class PortalWireGeometry {
  public static float3 SupportStation(float3 position,quaternion rotation,float3 contact){
   return position+math.mul(rotation,new float3(contact.x,0,contact.z));
  }
  public static bool NextReverse(bool current,bool currentAtStart,bool nextAtStart){return current^(currentAtStart==nextAtStart);}
  public static bool SameCurve(Bezier4x3 a,Bezier4x3 b){return math.distancesq(a.a,b.a)<.000001f&&math.distancesq(a.b,b.b)<.000001f&&math.distancesq(a.c,b.c)<.000001f&&math.distancesq(a.d,b.d)<.000001f;}
  public static bool JoinAtNode(float3 a,float3 b,float3 node,float hanging,out Bezier4x3 left,out Bezier4x3 right){
   left=right=default(Bezier4x3);var direction=(b-a).xz;float length=math.lengthsq(direction);
   if(length<.01f)return false;
   float t=math.dot((node-a).xz,direction)/length;
   if(!math.isfinite(t)||t<=.01f||t>=.99f)return false;
   var whole=Reanchor(default(Bezier4x3),a,b,hanging);
   left=MathUtils.Cut(whole,new float2(0,t));right=MathUtils.Cut(whole,new float2(t,1));return true;
  }
  public static float3 EdgePoint(Game.Net.EdgeGeometry geometry,float delta){
   var part=delta<=.5f?geometry.m_Start:geometry.m_End;
   float t=delta<=.5f?delta*2:(delta-.5f)*2;
   return MathUtils.Position(MathUtils.Lerp(part.m_Left,part.m_Right,.5f),math.saturate(t));
  }
  public static int NearestSupport(float3 expected,float3[] centers,float maxDistance){
   int best=-1;float distance=maxDistance*maxDistance;
   for(int i=0;i<centers.Length;i++){
    float d=math.lengthsq((centers[i]-expected).xz);
    if(d<=distance){best=i;distance=d;}
   }
   return best;
  }
  // Preserve the native node frame, including its perpendicular crossbeam.
  // Only disambiguate its 180-degree handedness using a nearby span on the
  // same directed edge. Compare tangents locally so curved edges are safe.
  public static bool NeedsHalfTurn(quaternion node,float3 nodeTangent,quaternion span,float3 spanTangent){
   float a=math.dot(math.mul(node,new float3(0,0,1)).xz,nodeTangent.xz);
   float b=math.dot(math.mul(span,new float3(0,0,1)).xz,spanTangent.xz);
   return a*b<-.00001f;
  }
  public static Bezier4x3 Reanchor(Bezier4x3 old,float3 start,float3 end,float hanging){
   if(hanging<=0)return new Bezier4x3(start,old.b+start-old.a,old.c+end-old.d,end);
   var b=math.lerp(start,end,1f/3f);var c=math.lerp(start,end,2f/3f);
   float drop=math.distance(start.xz,end.xz)*hanging*(4f/3f);b.y-=drop;c.y-=drop;
   return new Bezier4x3(start,b,c,end);
  }
 }
}
