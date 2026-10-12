namespace JPCatenaryPrototype {
 public static class AuxiliaryTerminationPolicy {
  // A wire is allowed up to a real equipped support, never to a bare row.
  // Keep this decision independent of contact/messenger wire topology.
  public static bool SuppressSpan(bool auxiliary,bool bareOwner,bool startBoundary,bool endBoundary,bool startSupported,bool endSupported){
   return auxiliary&&(bareOwner||(startBoundary&&!startSupported)||(endBoundary&&!endSupported));
  }
  public static bool TerminalBoundary(int equippedEdges,int bareEdges){return equippedEdges==1&&bareEdges>0;}
 }
}
