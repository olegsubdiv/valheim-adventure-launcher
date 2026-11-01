using AdventureRpg.FirecastLifecycle;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using UnityEngine;

if (args.Length != 2) throw new ArgumentException("Usage: <workspace root> <actual client Managed folder>");
int checks = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; }
void Throws(Action action, string name) { bool failed = false; try { action(); } catch { failed = true; } Check(failed, name); }
Fixture New() => new();
var f = New(); var before = f.Snapshot(); var policy = f.Prepare();
Check(before == f.Snapshot(), "preflight has no mutations");
Check(policy.Apply() == 3, "exact three affected attachments");
foreach (var row in f.Rows) Check(!row.m_attach && row.m_follow, "target follows without parenting");
Check(f.Snapshot(ignoreFlags: true) == before, "all other weapon/effect/lifetime values preserved");
Check(f.Other.m_attach && !f.Other.m_follow, "other attached effects unchanged");
Check(policy.Apply() == 0, "idempotent application");
policy.Restore(); Check(before == f.Snapshot(), "restore original rows only"); policy.Restore(); Check(before == f.Snapshot(), "restore idempotent");
foreach (int i in Enumerable.Range(0, 3))
{
    var partial = New(); partial.Rows[i].m_attach = false; partial.Rows[i].m_follow = true;
    var original = partial.Snapshot(); var p = partial.Prepare(); Check(p.Apply() == 2, "already following row not owned"); p.Restore(); Check(partial.Snapshot() == original, "prior fix not reverted");
}
var fully = New(); foreach (var row in fully.Rows) { row.m_attach = false; row.m_follow = true; }
Check(fully.Prepare().Apply() == 0, "already repaired assets accepted");
var concurrent = New(); var pending = concurrent.Prepare(); concurrent.Rows[2].m_follow = true;
Throws(() => pending.Apply(), "preflight detects concurrent modification"); Check(concurrent.Rows[0].m_attach && !concurrent.Rows[0].m_follow, "no partial apply on conflict");
var foreign = New(); var owned = foreign.Prepare(); owned.Apply(); foreign.Rows[0].m_follow = false; owned.Restore();
Check(!foreign.Rows[0].m_attach && !foreign.Rows[0].m_follow, "unload preserves foreign row edit");
foreach (Action<Fixture> breakContract in new Action<Fixture>[]
{
    x => x.Effect.name = "other", x => x.Staff.name = "other", x => x.Monster.name = "other",
    x => x.Effect.Components.Remove(typeof(ZNetView)), x => x.Effect.GetComponent<ZNetView>()!.m_persistent = true,
    x => x.Effect.Components.Remove(typeof(ZSyncTransform)), x => x.Effect.GetComponent<TimedDestruction>()!.m_timeout = 3,
    x => x.Effect.GetComponent<TimedDestruction>()!.m_triggerOnAwake = false,
    x => x.Effect.GetComponent<TimedDestruction>()!.m_forceTakeOwnershipAndDestroy = false,
    x => x.Rows[0].m_childTransform = "wrong", x => x.Rows[1].m_enabled = false, x => x.Rows[2].m_variant = 1,
    x => x.Rows[0].m_inheritParentRotation = true, x => x.Rows[0].m_inheritParentScale = true,
    x => x.Rows[0].m_multiplyParentVisualScale = true, x => x.Rows[0].m_randomRotation = true, x => x.Rows[0].m_scale = true,
    x => { x.Rows[0].m_attach = false; x.Rows[0].m_follow = false; },
    x => x.Staff.GetComponent<ItemDrop>()!.m_itemData.m_shared.m_attack.m_startEffect.m_effectPrefabs = [x.Rows[0]],
    x => x.Rows[2].m_prefab = new() { name = FirecastPolicy.EffectName }
})
{
    var broken = New(); breakContract(broken); var snapshot = broken.Snapshot(); Throws(() => broken.Prepare(), "asset drift refused"); Check(broken.Snapshot() == snapshot, "failed preflight writes nothing");
}

