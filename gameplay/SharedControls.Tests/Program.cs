using AdventureRpg.SharedControls;
using BepInEx.Configuration;
using BepInEx.Bootstrap;
using UnityEngine;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
void Throws(Action action, string name) { bool threw = false; try { action(); } catch { threw = true; } Check(threw, name); }
Fixture New(bool dynamic = true) => new(dynamic);
Dictionary<string, object> Values(ConfigFile config) => config.Entries.ToDictionary(x => x.Key.Section + "/" + x.Key.Key, x => x.Value.BoxedValue);
void Same(Dictionary<string, object> before, ConfigFile config, string name)
{
    var now = Values(config); Check(now.Count == before.Count && before.All(x => now.TryGetValue(x.Key, out var v) && Equals(v, x.Value)), name);
}
ZInput Input(string radial = "<Keyboard>/G", string power = "<Keyboard>/F")
{
    var input = new ZInput();
    foreach (var (name, path) in new[] { ("OpenRadial", radial), ("GP", power), ("JoyGP", "<Gamepad>/dpad/down"), ("JoyRadial", "<Gamepad>/rightStickPress"), ("Emote", "<Keyboard>/T"), ("Other", "<Keyboard>/G") })
        input.Buttons[name] = new(name, path) { Source = name.StartsWith("Joy") ? ZInput.InputSource.Gamepad : ZInput.InputSource.KeyboardMouse };
    return input;
}
string PathOf(ZInput input, string name) => input.Buttons[name].GetActionPath();

// Migration must update the real registered entries, save each owner once and
// commit the marker last. Exactly 16 intended values; no unrelated values move.
var f = New();
var epicBefore = Values(f.Epic); var autoBefore = Values(f.Auto);
var order = new List<string>();
f.Epic.OnSave = () => { Check(f.Marker.Value == 0, "marker not committed during Epic save"); order.Add("epic"); };
f.Auto.OnSave = () => { Check(f.Marker.Value == 0, "marker not committed during Auto save"); order.Add("auto"); };
f.Own.OnSave = () => { Check(f.Epic.Saves == 1 && f.Auto.Saves == 1 && f.Marker.Value == 1, "marker written after both saves"); order.Add("marker"); };
Check(f.Apply() == MigrationResult.Applied, "first application succeeds");
Check(order.SequenceEqual(new[] { "epic", "auto", "marker" }), "save order");
Check(f.Epic.Saves == 1 && f.Auto.Saves == 1 && f.Own.Saves == 1, "no per-value autosaves");
foreach (var key in ControlMigration.SetKeys) Check((KeyCode)f.Epic[ControlMigration.EpicSection, key].BoxedValue == KeyCode.G, key + " G");
Check((KeyCode)f.Epic[ControlMigration.EpicSection, "Spirit Animal Hotkey"].BoxedValue == KeyCode.Z, "spirit Z");
Check((string)f.Epic[ControlMigration.EpicSection, "Ability Hotkey 1"].BoxedValue == "u", "slot U remains string");
Check(((KeyboardShortcut)f.Auto["Keyboard", "Toggle auto-attack key"].BoxedValue).MainKey == KeyCode.F, "auto F");
Check((bool)f.Auto["General", "Cancel on weapon change"].BoxedValue, "weapon swap cancels");
foreach (var key in ControlMigration.KeyboardMovement) Check(!(bool)f.Auto["Keyboard pause buttons", key].BoxedValue, "keyboard moving does not pause");
foreach (var key in ControlMigration.GamepadMovement) Check(!(bool)f.Auto["Gamepad pause buttons", key].BoxedValue, "gamepad moving does not pause");
Check(Values(f.Epic).Where(x => !Equals(x.Value, epicBefore[x.Key])).Count() == 6, "exact six Epic changes");
Check(Values(f.Auto).Where(x => !Equals(x.Value, autoBefore[x.Key])).Count() == 10, "exact ten Auto changes");
Check((string)f.Epic[ControlMigration.EpicSection, "Ability Hotkey 2"].BoxedValue == "h" && (string)f.Epic["Other", "Balance"].BoxedValue == "unchanged", "H and balance preserved");
Check((bool)f.Auto["Keyboard pause buttons", "Block"].BoxedValue && (bool)f.Auto["Keyboard cancel buttons", "Attack"].BoxedValue, "other pause/cancel controls preserved");

