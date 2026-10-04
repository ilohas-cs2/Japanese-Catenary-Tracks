namespace JPCatenaryPrototype {
 // Polling does not depend on FPS. Only a changed railway snapshot schedules
 // reconciliation; one follow-up lets native lane/batch updates settle.
 public sealed class CatenaryWorkGate {
  double nextPoll;
  ulong previous;
  bool observed;
  int pending;
  public void Reset(){nextPoll=0;previous=0;observed=false;pending=0;}
  public bool PollDue(double now){if(now<nextPoll)return false;nextPoll=now+.25;return true;}
  public bool Observe(bool hasTracks,ulong snapshot){
   if(!hasTracks){observed=false;pending=0;return false;}
   if(!observed||snapshot!=previous){observed=true;previous=snapshot;pending=2;}
   if(pending==0)return false;
   --pending;return true;
  }
 }
}
