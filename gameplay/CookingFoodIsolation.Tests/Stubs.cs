using System.Reflection;
namespace BepInEx
{
 public sealed class BepInPlugin:Attribute{public BepInPlugin(string id,string name,string version){}}
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]public sealed class BepInDependency:Attribute{public BepInDependency(string id){}public BepInDependency(string id,string version){}}
 public class BaseUnityPlugin{public Log Logger=new();public sealed class Log{public void LogInfo(string s){}}}
}
namespace BepInEx.Bootstrap{public static class Chainloader{public static Dictionary<string,Info> PluginInfos=new();public sealed class Info{public Meta Metadata=new();}public sealed class Meta{public System.Version Version=new(1,2,5);}}}
namespace Jotunn.Utils{public enum CompatibilityLevel{EveryoneMustHaveMod}public enum VersionStrictness{Patch}public sealed class NetworkCompatibilityAttribute:Attribute{public NetworkCompatibilityAttribute(CompatibilityLevel c,VersionStrictness s){}}}
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public sealed class HarmonyPatch:Attribute{public HarmonyPatch(){}}
 public sealed class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}public static class Priority{public const int First=800;}
 public sealed class Harmony{public Harmony(string s){}public void PatchAll(Assembly a){}public void UnpatchSelf(){}}
 public sealed class CodeInstruction{public System.Reflection.Emit.OpCode opcode;public object? operand;public List<string> labels=new(),blocks=new();public CodeInstruction(System.Reflection.Emit.OpCode o,object? v=null){opcode=o;operand=v;}}
 public static class AccessTools
 {
  const BindingFlags F=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  public static Type? TypeByName(string n)=>Assembly.GetExecutingAssembly().GetType(n);
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,F)!;
  public static MethodInfo PropertyGetter(Type t,string n)=>t.GetProperty(n,F)!.GetGetMethod(true)!;
  public static MethodInfo DeclaredMethod(Type t,string n,Type[]? args=null)=>args==null?t.GetMethod(n,F)!:t.GetMethod(n,F,null,args,null)!;
 }
}
namespace UnityEngine{public sealed class GameObject{public ItemDrop Drop=new();public T? GetComponent<T>() where T:class=>Drop as T;}}
public sealed class ItemDrop
{
 public ItemData m_itemData=new();
 public sealed class ItemData
 {
  public SharedData m_shared=new();public UnityEngine.GameObject? m_dropPrefab;public int m_stack=8,m_quality=1;public Dictionary<string,string> m_customData=new();
  public sealed class SharedData{public float m_food=36,m_foodStamina=40,m_foodRegen=4,m_foodEitr=39,m_weight=.5f,m_foodBurnTime=1800;public int m_maxStackSize=100;public string m_name="cake";}
 }
}
namespace Cooking
{
 public abstract class Modifier
 {
  public ItemDrop.ItemData Item{get;set;}=null!;public bool IsCloned{get;set;}public string Value="055";
 }
 public sealed class Cooking
 {
  public sealed class CookingSkill:Modifier
  {
   public static readonly Dictionary<ItemDrop.ItemData.SharedData,int> active=new();public int skill;public bool happy;
   public static float Health=1.1f,Stamina=1.1f,Regen=1.1f,Eitr=1.1f;
   // Pinned author formula and registration order, deliberately retaining the
   // inherited clone gate. Tests exercise both original and rewritten decisions.
   public void Load(){active[Item.m_shared]=skill;if(Value.Length>0){happy=Value[0]=='1';int.TryParse(Value[1..],out skill);}if(!IsCloned)Multiply();}
   public void Multiply(){var s=Item.m_shared;s.m_food*=1+skill*(Health-1)/100;s.m_foodStamina*=1+skill*(Stamina-1)/100;s.m_foodRegen*=1+skill*(Regen-1)/100;if(s.m_foodEitr>0)s.m_foodEitr*=1+skill*(Eitr-1)/100;}
   public void Unload(){Item.m_shared=(ItemDrop.ItemData.SharedData)typeof(object).GetMethod("MemberwiseClone",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Item.m_shared,null)!;var s=Item.m_shared;s.m_food/=1+skill*(Health-1)/100;s.m_foodStamina/=1+skill*(Stamina-1)/100;s.m_foodRegen/=1+skill*(Regen-1)/100;if(s.m_foodEitr>0)s.m_foodEitr/=1+skill*(Eitr-1)/100;}
  }
 }
}