// Behavioral regression model derived from the pinned native methods and asset:
// parent destruction recursively destroys attached children; their ZNetView does
// not unregister. Native unload resets ZDO before accessing gameObject and fails.
// Following keeps the effect outside that hierarchy; its native timer/unload can
// still remove its registered view. This models the failure; it is not Unity.
foreach (bool owner in new[] { false, true }) foreach (bool timerFirst in new[] { false, true })
{
    var old = New(); var oldModel = new LifecycleModel(old.Rows[0].m_attach, owner);
    if (timerFirst) oldModel.NativeTimerOrRemoteRemoval(); oldModel.DestroyParent();
    if (!timerFirst)
    {
        Throws(oldModel.NativeUnload, "original attached child reproduces unload failure"); Check(oldModel.Registered && !oldModel.ZdoPresent, "failed unload leaves reset view registered");
        Throws(oldModel.NativeUnload, "repeated unload keeps failing");
    }
    else { oldModel.NativeUnload(); Check(!oldModel.Registered, "already expired effect does not leak"); }
    var repaired = New(); repaired.Prepare().Apply(); var model = new LifecycleModel(repaired.Rows[0].m_attach, owner);
    if (timerFirst) model.NativeTimerOrRemoteRemoval(); model.DestroyParent();
    if (!timerFirst) { Check(model.Alive && model.Registered, "effect survives parent removal"); model.NativeTimerOrRemoteRemoval(); }
    model.NativeUnload(); Check(!model.Registered, "normal native removal completes for owner and remote");
    Check(model.NetworkRecordDeleted == owner, "only native owner destroys transient network record");
}
for (int i = 0; i < 100; i++)
{
    var sample = New(); sample.Prepare().Apply(); var model = new LifecycleModel(sample.Rows[i % 3].m_attach, i % 2 == 0);
    model.DestroyParent(); model.NativeUnload(); Check(!model.Registered, "repeated parent/unload cycles leave no model registry entry");
}

// Startup calls the actual owner cache once and repairs the shared source assets.
var startup = New(); ItemManager.PrefabManager.Bundle = startup.Bundle(); var plugin = new Plugin();
typeof(Plugin).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plugin, null);
Check(ItemManager.PrefabManager.Calls == 1 && startup.Rows.All(r => !r.m_attach && r.m_follow), "startup reuses owner bundle and fixes source rows");
typeof(Plugin).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(plugin, null);
Check(startup.Rows.All(r => r.m_attach && !r.m_follow), "plugin unload restores own changes");

// Independent evidence from real serialized assets and production IL/API.
var root = Path.GetFullPath(args[0]); var client = Path.GetFullPath(args[1]);
using var assetJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "research/20261101-portal-lag/firecast-prefab.json")));
var comps = assetJson.RootElement[0].GetProperty("components").EnumerateArray().Where(x => x.TryGetProperty("script", out _)).ToArray();
var assetView = comps.Single(c => c.GetProperty("script").GetProperty("m_ClassName").GetString() == "ZNetView").GetProperty("data");
var assetTimer = comps.Single(c => c.GetProperty("script").GetProperty("m_ClassName").GetString() == "TimedDestruction").GetProperty("data");
Check(assetView.GetProperty("m_persistent").GetInt32() == 0 && assetTimer.GetProperty("m_timeout").GetDouble() == 2 && assetTimer.GetProperty("m_triggerOnAwake").GetInt32() == 1 && assetTimer.GetProperty("m_forceTakeOwnershipAndDestroy").GetInt32() == 1, "actual serialized lifetime contract");
var referenceJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "research/20261101-portal-lag/firecast-references.json")));
Check(referenceJson.RootElement.EnumerateArray().Where(e => e.GetProperty("script").GetString() == "ItemDrop").Sum(e => e.GetProperty("references").GetArrayLength()) == 3, "actual bundle has three item attachment references");

