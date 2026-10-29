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
namespace UnityEngine {public sealed class GameObject {public string name="";}}
public sealed class CraftingStation {
 public UnityEngine.GameObject gameObject=new();
 public bool m_craftRequireFire=true,m_craftRequireRoof=true,m_hasCraftTab=true,m_canRepair=true;
 public float m_rangeBuild=13,m_useDistance=3;
 public void Start(){}
}