// Later startups retain all manually selected values and perform no owner saves.
f.Epic.OnSave = null; f.Auto.OnSave = null; f.Own.OnSave = null;
f.Epic[ControlMigration.EpicSection, "Overwhelming Launch Hotkey"].BoxedValue = KeyCode.P;
f.Auto["Keyboard", "Toggle auto-attack key"].BoxedValue = new KeyboardShortcut(KeyCode.Q);
f.Auto["Keyboard pause buttons", "Forward"].BoxedValue = true;
var personalEpic = Values(f.Epic); var personalAuto = Values(f.Auto); var saves = (f.Epic.Saves, f.Auto.Saves, f.Own.Saves);
Check(f.Apply() == MigrationResult.AlreadyApplied, "idempotent marker");
Same(personalEpic, f.Epic, "personal Epic remap retained"); Same(personalAuto, f.Auto, "personal Auto remap retained");
Check(saves == (f.Epic.Saves, f.Auto.Saves, f.Own.Saves), "no later save");

// Raw template/orphan values cannot cause a premature commit before Generate.
var late = New(false); var lateEpic = Values(late.Epic); var lateAuto = Values(late.Auto);
Check(late.Apply() == MigrationResult.Pending && late.Marker.Value == 0, "late dynamic bindings wait");
Same(lateEpic, late.Epic, "pending does not touch Epic"); Same(lateAuto, late.Auto, "pending does not touch Auto");
Check(late.Epic.Saves + late.Auto.Saves + late.Own.Saves == 0, "pending saves nothing");
late.AddDynamic(); Check(late.Apply() == MigrationResult.Applied && late.Marker.Value == 1, "late Generate can finish migration");
var wrongType = New(); wrongType.Epic.Entries.Remove(new(ControlMigration.EpicSection, "Shield Throw Hotkey")); wrongType.Epic.Bind(ControlMigration.EpicSection, "Shield Throw Hotkey", "not-KeyCode");
var wrongBefore = Values(wrongType.Epic); Throws(() => wrongType.Apply(), "wrong actual registered type fails clearly"); Same(wrongBefore, wrongType.Epic, "wrong type preflight no mutation");
Check(wrongType.Marker.Value == 0 && wrongType.Epic.Saves + wrongType.Auto.Saves + wrongType.Own.Saves == 0, "wrong type not committed");

// A one-shot save failure at each of the three commit stages restores values,
// flags and marker; subsequent ordinary startup can retry.
for (int stage = 0; stage < 3; stage++)
{
    var failure = New(); var oldEpic = Values(failure.Epic); var oldAuto = Values(failure.Auto);
    failure.Epic.SaveOnConfigSet = false;
    var files = new[] { failure.Epic, failure.Auto, failure.Own };
    bool once = true; files[stage].OnSave = () => { if (once) { once = false; throw new IOException("fixture save failure"); } };
    Throws(() => failure.Apply(), "save failure stage " + stage);
    Same(oldEpic, failure.Epic, "Epic rollback stage " + stage); Same(oldAuto, failure.Auto, "Auto rollback stage " + stage);
    Check(failure.Marker.Value == 0, "marker rolled back stage " + stage);
    Check(!failure.Epic.SaveOnConfigSet && failure.Auto.SaveOnConfigSet && failure.Own.SaveOnConfigSet, "autosave flags restored stage " + stage);
    Check(failure.Apply() == MigrationResult.Applied, "retry after recoverable save failure " + stage);
}
var foreign = New(); bool first = true;
foreign.Auto.OnSave = () => { if (first) { first = false; foreign.Epic[ControlMigration.EpicSection, "Shield Throw Hotkey"].BoxedValue = KeyCode.P; foreign.Epic["Other", "Balance"].BoxedValue = "new foreign value"; throw new IOException("concurrent edit"); } };
Throws(() => foreign.Apply(), "rollback reports concurrent edit");
Check((KeyCode)foreign.Epic[ControlMigration.EpicSection, "Shield Throw Hotkey"].BoxedValue == KeyCode.P && (string)foreign.Epic["Other", "Balance"].BoxedValue == "new foreign value", "rollback preserves foreign edit");
Check(foreign.Marker.Value == 0, "foreign edit never committed");
var permanent = New(); permanent.Auto.OnSave = () => throw new IOException("persistent failure"); Throws(() => permanent.Apply(), "rollback write failure is reported"); Check(permanent.Marker.Value == 0, "persistent failure marker zero");

