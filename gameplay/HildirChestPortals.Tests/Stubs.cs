// Engine-free fixture types; tests execute the linked production postfix.
namespace BepInEx
{
    public sealed class BepInPlugin : Attribute { public BepInPlugin(string id, string name, string version) { } }
    public sealed class BepInDependency : Attribute { public BepInDependency(string id) { } }
    public class BaseUnityPlugin { public Log Logger = new(); public sealed class Log { public void LogInfo(string s) { } } }
}
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name, Type[]? args = null) { } }
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int p) { } }
    public static class Priority { public const int Last = 0; public const int First=800; }
    public sealed class Harmony { public Harmony(string id) { } public void PatchAll(System.Reflection.Assembly a) { } public void UnpatchSelf() { } }
}
namespace Jotunn.Utils
{
    public enum CompatibilityLevel { NotEnforced, EveryoneMustHaveMod }
    public enum VersionStrictness { Patch }
    public sealed class NetworkCompatibilityAttribute : Attribute { public NetworkCompatibilityAttribute(CompatibilityLevel c, VersionStrictness v) { } }
}
namespace UnityEngine { public sealed class GameObject { public ItemDrop? Drop; public T? GetComponent<T>() where T:class => Drop as T; } }
public sealed class ItemDrop {
 public ItemData? m_itemData=new();
 public sealed class ItemData { public SharedData? m_shared=new(); public sealed class SharedData {public bool m_teleportable;public float m_weight=200;public int m_toolTier=0;public string m_name="original";} }
}
public sealed class ObjectDB {
 public Dictionary<string,UnityEngine.GameObject> Items=new();
 public List<string> Lookups=new();
 public UnityEngine.GameObject? GetItemPrefab(string name){Lookups.Add(name);return Items.GetValueOrDefault(name);}
}
