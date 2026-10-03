using System;
namespace JPCatenaryPrototype {
 public static class MixedJoinPolicy {
  // Initial scene discovery gives all unrecorded edges the same order.
  // Never pretend entity indices describe historical build chronology.
  public static string Select(string a,long orderA,string b,long orderB,string previous){
   if(orderA>orderB)return a;
   if(orderB>orderA)return b;
   if(previous==a||previous==b)return previous;
   return string.CompareOrdinal(a,b)<=0?a:b;
  }
 }
}