// Runtime isolation: only G/F keyboard conflicts owned by us are unbound.
foreach (bool g in new[] { false, true }) foreach (bool a in new[] { false, true })
{
    var input = Input(); var runtime = new NativeOverrides(); runtime.Refresh(input, g, a);
    Check(PathOf(input, "OpenRadial") == (g ? "" : "<Keyboard>/G"), "G scoped toggle");
    Check(PathOf(input, "GP") == (a ? "" : "<Keyboard>/F"), "F scoped toggle");
    Check(PathOf(input, "JoyGP") == "<Gamepad>/dpad/down" && PathOf(input, "JoyRadial") == "<Gamepad>/rightStickPress" && PathOf(input, "Emote") == "<Keyboard>/T" && PathOf(input, "Other") == "<Keyboard>/G", "gamepad/emotes/other G action retained");
    runtime.Restore(); Check(PathOf(input, "OpenRadial") == "<Keyboard>/G" && PathOf(input, "GP") == "<Keyboard>/F", "unload restores only owned values");
}
var custom = Input("<Keyboard>/P", "<Keyboard>/Q"); var customRuntime = new NativeOverrides(); customRuntime.Refresh(custom, true, true);
Check(PathOf(custom, "OpenRadial") == "<Keyboard>/P" && PathOf(custom, "GP") == "<Keyboard>/Q", "native other-key remaps preserved");
var gamepadOnly = Input(); gamepadOnly.Buttons["GP"].Source = ZInput.InputSource.Gamepad; gamepadOnly.Buttons["OpenRadial"].Rebindable = false;
new NativeOverrides().Refresh(gamepadOnly, true, true); Check(PathOf(gamepadOnly, "GP") == "<Keyboard>/F" && PathOf(gamepadOnly, "OpenRadial") == "<Keyboard>/G", "nonkeyboard/nonrebindable definitions not touched");
var remapped = Input(); var remapRuntime = new NativeOverrides(); remapRuntime.Refresh(remapped, true, true); remapped.Buttons["OpenRadial"].Rebind("<Keyboard>/P"); remapRuntime.Restore();
Check(PathOf(remapped, "OpenRadial") == "<Keyboard>/P" && PathOf(remapped, "GP") == "<Keyboard>/F", "unload preserves fresh remap");

// The plugin does not call native Save. During someone else's Save, only our
// originals are visible; finalization is idempotent and preserves its exception.
for (int mode = 0; mode < 4; mode++)
{
    var input = Input(); var runtime = new NativeOverrides(); runtime.Refresh(input, true, true);
    var scope = runtime.BeforeSave(input);
    Check(PathOf(input, "OpenRadial") == "<Keyboard>/G" && PathOf(input, "GP") == "<Keyboard>/F", "native serializer sees originals");
    if (mode == 1) input.Buttons["OpenRadial"].Rebind("<Keyboard>/P");
    if (mode == 2) input.Buttons["GP"].Rebind(""); // a foreign explicit unbind
    if (mode == 3) runtime.Refresh(input, false, false); // personal config changed during Save
    var saveFailure = new IOException("native save fails");
    try { throw saveFailure; } catch (IOException error) { Check(ReferenceEquals(saveFailure, error), "native Save exception preserved"); } finally { runtime.AfterSave(scope); }
    Check(PathOf(input, "OpenRadial") == (mode == 1 ? "<Keyboard>/P" : mode == 3 ? "<Keyboard>/G" : ""), "scoped G finalization");
    Check(PathOf(input, "GP") == (mode == 3 ? "<Keyboard>/F" : ""), "scoped F finalization");
    var final = PathOf(input, "OpenRadial"); runtime.AfterSave(scope); Check(PathOf(input, "OpenRadial") == final, "same scope cannot finalize twice");
    runtime.Restore(); Check(PathOf(input, "OpenRadial") == (mode == 1 ? "<Keyboard>/P" : "<Keyboard>/G"), "scope ownership cleaned");
}
var nestedInput = Input(); var nested = new NativeOverrides(); nested.Refresh(nestedInput, true, true);
var outer = nested.BeforeSave(nestedInput); var inner = nested.BeforeSave(nestedInput); nested.AfterSave(inner);
Check(PathOf(nestedInput, "OpenRadial") == "<Keyboard>/G", "nested Save leaves outer originals visible"); nested.AfterSave(outer);
Check(PathOf(nestedInput, "OpenRadial") == "", "outer Save restores runtime override");
var otherProfile = Input(); var irrelevant = nested.BeforeSave(otherProfile); nested.AfterSave(irrelevant);
Check(PathOf(otherProfile, "OpenRadial") == "<Keyboard>/G", "foreign ZInput Save unaffected");
var nextInput = Input(); nested.Refresh(nextInput, true, false); Check(PathOf(nestedInput, "OpenRadial") == "<Keyboard>/G" && PathOf(nextInput, "OpenRadial") == "", "new registry restores old ownership and scopes new");

