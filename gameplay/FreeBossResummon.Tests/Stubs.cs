namespace BepInEx
{
    public sealed class BepInPlugin : Attribute { public BepInPlugin(string id,string name,string version){} }
    public sealed class BepInDependency : Attribute { public BepInDependency(string id){} }
    public class BaseUnityPlugin { public Log Logger=new(); public class Log {public void LogInfo(string s){} } }
}
#if !REAL_HARMONY
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute {public HarmonyPatch(Type t,string method){} }
    public sealed class HarmonyAfter : Attribute {public string Owner; public HarmonyAfter(string owner){Owner=owner;} }
    public sealed class Harmony {public Harmony(string id){} public void PatchAll(System.Reflection.Assembly a){} public void UnpatchSelf(){} }
    public static class AccessTools
    {
        public static System.Reflection.MethodInfo Method(Type t,string n)=>t.GetMethod(n,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Static)!;
        public static T MethodDelegate<T>(System.Reflection.MethodInfo m) where T:Delegate => (T)m.CreateDelegate(typeof(T));
    }
}
#endif
namespace Jotunn.Utils
{
    public enum CompatibilityLevel { EveryoneMustHaveMod }
    public enum VersionStrictness { Patch }
    public sealed class NetworkCompatibilityAttribute : Attribute {public NetworkCompatibilityAttribute(CompatibilityLevel c,VersionStrictness v){} }
}
namespace UnityEngine
{
    public class Object {public static implicit operator bool(Object? x)=>x is not null;}
    public sealed class GameObject:Object {public object? Component;public T? GetComponent<T>() where T:class => Component as T;}
    public struct Vector3 {public float x,y,z;}
}
public class Character:UnityEngine.Object {public bool m_boss;public string m_defeatSetGlobalKey="";}
public class Humanoid:Character { }
public class ItemDrop:UnityEngine.Object
{
    public ItemData m_itemData=new();
    public class ItemData {public SharedData m_shared=new();}
    public class SharedData {public string m_name="";}
}
public class ZoneSystem:UnityEngine.Object
{
    public static ZoneSystem instance=new(); public HashSet<string> Keys=new();
    public bool GetGlobalKey(string key)=>Keys.Contains(key.ToLower());
}
public class Localization
{
    public static Localization instance=new();public string Language="English";
    public string GetSelectedLanguage()=>Language;
    public string Localize(string text)=>text;
}
public class OfferingBowl
{
    public UnityEngine.GameObject? m_bossPrefab;
    public ItemDrop? m_bossItem;public bool m_useItemStands;public string m_name="$altar";
    public bool Queued;public int Requests,Positions;public bool? RemoveInventory;
    public bool Interact(Humanoid user,bool hold,bool alt)=>false;
    public bool UseItem(Humanoid user,ItemDrop.ItemData item)=>false;
    public string GetHoverText()=>"vanilla";
    private bool IsBossSpawnQueued()=>Queued;
    private UnityEngine.Vector3 GetSpawnPosition(){Positions++;return new(){x=1,y=2,z=3};}
    private void InitiateSpawnBoss(UnityEngine.Vector3 p,bool removeItemsFromInventory)
    {if(p.x!=1||p.y!=2||p.z!=3)throw new Exception("spawn position changed");Requests++;RemoveInventory=removeItemsFromInventory;}
    private void RPC_SpawnBoss(long senderId,UnityEngine.Vector3 point,bool removeItemsFromInventory){}
    private void RemoveAltarItems(){}
    private void RPC_RemoveBossSpawnInventoryItems(long senderId){}
}