var gameRoot = Path.Combine(root, "management/BakaTrial/runtime");
using var resolver = new DefaultAssemblyResolver();
foreach (var path in new[] { Path.Combine(gameRoot, "valheim_server_Data/Managed"), client, Path.Combine(gameRoot, "BepInEx/core"), Path.Combine(gameRoot, "BepInEx/plugins/Therzie-Wizardry"), Path.Combine(gameRoot, "BepInEx/plugins/ValheimModding-Jotunn") }) resolver.AddSearchDirectory(path);
var rp = new ReaderParameters { AssemblyResolver = resolver };
string dll = Path.Combine(root, "launcher/gameplay/FirecastLifecycle/bin/Release/netstandard2.1/FirecastLifecycle.dll");
using var production = AssemblyDefinition.ReadAssembly(dll, rp);
IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> types) { foreach (var t in types) { yield return t; foreach (var n in All(t.NestedTypes)) yield return n; } }
var types = All(production.MainModule.Types).ToArray(); var methods = types.SelectMany(t => t.Methods).Where(m => m.HasBody).ToArray();
foreach (var reference in production.MainModule.GetMemberReferences())
    if (reference.DeclaringType.Scope is AssemblyNameReference assembly && !assembly.Name.StartsWith("System") && assembly.Name != "netstandard")
        Check(reference switch { MethodReference m => m.Resolve() != null, FieldReference field => field.Resolve() != null, _ => true }, "actual external member resolves: " + reference.FullName);