// Startup/hook routing uses the same production methods: a deferred first Awake,
// a Generate callback, config edits and destroy, with no Update loop.
var pluginFixture = New(false);
Chainloader.PluginInfos.Clear();
Chainloader.PluginInfos["randyknapp.mods.epicloot"] = new() { Metadata = new() { Version = new(0, 15, 5) }, Instance = new() { Config = pluginFixture.Epic } };
Chainloader.PluginInfos["orax.AutoAttack"] = new() { Metadata = new() { Version = new(0, 1, 0) }, Instance = new() { Config = pluginFixture.Auto } };
var plugin = new Plugin { Config = pluginFixture.Own };
void Invoke(string name) => typeof(Plugin).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plugin, null);
ZInput.instance = Input(); Invoke("Awake");
Check(pluginFixture.Marker.Value == 0 && PathOf(ZInput.instance, "GP") == "<Keyboard>/F", "Awake waits without native changes");
pluginFixture.AddDynamic();
var dynamicPatch = typeof(Plugin).GetNestedType("DynamicControlsReady", BindingFlags.NonPublic)!;
Check(((MethodBase)dynamicPatch.GetMethod("TargetMethod", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null)!).Name == "Generate", "late binding hook resolves exact owner method");
dynamicPatch.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, null);
Check(pluginFixture.Marker.Value == 1 && PathOf(ZInput.instance, "GP") == "" && PathOf(ZInput.instance, "OpenRadial") == "", "Generate callback commits then scopes native keys");
foreach (var key in ControlMigration.SetKeys) pluginFixture.Epic[ControlMigration.EpicSection, key].BoxedValue = KeyCode.P;
Check(PathOf(ZInput.instance, "OpenRadial") == "<Keyboard>/G", "live personal Epic remaps release runtime G override");
pluginFixture.Auto["Keyboard", "Toggle auto-attack key"].BoxedValue = new KeyboardShortcut(KeyCode.F, KeyCode.LeftAlt);
Check(PathOf(ZInput.instance, "GP") == "<Keyboard>/F", "modifier shortcut does not take plain F");
Invoke("OnDestroy"); Check(PathOf(ZInput.instance, "GP") == "<Keyboard>/F", "destroy leaves native defaults restored");
ZInput.instance = null;

// Pinned production binary/API evidence. Resolves the exact host/server native
// methods and the registered owner types from their actual reviewed assemblies.
if (args.Length != 4) throw new ArgumentException("Usage: <workspace root> <reviewed reference root> <client Managed folder> <reviewed AutoAttack.dll>");
var root = System.IO.Path.GetFullPath(args[0]);
var staged = System.IO.Path.GetFullPath(args[1]);
var clientManaged = System.IO.Path.GetFullPath(args[2]);
var autoPath = System.IO.Path.GetFullPath(args[3]);
var dll = System.IO.Path.Combine(root, "launcher/gameplay/SharedControls/bin/Release/netstandard2.1/SharedControls.dll");
using var resolver = new DefaultAssemblyResolver();
foreach (var path in new[] { clientManaged, System.IO.Path.Combine(staged, "valheim_server_Data/Managed"), System.IO.Path.Combine(staged, "BepInEx/core") }) resolver.AddSearchDirectory(path);
using var production = AssemblyDefinition.ReadAssembly(dll, new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> source) { foreach (var t in source) { yield return t; foreach (var n in Types(t.NestedTypes)) yield return n; } }
var types = Types(production.MainModule.Types).ToArray();
foreach (var reference in production.MainModule.GetMemberReferences())
    if (reference.DeclaringType.Scope is AssemblyNameReference assembly && new[] { "assembly_utils", "BepInEx", "0Harmony", "UnityEngine.CoreModule" }.Contains(assembly.Name))
        Check(reference switch { MethodReference method => method.Resolve() != null, FieldReference field => field.Resolve() != null, _ => true }, "pinned member resolves: " + reference.FullName);
