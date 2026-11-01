using System.Reflection;
namespace BepInEx
{
 public sealed class BepInPlugin:Attribute{public BepInPlugin(string id,string name,string version){}}
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]public sealed class BepInDependency:Attribute{public BepInDependency(string id){}public BepInDependency(string id,string v){}}
 public class BaseUnityPlugin{public Log Logger=new();public sealed class Log{public void LogInfo(string s){}}}
}
namespace BepInEx.Bootstrap
{
 public static class Chainloader{public static Dictionary<string,Info> PluginInfos=new();public sealed class Info{public Meta Metadata=new();}public sealed class Meta{public System.Version Version=new(1,4,5);}}
}
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public sealed class HarmonyPatch:Attribute{public HarmonyPatch(){}public HarmonyPatch(Type t,string n){}}
 public sealed class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}public static class Priority{public const int First=800;}
 public sealed class Harmony{public Harmony(string s){}public void PatchAll(Assembly a){}public void UnpatchSelf(){}}
 public sealed class CodeInstruction
 {
  public System.Reflection.Emit.OpCode opcode;public object? operand;public List<string> labels=new(),blocks=new();
  public CodeInstruction(System.Reflection.Emit.OpCode o,object? p=null){opcode=o;operand=p;}
 }
 public static class AccessTools
 {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  public delegate ref F FieldRef<T,F>(T instance);
  public static FieldRef<T,F> FieldRefAccess<T,F>(string name)
  {
   if(typeof(T)!=typeof(InventoryGui)||typeof(F)!=typeof(float)||name!="m_craftTimer")throw new Exception("Unexpected field reference");
   FieldRef<InventoryGui,float> get=(InventoryGui gui)=>ref gui.m_craftTimer;return (FieldRef<T,F>)(object)get;
  }
  public static MethodInfo DeclaredMethod(Type t,string n,Type[]? args=null)=>args==null?t.GetMethod(n,Flags)!:t.GetMethod(n,Flags,null,args,null)!;
  public static Type? TypeByName(string name)=>Assembly.GetExecutingAssembly().GetType(name);
 }
}
namespace Jotunn.Utils
{
 public enum CompatibilityLevel{EveryoneMustHaveMod}public enum VersionStrictness{Patch}
 public sealed class NetworkCompatibilityAttribute:Attribute{public NetworkCompatibilityAttribute(CompatibilityLevel c,VersionStrictness s){}}
}
public sealed class ItemDrop
{
 public sealed class ItemData
 {
  public string Name="source";public int m_stack=1,m_quality=1,m_variant;public float m_durability=7;public int DurabilityReads;public long Crafter;public string CrafterName="";public bool Cheated,Picked;
  public float GetMaxDurability(){DurabilityReads++;return 2300;}
 }
}
public sealed class InventoryGui{public static InventoryGui? instance;public float m_craftTimer;}
public sealed class Player{public long GetPlayerID()=>42;public string GetPlayerName()=>"Fixture";}
public sealed class Inventory
{
 public List<ItemDrop.ItemData> Items=new();public Dictionary<string,int> Counts=new();public List<(string Name,int Amount)> Calls=new();
 public string? Refuse,ThrowOn;public bool GuardBlacksmithing;public bool ThrowAfterRemove;
 public bool ContainsItem(ItemDrop.ItemData item)=>Items.Contains(item);
 public bool RemoveItem(ItemDrop.ItemData item){bool removed=Items.Remove(item);if(ThrowAfterRemove)throw new Exception("Injected after source removal");return removed;}
 public ItemDrop.ItemData? AddItem(string name,int amount,int quality,int variant,long crafter,string crafterName,bool cheated,bool picked)
 {
  Calls.Add((name,amount));if(name==ThrowOn)throw new Exception("Injected addon failure");
  ItemDrop.ItemData? item=amount>0&&name!=Refuse?new(){Name=name,m_stack=amount,m_quality=quality,m_variant=variant,Crafter=crafter,CrafterName=crafterName,Cheated=cheated,Picked=picked}:null;
  if(item!=null)Counts[name]=Counts.GetValueOrDefault(name)+amount;
  if(GuardBlacksmithing)AdventureRpg.SafeReclaim.Plugin.RestoreDurabilityIfPresent(item);else Blacksmithing.Blacksmithing.IncreaseDurabilityToMax.UnsafeForTest(item);
  return item;
 }
}
namespace Blacksmithing
{
 public sealed class Blacksmithing
 {
  public static class IncreaseDurabilityToMax
  {
   private static void Postfix(ItemDrop.ItemData item)=>item.m_durability=item.GetMaxDurability();
   public static void UnsafeForTest(ItemDrop.ItemData? item)=>Postfix(item!);
  }
 }
}
namespace Recycle_N_Reclaim.GamePatches.Recycling
{
 public sealed class RecyclingAnalysisContext
 {
  public ItemDrop.ItemData Item;public List<string> RecyclingImpediments=new();public List<(string Name,int Amount,bool OriginalZero)> Entries=new();public bool UndoRecorded;public bool ApplyCraftedBy=true;
  public RecyclingAnalysisContext(ItemDrop.ItemData item){Item=item;}
 }
 public static class Reclaimer
 {
  public static bool Patched;
  // Isolated control-flow reproduction of the pinned method; the tests also
  // inspect and rewrite its real IL to keep this fixture tied to that binary.
  public static void DoInventoryChanges(RecyclingAnalysisContext context,Inventory inventory,Player player,bool recordUndo=false)
  {
   var added=new List<(string Name,int Amount)>();
   foreach(var e in context.Entries)
   {
    if(e.Amount==0&&e.OriginalZero)continue;
    var result=Patched?AdventureRpg.SafeReclaim.Plugin.AddPositiveResource(inventory,e.Name,e.Amount,1,2,context.ApplyCraftedBy?player.GetPlayerID():0,context.ApplyCraftedBy?player.GetPlayerName():"",false,false):inventory.AddItem(e.Name,e.Amount,1,2,context.ApplyCraftedBy?player.GetPlayerID():0,context.ApplyCraftedBy?player.GetPlayerName():"",false,false);
    if(result!=null){added.Add((e.Name,e.Amount));continue;}
    if(e.Amount<1)continue;
    foreach(var a in added)inventory.Counts[a.Name]-=a.Amount;
    context.RecyclingImpediments.Add("Add failed");return;
   }
   if(inventory.RemoveItem(context.Item)&&recordUndo)context.UndoRecorded=true;
  }
 }
}
