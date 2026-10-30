using System;
using System.Reflection;
namespace HarmonyLib { class HarmonyPatch:Attribute { public HarmonyPatch(Type t,string m) {} public HarmonyPatch(Type t,string m,Type[] a) {} } }
namespace GameCore.HotUpdate { class WebGm { internal static bool LockExploreExposure; } }
namespace GameCore.HotUpdate.Battle.Logic {
 class ExploreManager {}
 class GameTimeManager { internal float Speed=1; internal void GmSetCustomGameSpeed(float speed) {Speed=speed;} }
 class BattleLogicWorld { internal static bool IsInstanceCreated=true; internal static BattleLogicWorld Instance=new BattleLogicWorld(); internal GameTimeManager _GameTimeManager=new GameTimeManager(); }
}
namespace SurvivalLogTrainer {
 public sealed partial class Plugin {
  internal static float MoveMultiplier=1,TimeMultiplier=1; internal static int SkillMultiplier=1;
  internal static bool InfiniteStamina,InfiniteSatiety,InfiniteMorale,InfiniteShelfLife,InfiniteUses,InfiniteDurability,InfiniteGeneration,InfiniteBattery,InfiniteGeneratorFuel,InfiniteCookingFuel,InstantPlants,ZeroTravelTime;
  internal static string B(bool v)=>v?"1":"0";
 }
 class Test {
  static void Check(bool ok,string message) {if(!ok) throw new Exception(message);}
  static void Main() {
   Plugin.PersistAllSettings=true;
   Plugin.MoveMultiplier=20;Plugin.TimeMultiplier=.25f;Plugin.SkillMultiplier=10;
   var names=(string[])typeof(Plugin).GetField("FixedBooleanFields",BindingFlags.NonPublic|BindingFlags.Static).GetValue(null);
   foreach(var name in names) typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).SetValue(null,true);
   var saved=Plugin.SerializeExtraSettings();
   Plugin.LoadExtraSettings("",true);
   Check(!Plugin.PersistAllSettings && Plugin.MoveMultiplier==1 && Plugin.TimeMultiplier==1,"save isolation");
   foreach(var name in names) Check(!(bool)typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"clear "+name);
   Plugin.LoadExtraSettings(saved,true);
   Check(Plugin.MoveMultiplier==20 && Plugin.TimeMultiplier==.25f && Plugin.SkillMultiplier==10,"multipliers");
   foreach(var name in names) Check((bool)typeof(Plugin).GetField(name,BindingFlags.NonPublic|BindingFlags.Static).GetValue(null),"restore "+name);
   Check(GameCore.HotUpdate.Battle.Logic.BattleLogicWorld.Instance._GameTimeManager.Speed==.25f,"native time application");
   Check(!saved.Contains("ADD_") && !saved.Contains("UNLOCK"),"no command replay");
   Plugin.LoadExtraSettings("persist=1\nbag_width=30\n",false);
   Check(!Plugin.PersistAllSettings && Plugin.MoveMultiplier==1,"legacy config compatibility");
   Plugin.LoadExtraSettings("persist_all=1\nfixed_move=NaN\nfixed_time=Infinity\nfixed_skill=99999",true);
   Check(Plugin.MoveMultiplier==1 && Plugin.TimeMultiplier==1 && Plugin.SkillMultiplier==1,"invalid values rejected");
   var prefix=typeof(ExploreLockPatch).GetMethod("Prefix",BindingFlags.Static|BindingFlags.NonPublic);
   var finalizer=typeof(ExploreLockPatch).GetMethod("Finalizer",BindingFlags.Static|BindingFlags.NonPublic);
   Plugin.NoExploreExposure=true;
   object[] args={false};prefix.Invoke(null,args);
   Check(GameCore.HotUpdate.WebGm.LockExploreExposure,"native exposure lock applied");
   finalizer.Invoke(null,args);
   Check(!GameCore.HotUpdate.WebGm.LockExploreExposure,"native lock restored");
   GameCore.HotUpdate.WebGm.LockExploreExposure=true;prefix.Invoke(null,args);finalizer.Invoke(null,args);
   Check(GameCore.HotUpdate.WebGm.LockExploreExposure,"existing native lock preserved");
   Console.WriteLine("PASS: all persistent toggles, multipliers, save isolation, legacy config, invalid input, no one-shot replay.");
  }
 }
}
