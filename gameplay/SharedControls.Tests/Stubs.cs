using System.Reflection;

namespace UnityEngine { public enum KeyCode { None = 0, F = 102, G = 103, H = 104, J = 106, N = 110, P = 112, Q = 113, U = 117, Z = 122, LeftAlt = 308 } }
namespace BepInEx.Configuration
{
    public readonly struct KeyboardShortcut : IEquatable<KeyboardShortcut>
    {
        public UnityEngine.KeyCode MainKey { get; }
        public UnityEngine.KeyCode[] Modifiers { get; }
        public KeyboardShortcut(UnityEngine.KeyCode key, params UnityEngine.KeyCode[] modifiers) { MainKey = key; Modifiers = modifiers; }
        public bool Equals(KeyboardShortcut other) => MainKey == other.MainKey && Modifiers.SequenceEqual(other.Modifiers);
        public override bool Equals(object? obj) => obj is KeyboardShortcut other && Equals(other);
        public override int GetHashCode() => (int)MainKey;
    }
    public sealed record ConfigDefinition(string Section, string Key);
    public abstract class ConfigEntryBase
    {
        public ConfigDefinition Definition { get; }
        public ConfigFile ConfigFile { get; }
        public abstract object BoxedValue { get; set; }
        protected ConfigEntryBase(ConfigFile file, ConfigDefinition definition) { ConfigFile = file; Definition = definition; }
    }
    public sealed class ConfigEntry<T> : ConfigEntryBase
    {
        private T value;
        public ConfigEntry(ConfigFile file, ConfigDefinition definition, T value) : base(file, definition) { this.value = value; }
        public T Value { get => value; set { if (!Equals(this.value, value)) { this.value = value; ConfigFile.Changed(this); } } }
        public override object BoxedValue { get => value!; set => Value = (T)value; }
    }
    public sealed class SettingChangedEventArgs : EventArgs
    {
        public ConfigEntryBase ChangedSetting { get; }
        public SettingChangedEventArgs(ConfigEntryBase entry) => ChangedSetting = entry;
    }
    public sealed class ConfigFile
    {
        public readonly Dictionary<ConfigDefinition, ConfigEntryBase> Entries = new();
        public bool SaveOnConfigSet { get; set; } = true;
        public int Saves;
        public Action? OnSave;
        public event EventHandler<SettingChangedEventArgs>? SettingChanged;
        public ConfigEntryBase this[string section, string key] => Entries[new(section, key)];
        public ConfigEntry<T> Bind<T>(string section, string key, T value, string description = "")
        {
            if (TryGetEntry<T>(section, key, out var entry)) return entry;
            entry = new(this, new(section, key), value); Entries.Add(entry.Definition, entry); return entry;
        }
        public bool TryGetEntry<T>(string section, string key, out ConfigEntry<T> entry)
        {
            if (Entries.TryGetValue(new(section, key), out var existing)) { entry = (ConfigEntry<T>)existing; return true; }
            entry = null!; return false;
        }
        public void Save() { Saves++; OnSave?.Invoke(); }
        internal void Changed(ConfigEntryBase entry)
        {
            if (SaveOnConfigSet) Save();
            SettingChanged?.Invoke(this, new(entry));
        }
    }
}
namespace BepInEx
{
    public sealed class BepInPlugin : Attribute { public BepInPlugin(string id, string name, string version) { } }
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class BepInDependency : Attribute { public BepInDependency(string id, string version) { } }
    public class BaseUnityPlugin
    {
        public Configuration.ConfigFile Config { get; set; } = new();
        public Log Logger { get; } = new();
        public sealed class Log { public List<string> Errors = new(); public void LogInfo(string s) { } public void LogError(string s) => Errors.Add(s); }
    }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, Info> PluginInfos = new(); }
    public sealed class Info { public Metadata Metadata = new(); public BepInEx.BaseUnityPlugin Instance = new(); }
    public sealed class Metadata { public Version Version = new(0, 15, 5); }
}
namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class HarmonyPatch : Attribute { public HarmonyPatch() { } public HarmonyPatch(Type t, string name) { } }
    [AttributeUsage(AttributeTargets.Method)] public sealed class HarmonyAfter : Attribute { public HarmonyAfter(params string[] names) { } }
    public sealed class Harmony { public Harmony(string guid) { } public void PatchAll(Assembly a) { } public void UnpatchSelf() { } }
    public static class AccessTools
    {
        public static Type TypeByName(string name) => typeof(AccessTools).Assembly.GetType(name)!;
        public static MethodInfo? Method(Type t, string name, Type[] args) => t.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, null, args, null);
    }
}
namespace AutoAttack { internal static class DynamicButtonConfig { public static void Generate(BepInEx.Configuration.ConfigFile config) { } } }
public sealed class ZInput
{
    public enum InputSource { KeyboardMouse, Gamepad }
    public sealed class ButtonDef
    {
        public string Name;
        public bool Rebindable { get; set; } = true;
        public InputSource Source { get; set; }
        private string path;
        public int Rebinds;
        public ButtonDef(string name, string path) { Name = name; this.path = path; }
        public string GetActionPath(bool effective = true) => path;
        public void Rebind(string value) { path = value; Rebinds++; }
    }
    public static ZInput? instance;
    public Dictionary<string, ButtonDef> Buttons = new();
    public ButtonDef? GetButtonDef(string name) => Buttons.GetValueOrDefault(name);
    public static void Initialize() { }
    public void Save() { }
}
