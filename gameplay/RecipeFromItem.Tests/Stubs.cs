// Engine-free fixture types; tests execute the linked production postfix.
namespace BepInEx
{
    public sealed class BepInPlugin : Attribute { public BepInPlugin(string id, string name, string version) { } }
    public sealed class BepInDependency : Attribute { public BepInDependency(string id) { } }
    public class BaseUnityPlugin { public Log Logger = new(); public sealed class Log { public void LogInfo(string s) { } } }
}
namespace HarmonyLib
{
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type t, string name, Type[] args) { } }
    public sealed class HarmonyPriority : Attribute { public HarmonyPriority(int p) { } }
    public static class Priority { public const int Last = 0; }
    public sealed class Harmony { public Harmony(string id) { } public void PatchAll(System.Reflection.Assembly a) { } public void UnpatchSelf() { } }
}
namespace Jotunn.Utils
{
    public enum CompatibilityLevel { NotEnforced }
    public enum VersionStrictness { Patch }
    public sealed class NetworkCompatibilityAttribute : Attribute { public NetworkCompatibilityAttribute(CompatibilityLevel c, VersionStrictness v) { } }
}
public sealed class Player
{
    public HashSet<string> Known = [];
    public bool IsKnownMaterial(string name) => Known.Contains(name);
    public bool HaveRequirements(Recipe recipe, bool discover, int quality, int amount) => false;
}
public sealed class Recipe { public ItemDrop? m_item; }
public sealed class ItemDrop
{
    public ItemData? m_itemData = new();
    public sealed class ItemData { public SharedData? m_shared = new(); }
    public sealed class SharedData { public string m_name = ""; public string m_dlc = ""; }
}
public sealed class DLCMan
{
    public static DLCMan? instance = new();
    public HashSet<string> Installed = [];
    public bool IsDLCInstalled(string name) => Installed.Contains(name);
}
