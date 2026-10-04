namespace JPCatenaryPrototype {
 public static class CatenaryJunctionPolicy {
  // A crossing road does not branch the railway's overhead wiring. Keep
  // stock railway branches and unknown network types as blockers, however.
  public static bool CountsForWireDegree(bool customTrack,bool trainTrack,bool road){
   return customTrack||trainTrack||!road;
  }
 }
}
