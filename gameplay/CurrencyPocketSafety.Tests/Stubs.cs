using System.Reflection;
namespace BepInEx
{
 public sealed class BepInPlugin:Attribute{public BepInPlugin(string id,string name,string version){}}
 [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)]public sealed class BepInDependency:Attribute{public BepInDependency(string id){}public BepInDependency(string id,string v){}}
 public class BaseUnityPlugin{public Log Logger=new();public sealed class Log{public void LogInfo(string s){}}}
}
namespace BepInEx.Bootstrap
{
 public static class Chainloader{public static Dictionary<string,Info> PluginInfos=new();public sealed class Info{public Meta Metadata=new();}public sealed class Meta{public System.Version Version=new(1,0,15);}}
}
namespace HarmonyLib
{
 [AttributeUsage(AttributeTargets.Class|AttributeTargets.Method,AllowMultiple=true)]public sealed class HarmonyPatch:Attribute{public HarmonyPatch(){}public HarmonyPatch(Type t,string n,Type[] ts){}}
 public sealed class HarmonyPriority:Attribute{public HarmonyPriority(int p){}}public static class Priority{public const int First=800,Last=0;}
 public sealed class Harmony{public Harmony(string s){}public void PatchAll(Assembly a){}public void UnpatchSelf(){}}
 public static class AccessTools
 {
  const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
  public static MethodInfo? DeclaredMethod(Type? t,string n,Type[]? ts=null)=>t?.GetMethods(Flags).SingleOrDefault(m=>m.Name==n&&(ts==null||m.GetParameters().Select(x=>x.ParameterType).SequenceEqual(ts)));
  public static Type? TypeByName(string n)=>Assembly.GetExecutingAssembly().GetType(n);
  public static FieldInfo? Field(Type t,string n)=>t.GetField(n,Flags);
 }
}
namespace Jotunn.Utils{public enum CompatibilityLevel{EveryoneMustHaveMod}public enum VersionStrictness{Patch}public sealed class NetworkCompatibility:Attribute{public NetworkCompatibility(CompatibilityLevel c,VersionStrictness v){}}}
public static class Game{public static int m_worldLevel;}
public struct Vector2i{public int x,y;public Vector2i(int a,int b){x=a;y=b;}}
public class ItemDrop
{
 public class SharedData{public string m_name="";public int m_maxStackSize=1000;}
 public class ItemData{public int m_stack,m_quality=1,m_worldLevel;public SharedData m_shared=new();public Vector2i m_gridPos;public bool IsSameType(ItemData other)=>m_shared.m_name==other.m_shared.m_name&&m_quality==other.m_quality&&m_worldLevel==other.m_worldLevel;public ItemData Clone()=>(ItemData)MemberwiseClone();}
}
public class Inventory
{
 public readonly List<ItemDrop.ItemData> Items=new();public int Changes,StringRemoveCalls,ExactRemoveCalls;
 public int Width=8,Height=8,CellAddCalls,NormalAddCalls,PartialLimit=int.MaxValue;public bool RejectCellAdd,ThrowAfterCellAdd;
 public bool? LastFindTopFirst;
 public int CountItems(string name,int quality=-1,bool matchWorldLevel=true)=>Items.Where(i=>(name==null||i.m_shared.m_name==name)&&(quality<0||quality==i.m_quality)&&(!matchWorldLevel||i.m_worldLevel>=Game.m_worldLevel)).Sum(i=>i.m_stack);
 public void RemoveItem(string name,int amount,int itemQuality=-1,bool worldLevelBased=true)
 {
  StringRemoveCalls++;
  foreach(var item in Items)if(item.m_shared.m_name==name&&(itemQuality<0||item.m_quality==itemQuality)&&(!worldLevelBased||item.m_worldLevel>=Game.m_worldLevel)){int n=Math.Min(item.m_stack,amount);item.m_stack-=n;amount-=n;if(amount<=0)break;}
  Items.RemoveAll(i=>i.m_stack<=0);Changes++;
 }
 public bool RemoveItem(ItemDrop.ItemData item,int amount){ExactRemoveCalls++;if(!Items.Contains(item)||amount<0||item.m_stack<amount)return false;item.m_stack-=amount;if(item.m_stack<=0)Items.Remove(item);Changes++;return true;}
 public void Add(string name,int amount,int quality=1,int world=0)=>Items.Add(new(){m_shared=new(){m_name=name},m_stack=amount,m_quality=quality,m_worldLevel=world,m_gridPos=new(Items.Count%Width,Items.Count/Width)});
 public List<ItemDrop.ItemData> GetAllItems()=>Items;public bool ContainsItem(ItemDrop.ItemData item)=>Items.Contains(item);public int GetWidth()=>Width;
 private Vector2i FindEmptySlot(bool topFirst){LastFindTopFirst=topFirst;for(int step=0;step<Height;step++){int y=topFirst?step:Height-1-step;for(int x=0;x<Width;x++)if(!Items.Any(i=>i.m_gridPos.x==x&&i.m_gridPos.y==y))return new(x,y);}return new(-1,-1);}
 private bool AddItem(ItemDrop.ItemData item,int amount,int x,int y,bool skipValidPositionCheck)
 {
  CellAddCalls++;if(RejectCellAdd||x<0||y<0||x>=Width||y>=Height)return false;
  amount=Math.Min(Math.Min(amount,item.m_stack),PartialLimit);var target=Items.FirstOrDefault(i=>i.m_gridPos.x==x&&i.m_gridPos.y==y);
  if(target!=null){if(!target.IsSameType(item))return false;amount=Math.Min(amount,target.m_shared.m_maxStackSize-target.m_stack);target.m_stack+=amount;}
  else{target=item.Clone();target.m_stack=amount;target.m_gridPos=new(x,y);Items.Add(target);}
  item.m_stack-=amount;Changes++;if(ThrowAfterCellAdd)throw new InvalidOperationException("changed callback failure");return amount>0;
 }
}
public class SplitDialog{public float SliderValue;}
public class InventoryGui{public static InventoryGui? instance;public Inventory? m_splitInventory;public ItemDrop.ItemData? m_splitItem;public SplitDialog m_splitDialog=new();}
namespace ExtraSlots{public static class Slots{public static int InventoryHeightPlayer=>7;}}
public class Humanoid{public Inventory Inventory=new();public Inventory GetInventory()=>Inventory;}
public class Player:Humanoid{public static Player? m_localPlayer;public Dictionary<string,string> m_customData=new();}
namespace CurrencyPocket
{
 public static class MiscFunctions
 {
  public static int GetPlayerCoinsFromCustomData()=>Player.m_localPlayer?.m_customData.TryGetValue("CoinPocket_CoinCount",out var s)==true?int.Parse(s):0;
  public static void UpdatePlayerCustomData(int n,Player? p=null)=>(p??Player.m_localPlayer)!.m_customData["CoinPocket_CoinCount"]=Math.Max(0,n).ToString();
  public static void ExtractCoins(){}
 }
 public class CurrencyPocket
 {
  public static bool CoinExtractionInProgress;
  public static int Updates;public static void UpdatePocketUI()=>Updates++;
  public class Inventory_RemoveItem_Patch{public static void Postfix(Inventory inv,string name,int amount,int quality,bool world){}}
 }
 public static class InventoryGuiOnSplitOkPatch{public static Inventory? throwAwayInventory;public static void Prefix(InventoryGui __instance){}}
}
namespace EpicLoot
{
 public static class API
 {
  public static Func<List<ItemDrop.ItemData>>? Items;public static Func<string,int>? Count;public static Func<string,int,int>? Remove;public static string? Id;public static Func<ItemDrop.ItemData,int,int>? Exact;
  public static bool RegisterInventoryProvider(string id,Func<List<ItemDrop.ItemData>> getItems,Func<string,int> countItem,Func<string,int,int> removeItem,Func<ItemDrop.ItemData,int,int> removeExactItem){Id=id;Items=getItems;Count=countItem;Remove=removeItem;Exact=removeExactItem;return true;}
  public static bool UnregisterInventoryProvider(string id){if(Id!=id)return false;Id=null;Count=null;Remove=null;Items=null;Exact=null;return true;}
 }
}
namespace EpicLoot_UnityLib{public class InventoryManagement{public int RemoveItem(string item,int amount)=>amount;}}