Check(!methods.Any(m => m.Name is "Update" or "FixedUpdate" or "LateUpdate" || m.Name.StartsWith("RPC_")), "no polling or custom RPC");
Check(!types.Any(t => t.CustomAttributes.Any(a => a.AttributeType.Name.StartsWith("Harmony"))), "no Harmony hooks");
var calls = methods.SelectMany(m => m.Body.Instructions).Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand).ToArray();
Check(!calls.Any(m => m.Name is "Destroy" or "DestroyZDO" or "SetGlobalKey" or "Damage" or "ResetZDO" || m.DeclaringType.Name is "ZNetScene" or "ZDOMan" || m.DeclaringType.FullName.StartsWith("System.IO")), "no world/object/registry/save writes");
foreach (var instruction in methods.SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Stfld))
{
    var field = (FieldReference)instruction.Operand;
    if (field.Resolve().Module != production.MainModule) Check(field.DeclaringType.FullName == "EffectList/EffectData" && (field.Name is "m_attach" or "m_follow"), "only two external effect flags can be written: " + field.FullName);
}
var bundleCall = calls.Single(m => m.Name == "RegisterAssetBundle");
Check(bundleCall.DeclaringType.Scope.Name == "Wizardry" && bundleCall.Parameters.Count == 2, "cache lookup is scoped to original Wizardry assembly");
foreach (var managed in new[] { client, Path.Combine(gameRoot, "valheim_server_Data/Managed") })
{
    using var game = AssemblyDefinition.ReadAssembly(Path.Combine(managed, "assembly_valheim.dll"), rp);
    var scene = game.MainModule.Types.Single(t => t.Name == "ZNetScene"); var remove = scene.Methods.Single(m => m.Name == "RemoveObjects");
    var names = remove.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => ((MethodReference)i.Operand).Name).ToList();
    Check(names.IndexOf("ResetZDO") < names.IndexOf("get_gameObject") && names.IndexOf("get_gameObject") < names.LastIndexOf("Remove"), "native reset/access/unregister order");
    var destroy = game.MainModule.Types.Single(t => t.Name == "TimedDestruction").Methods.Single(m => m.Name == "DestroyNow");
    Check(destroy.Body.Instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.Name == "ZNetScene" && m.Name == "Destroy"), "native timer uses registered destruction");
    var viewDestroy = game.MainModule.Types.Single(t => t.Name == "ZNetView").Methods.Single(m => m.Name == "OnDestroy");
    Check(!viewDestroy.Body.Instructions.Any(i => i.Operand is MethodReference m && ((m.DeclaringType.Name == "ZNetScene" && m.Name != "get_instance") || m.DeclaringType.Name == "ZDOMan")), "native view OnDestroy does not deregister");
    var create = game.MainModule.Types.Single(t => t.Name == "EffectList").Methods.Single(m => m.Name == "Create");
    bool hasAttach = create.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == "m_attach");
    bool hasFollow = create.Body.Instructions.Any(i => i.Operand is FieldReference field && field.Name == "m_follow");
    bool hasConstraint = create.Body.Instructions.Any(i => i.Operand is GenericInstanceMethod m && m.GenericArguments.Any(t => t.Name == "ParentConstraint"));
    Check(hasAttach && hasFollow && hasConstraint, "native follow creates a constraint instead of parenting");
}
string hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(dll)));
var receipt = new { status = "PASS", checks, version = "1.0.0", dllSha256 = hash, effectEntries = 3, externalFieldsWritten = new[] { "m_attach", "m_follow" }, customPollingLoops = 0, harmonyHooks = 0, gameStarted = false, liveUnityAndMpTested = false, lifecycleModelIsNotLiveUnity = true };
File.WriteAllText(Path.Combine(root, "research/20261101-portal-lag/firecast-checks.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(receipt));

sealed class Fixture
{
    public readonly GameObject Effect = new() { name = FirecastPolicy.EffectName }, Staff = new() { name = "StaffPlains_TW" }, Monster = new() { name = "TMMonsterItemGoblinMage_FireballAttack" };
    public readonly EffectList.EffectData[] Rows;
    public readonly EffectList.EffectData Other = new() { m_prefab = new() { name = "unrelated" }, m_childTransform = "OtherBone" };
    public Fixture()
    {
        Effect.Add(new ZNetView()); Effect.Add(new ZSyncTransform()); Effect.Add(new TimedDestruction());
        Rows = new[] { "RightHand", "LeftHand", "R_Hand" }.Select(bone => new EffectList.EffectData { m_prefab = Effect, m_childTransform = bone }).ToArray();
        Staff.Add(new ItemDrop()).m_itemData.m_shared.m_attack.m_startEffect.m_effectPrefabs = [Other, Rows[0], Rows[1]];
        Monster.Add(new ItemDrop()).m_itemData.m_shared.m_startEffect.m_effectPrefabs = [Rows[2]];
    }
    public FirecastPolicy Prepare() => FirecastPolicy.Prepare(Effect, Staff, Monster);
    public AssetBundle Bundle() => new() { Assets = { [Effect.name] = Effect, [Staff.name] = Staff, [Monster.name] = Monster } };
    public string Snapshot(bool ignoreFlags = false) => JsonSerializer.Serialize(new
    {
        Effect.name, StaffName = Staff.name, MonsterName = Monster.name,
        Damage = Staff.GetComponent<ItemDrop>()!.m_itemData.m_shared.Damage,
        Cost = Monster.GetComponent<ItemDrop>()!.m_itemData.m_shared.Cost,
        Lifetime = Effect.GetComponent<TimedDestruction>()?.m_timeout,
        Rows = Rows.Append(Other).Select(r => new { Prefab = r.m_prefab.name, Attach = ignoreFlags ? true : r.m_attach, Follow = !ignoreFlags && r.m_follow, r.m_enabled, r.m_variant, r.m_childTransform, r.m_inheritParentRotation, r.m_inheritParentScale, r.m_multiplyParentVisualScale, r.m_randomRotation, r.m_scale })
    });
}
sealed class LifecycleModel(bool attached, bool owner)
{
    public bool Alive = true, Registered = true, ZdoPresent = true;
    public bool NetworkRecordDeleted;
    public void DestroyParent() { if (attached) Alive = false; }
    public void NativeTimerOrRemoteRemoval() { if (!Alive) return; Registered = false; ZdoPresent = false; Alive = false; NetworkRecordDeleted = owner; }
    public void NativeUnload()
    {
        if (!Registered) return;
        if (!ZdoPresent) throw new NullReferenceException("ResetZDO on the previously reset view");
        ZdoPresent = false;
        if (!Alive) throw new NullReferenceException("Destroyed view.gameObject");
        Alive = false; Registered = false; NetworkRecordDeleted = owner;
    }
}
