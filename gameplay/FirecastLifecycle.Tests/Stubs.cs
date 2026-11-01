namespace UnityEngine
{
    public class Object
    {
        public string name = "";
        public bool Alive = true;
        public static implicit operator bool(Object? value) => value is not null && value.Alive;
    }
    public class Component : Object { }
    public class GameObject : Object
    {
        public readonly Dictionary<Type, Component> Components = new();
        public T GetComponent<T>() where T : Component => Components.TryGetValue(typeof(T), out var c) ? (T)c : null!;
        public T Add<T>(T value) where T : Component { Components.Add(typeof(T), value); return value; }
    }
    public class AssetBundle : Object
    {
        public readonly Dictionary<string, GameObject> Assets = new();
        public T LoadAsset<T>(string name) where T : Object => (T)(Object)Assets[name];
    }
}
namespace BepInEx
{
    [AttributeUsage(AttributeTargets.Class)] public class BepInPlugin(string guid, string name, string version) : Attribute { }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)] public class BepInDependency : Attribute
    { public BepInDependency(string guid) { } public BepInDependency(string guid, string version) { } }
    public class BaseUnityPlugin
    { public readonly Log Logger = new(); public class Log { public readonly List<string> Lines = new(); public void LogInfo(object message) => Lines.Add(message.ToString()!); } }
}
namespace BepInEx.Bootstrap
{
    public class Info { public Meta Metadata = new(); public class Meta { public System.Version Version = new(1, 2, 3); } }
    public static class Chainloader { public static readonly Dictionary<string, Info> PluginInfos = new() { ["Therzie.Wizardry"] = new() }; }
}
namespace Jotunn.Utils
{
    public enum CompatibilityLevel { EveryoneMustHaveMod } public enum VersionStrictness { Patch }
    [AttributeUsage(AttributeTargets.Class)] public class NetworkCompatibilityAttribute(CompatibilityLevel level, VersionStrictness strict) : Attribute { }
}
namespace ItemManager
{
    public static class PrefabManager
    {
        public static UnityEngine.AssetBundle Bundle = new();
        public static int Calls;
        public static UnityEngine.AssetBundle RegisterAssetBundle(string name, string folder) { if (name != "warfarewizard" || folder != "assets") throw new Exception("Wrong bundle"); Calls++; return Bundle; }
    }
}
public class ZNetView : UnityEngine.Component { public bool m_persistent; }
public class ZSyncTransform : UnityEngine.Component { }
public class TimedDestruction : UnityEngine.Component { public float m_timeout = 2; public bool m_triggerOnAwake = true, m_forceTakeOwnershipAndDestroy = true; }
public class EffectList
{
    public EffectData[] m_effectPrefabs = [];
    public class EffectData
    {
        public UnityEngine.GameObject m_prefab = null!;
        public bool m_enabled = true;
        public int m_variant;
        public bool m_attach = true, m_follow, m_inheritParentRotation, m_inheritParentScale, m_multiplyParentVisualScale, m_randomRotation, m_scale;
        public string m_childTransform = "";
    }
}
public class Attack { public EffectList m_startEffect = new(); }
public class ItemDrop : UnityEngine.Component
{
    public ItemData m_itemData = new();
    public class ItemData
    {
        public SharedData m_shared = new();
        public class SharedData { public Attack m_attack = new(); public EffectList m_startEffect = new(); public float Damage = 123, Cost = 7; }
    }
}
