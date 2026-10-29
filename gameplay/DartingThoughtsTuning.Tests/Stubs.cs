using System.Reflection;
namespace BepInEx {
 public sealed class BepInPlugin:Attribute {public BepInPlugin(string a,string b,string c){}}
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)] public sealed class BepInDependency:Attribute {public BepInDependency(string a){} public BepInDependency(string a,string b){}}
 public class BaseUnityPlugin {public Log Logger=new();public sealed class Log {public void LogInfo(string s){}}}
}
namespace BepInEx.Bootstrap {public static class Chainloader {public static Dictionary<string,Info> PluginInfos=new();public sealed class Info {public Meta Metadata=new();}public sealed class Meta {public System.Version Version=new(1,21,4);}}}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)] public sealed class HarmonyPatch:Attribute {public HarmonyPatch(Type t,string n){} public HarmonyPatch(){}}
 public sealed class HarmonyPriority:Attribute {public HarmonyPriority(int p){}}
 public static class Priority {public const int Last=0;}
 public sealed class Harmony {public Harmony(string s){}public void PatchAll(Assembly a){}public void UnpatchSelf(){}}
 public sealed class CodeInstruction {
  public System.Reflection.Emit.OpCode opcode;public object? operand;
  public List<string> labels=new();public List<string> blocks=new();
  public CodeInstruction(System.Reflection.Emit.OpCode c,object? o=null){opcode=c;operand=o;}
  public CodeInstruction(CodeInstruction i){opcode=i.opcode;operand=i.operand;labels=new(i.labels);blocks=new(i.blocks);}
 }
 public static class Extensions {
  public static bool Calls(this CodeInstruction c,MethodInfo m)=>(c.opcode==System.Reflection.Emit.OpCodes.Call||c.opcode==System.Reflection.Emit.OpCodes.Callvirt)&&Equals(c.operand,m);
  public static CodeInstruction MoveLabelsTo(this CodeInstruction c,CodeInstruction dest){dest.labels.AddRange(c.labels);c.labels.Clear();return c;}
 }
 public static class AccessTools {
 const BindingFlags F=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
 public static Type? TypeByName(string name)=>typeof(AccessTools).Assembly.GetType(name);
 public static FieldInfo Field(Type t,string n)=>t.GetField(n,F)!;
 public static MethodInfo Method(Type t,string n,Type[]? args=null)=>args==null?t.GetMethod(n,F)!:t.GetMethod(n,F,null,args,null)!;
 }
}
namespace Jotunn.Utils {
 public enum CompatibilityLevel {NotEnforced,EveryoneMustHaveMod}public enum VersionStrictness {Patch}
 public sealed class NetworkCompatibilityAttribute:Attribute {public NetworkCompatibilityAttribute(CompatibilityLevel c,VersionStrictness v){}}
}
namespace EpicLoot {
 public static class MagicItem {
  public static Dictionary<string,Func<float,object[]>> Providers=new();
  public static void RegisterDisplayValues(string type,Func<float,object[]> provider)=>Providers[type]=provider;
 }
}
namespace EpicLoot.Magic.MagicItemEffects {
 public static class DartingThoughts {
  public static void RegisterDisplayValues()=>EpicLoot.MagicItem.RegisterDisplayValues("DartingThoughts",v=>new object[]{v,v*.5f});
  public static class ModifyMaxEitr {public static void Postfix(){}}
 }
}