var pluginType = types.Single(x => x.FullName == "AdventureRpg.SharedControls.Plugin");
var deps = pluginType.CustomAttributes.Where(x => x.AttributeType.FullName == "BepInEx.BepInDependency").ToArray();
Check(deps.Length == 2 && deps.Any(x => (string)x.ConstructorArguments[0].Value == "randyknapp.mods.epicloot" && (string)x.ConstructorArguments[1].Value == "0.15.5") && deps.Any(x => (string)x.ConstructorArguments[0].Value == "orax.AutoAttack" && (string)x.ConstructorArguments[1].Value == "0.1.0"), "two pinned owner dependencies");
Check(!types.Any(x => x.CustomAttributes.Any(a => a.AttributeType.Name == "NetworkCompatibilityAttribute")), "no server presence requirement");
var ownMethods = types.SelectMany(x => x.Methods).Where(x => x.HasBody).ToArray();
Check(!ownMethods.Any(x => x.Name is "Update" or "FixedUpdate" or "LateUpdate" || x.Name.StartsWith("RPC_")), "no polling or custom RPC");
var calls = ownMethods.SelectMany(x => x.Body.Instructions).Where(x => x.Operand is MethodReference).Select(x => (MethodReference)x.Operand).ToArray();
Check(!calls.Any(x => x.DeclaringType.FullName.Contains("PlayerPrefs") || x.DeclaringType.FullName.Contains("PlatformPrefs") || (x.DeclaringType.Name == "ZInput" && x.Name == "Save")), "no preference writes/native Save call");
Check(!calls.Any(x => x.Name is "SetGlobalKey" or "Damage" or "AddEitr" or "AddStamina" || x.DeclaringType.FullName.StartsWith("System.IO.File")), "no world/resource/raw config file writes");
var savePatch = types.Single(x => x.Name == "KeepNativePreferences");
Check(savePatch.Methods.Single(x => x.Name == "Finalizer").ReturnType.FullName == "System.Void", "void finalizer preserves native exception");
var readyPatch = types.Single(x => x.Name == "InputReady").Methods.Single(x => x.Name == "Postfix");
Check(readyPatch.CustomAttributes.Any(x => x.AttributeType.Name == "HarmonyAfter"), "native Initialize after AutoAttack Generate patch");
foreach (var managed in new[] { clientManaged, System.IO.Path.Combine(staged, "valheim_server_Data/Managed") })
{
    using var native = AssemblyDefinition.ReadAssembly(System.IO.Path.Combine(managed, "assembly_utils.dll"));
    var zinput = native.MainModule.Types.Single(x => x.Name == "ZInput");
    Check(zinput.Methods.Single(x => x.Name == "Initialize").IsStatic, "native static Initialize");
    Check(zinput.Methods.Single(x => x.Name == "GetButtonDef").IsPublic, "public exact native lookup");
    var button = zinput.NestedTypes.Single(x => x.Name == "ButtonDef");
    Check(button.Methods.Any(x => x.Name == "Rebind" && x.IsPublic && x.Parameters.Single().ParameterType.FullName == "System.String"), "public Rebind(string)");
    Check(button.Methods.Single(x => x.Name == "GetActionPath").Parameters.Single().ParameterType.FullName == "System.Boolean", "effective path API");
    var rebindCalls = button.Methods.Single(x => x.Name == "Rebind").Body.Instructions.Where(x => x.Operand is MethodReference).Select(x => (MethodReference)x.Operand).ToArray();
    Check(!rebindCalls.Any(x => x.Name == "Save" || x.DeclaringType.FullName.Contains("Prefs")), "native Rebind runtime only");
    Check(zinput.Methods.Single(x => x.Name == "Save").Body.Instructions.Any(x => x.Operand is MethodReference m && m.Name == "GetActionPath"), "native Save serializes effective bindings; scoped hook necessary");
}
using var epicBinary = AssemblyDefinition.ReadAssembly(System.IO.Path.Combine(staged, "BepInEx/plugins/RandyKnapp-EpicLoot/EpicLoot.dll"));
foreach (var key in ControlMigration.SetKeys.Append("Spirit Animal Hotkey").Append("Ability Hotkey 1"))
{
    var body = Types(epicBinary.MainModule.Types).SelectMany(x => x.Methods).Where(x => x.HasBody).Select(x => x.Body.Instructions).Single(x => x.Any(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == key));
    int index = body.ToList().FindIndex(i => i.OpCode == OpCodes.Ldstr && (string)i.Operand == key);
    var bind = body.Skip(index).Take(10).Select(i => i.Operand).OfType<GenericInstanceMethod>().First(x => x.Name == "BindClient");
    Check(bind.GenericArguments.Single().FullName == (key == "Ability Hotkey 1" ? "System.String" : "UnityEngine.KeyCode"), "actual Epic registered type: " + key);
}
using var autoBinary = AssemblyDefinition.ReadAssembly(autoPath);
Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(autoPath))).ToLowerInvariant() == "5320f11a763db54351face089773731a94c84fe8beeed2a1fc2d12a63a9b57f1", "reviewed patched AutoAttack SHA");
var autoTypes = Types(autoBinary.MainModule.Types).ToArray();
var autoType = autoTypes.Single(x => x.FullName == "AutoAttack.AutoAttack");
Check((string)autoType.CustomAttributes.Single(x => x.AttributeType.Name == "BepInPlugin").ConstructorArguments[0].Value == "orax.AutoAttack", "actual AutoAttack GUID");
var generator = autoTypes.Single(x => x.FullName == "AutoAttack.DynamicButtonConfig").Methods.Single(x => x.Name == "Generate");
Check(generator.IsStatic && generator.Parameters.Single().ParameterType.FullName == "BepInEx.Configuration.ConfigFile", "exact late Generate(ConfigFile) target");
Check(autoType.Methods.Single(x => x.Name == "Update").Body.Instructions.Any(x => x.Operand is MethodReference m && m.Name == "Generate"), "actual owner fallback generates late controls");
Check(autoTypes.Single(x => x.Name == "ZInput_Initialize_GenerateButtonConfig").Methods.Single(x => x.Name == "Postfix").Body.Instructions.Any(x => x.Operand is MethodReference m && m.Name == "Generate"), "actual owner native initialization generation");
var result = new { status = "PASS", checks, version = "1.0.0", dllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dll))).ToLowerInvariant(), clientOnly = true, gameStarted = false, liveInputTested = false, markerPath = "BepInEx/config/arpg.sharedcontrols.cfg", migrationEntries = 16, ownPollingLoops = 0, registryWrites = 0 };
Directory.CreateDirectory(System.IO.Path.Combine(root, "research/20261101-shared-controls"));
File.WriteAllText(System.IO.Path.Combine(root, "research/20261101-shared-controls/checks.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(result));

sealed class Fixture
{
    public ConfigFile Epic = new(), Auto = new(), Own = new();
    public ConfigEntry<int> Marker;
    public Fixture(bool dynamic)
    {
        foreach (var key in ControlMigration.SetKeys) Epic.Bind(ControlMigration.EpicSection, key, KeyCode.U);
        Epic.Bind(ControlMigration.EpicSection, "Spirit Animal Hotkey", KeyCode.N);
        Epic.Bind(ControlMigration.EpicSection, "Ability Hotkey 1", "g");
        Epic.Bind(ControlMigration.EpicSection, "Ability Hotkey 2", "h");
        Epic.Bind(ControlMigration.EpicSection, "Ability Hotkey 3", "j");
        Epic.Bind("Other", "Balance", "unchanged");
        Auto.Bind("Keyboard", "Toggle auto-attack key", new KeyboardShortcut(KeyCode.P));
        Auto.Bind("General", "Cancel on weapon change", false);
        Auto.Bind("Keyboard pause buttons", "Block", true);
        Auto.Bind("Keyboard cancel buttons", "Attack", true);
        Marker = Own.Bind("Migration", "AppliedVersion", 0);
        if (dynamic) AddDynamic();
    }
    public void AddDynamic()
    {
        foreach (var key in ControlMigration.KeyboardMovement) Auto.Bind("Keyboard pause buttons", key, true);
        foreach (var key in ControlMigration.GamepadMovement) Auto.Bind("Gamepad pause buttons", key, true);
    }
    public MigrationResult Apply() => ControlMigration.Apply(Epic, Auto, Own, Marker);
}
