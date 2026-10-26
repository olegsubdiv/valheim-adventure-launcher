using AdventureRpg.NpcEquipmentIsolation;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Text.Json;
using UnityEngine;

int checks = 0;
void Check(bool ok, string description) { if (!ok) throw new Exception(description); checks++; }
var pantsPrefab = new GameObject(); var swordPrefab = new GameObject(); var unrelatedPrefab = new GameObject();
var template = new ItemDrop.ItemData { m_dropPrefab = pantsPrefab };
var swordTemplate = new ItemDrop.ItemData { m_dropPrefab = swordPrefab };
GameObject[] weapons = [swordPrefab];
Humanoid.ItemSet[] armor = [new() { m_items = [pantsPrefab] }];
// Execute the actual production helper with independent NPC and player copies.
foreach (var qualities in new[] { (10, 1, 2), (1, 2, 10), (2, 10, 1) })
{
    var inventories = new List<ItemDrop.ItemData[]>();
    foreach (int npcArmor in new[] { qualities.Item1, qualities.Item2, qualities.Item3 })
    {
        var pants = template.Clone(); var sword = swordTemplate.Clone();
        var other = new ItemDrop.ItemData { m_dropPrefab = unrelatedPrefab, m_quality = 3 };
        ItemDrop.ItemData[] inv = [pants, sword, other];
        inventories.Add(inv);
        EquipmentQuality.ApplyToInventory(inv, weapons, armor, 2, npcArmor);
        Check(pants.m_quality == npcArmor && sword.m_quality == 2, "NPC keeps configured armor and weapon quality");
        Check(other.m_quality == 3, "unrelated NPC items unchanged");
        Check(template.m_quality == 1 && swordTemplate.m_quality == 1, "shared prefabs retain original quality");
        var playerLoot = template.Clone();
        Check(playerLoot.m_quality == 1, "new player loot does not inherit NPC quality");
        Check(6 + (playerLoot.m_quality - 1) * 2 == 6, "new troll pants retain normal base armor");
        Check(6 + (pants.m_quality - 1) * 2 == 6 + (npcArmor - 1) * 2, "NPC armor calculation retains intended quality");
    }
    Check(inventories.Select(i => i[0].m_quality).SequenceEqual(new[] { qualities.Item1, qualities.Item2, qualities.Item3 }),
        "later NPC spawns do not change earlier NPC inventory");
}
var existingPlayerItem = template.Clone(); existingPlayerItem.m_quality = 10;
EquipmentQuality.ApplyToInventory([template.Clone()], weapons, armor, 2, 10);
Check(existingPlayerItem.m_quality == 10, "existing player item left unchanged");
var overlap = swordTemplate.Clone();
EquipmentQuality.ApplyToInventory([overlap], weapons, [new() { m_items = [swordPrefab] }], 2, 10);
Check(overlap.m_quality == 10, "set membership takes precedence as in upstream");
var orphan = new ItemDrop.ItemData { m_quality = 4 };
EquipmentQuality.ApplyToInventory([orphan], null!, [new() { m_items = null! }], 2, 10);
Check(orphan.m_quality == 4, "incomplete item data ignored");
Check(EquipmentQuality.ForFaction(0) == EquipmentQuality.Profile.Neutral, "tamed faction keeps neutral config");
Check(EquipmentQuality.ForFaction(10) == EquipmentQuality.Profile.Neutral, "neutral faction config");
Check(EquipmentQuality.ForFaction(8) == EquipmentQuality.Profile.Overlord, "boss faction config");
foreach (int faction in new[] { 2, 3, 4, 5, 6, 7, 9 })
    Check(EquipmentQuality.ForFaction(faction) == EquipmentQuality.Profile.Renegade, "hostile faction config " + faction);
foreach (int faction in new[] { 1, -1, 11, 999 })
    Check(EquipmentQuality.ForFaction(faction) == EquipmentQuality.Profile.None, "unhandled faction unchanged " + faction);

var root = Path.GetFullPath(args[0]);
using var game = AssemblyDefinition.ReadAssembly(Path.Combine(root, "management/BakaTrial/runtime/valheim_server_Data/Managed/assembly_valheim.dll"));
using var rv = AssemblyDefinition.ReadAssembly(Path.Combine(root, "management/BakaTrial/runtime/BepInEx/plugins/blacks7ar-RenegadeVikings/RenegadeVikings.dll"));
using var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.Combine(root, "management/BakaTrial/runtime/BepInEx/plugins/ValheimModding-Jotunn"));
resolver.AddSearchDirectory(Path.Combine(root, "management/BakaTrial/runtime/BepInEx/core"));
resolver.AddSearchDirectory(Path.Combine(root, "management/BakaTrial/runtime/valheim_server_Data/Managed"));
resolver.AddSearchDirectory(Path.Combine(root, "management/BakaTrial/runtime/BepInEx/plugins/blacks7ar-RenegadeVikings"));
using var plugin = AssemblyDefinition.ReadAssembly(Path.Combine(root, "launcher/gameplay/NpcEquipmentIsolation/bin/Release/netstandard2.1/NpcEquipmentIsolation.dll"), new ReaderParameters { AssemblyResolver = resolver });
IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> ts) { foreach (var t in ts) { yield return t; foreach (var n in Types(t.NestedTypes)) yield return n; } }
MethodDefinition Method(AssemblyDefinition a, string type, string name) => Types(a.MainModule.Types).Single(t => t.FullName == type).Methods.Single(m => m.Name == name);
IEnumerable<MethodReference> Calls(MethodDefinition m) => m.Body.Instructions.Where(i => i.OpCode == OpCodes.Call || i.OpCode == OpCodes.Callvirt).Select(i => i.Operand).OfType<MethodReference>();
var awake = Method(rv, "RenegadeVikings.Functions.EquipmentAdjustment", "Awake");
Check(awake.Parameters.Count == 0 && !awake.IsPublic, "upstream Awake signature");
var ownCalls = Calls(awake).Where(m => m.DeclaringType.FullName == awake.DeclaringType.FullName).Select(m => m.Name).Distinct().Order().ToArray();
Check(ownCalls.SequenceEqual(new[] { "BossAdjustments", "NeutralAdjustments", "RenegadeAdjustments" }), "suppressed Awake only dispatches quality adjustments");
foreach (var methodName in ownCalls)
{
    var method = Method(rv, awake.DeclaringType.FullName, methodName);
    Check(Calls(method).Count(m => m.Name == "SetQuality" && m.DeclaringType.Name == "ItemDrop") == 2, "upstream has weapon and set prefab writes " + methodName);
    Check(Calls(method).Count(m => m.Name == "add_SettingChanged") == 2, "suppressed config callbacks " + methodName);
}
var setQuality = Method(game, "ItemDrop", "SetQuality");
Check(setQuality.Body.Instructions.Count(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "m_quality") == 1, "upstream SetQuality directly writes quality");
Check(!Calls(setQuality).Any(m => m.Name.Contains("Clamp")), "upstream SetQuality does not clamp above max quality");
var give = Method(game, "Humanoid", "GiveDefaultItems");
Check(give.IsPublic && give.Parameters.Count == 0, "GiveDefaultItems patch signature");
Check(Calls(Method(game, "Humanoid", "Start")).Any(m => m.Name == "GiveDefaultItems"), "NPC Start populates inventory before our postfix");
Check(Calls(Method(game, "Humanoid", "GiveDefaultItem")).Any(m => m.Name == "PickupPrefab"), "loadout creates pickup copies");
Check(Calls(Method(game, "Humanoid", "PickupPrefab")).Any(m => m.Name == "Instantiate"), "loadout starts from an independent GameObject copy");
var pt = Types(plugin.MainModule.Types).ToList();
var prefixes = pt.SelectMany(t => t.Methods).Where(m => m.Name == "Prefix").ToList();
Check(prefixes.Count == 1 && prefixes[0].Body.Instructions.Any(i => i.OpCode == OpCodes.Ldc_I4_0), "single prefix suppresses prefab mutations");
var postfix = pt.SelectMany(t => t.Methods).Single(m => m.Name == "Postfix");
Check(Calls(postfix).Any(m => m.Name == "IsPlayer") && Calls(postfix).Any(m => m.Name == "GetComponent" && m.FullName.Contains("EquipmentAdjustment")), "player and unrelated NPC guards present");
Check(Calls(postfix).Any(m => m.Name == "GetAllItems") && Calls(postfix).Any(m => m.Name == "ApplyToInventory"), "postfix changes actual NPC inventory");
Check(!pt.SelectMany(t => t.Methods).Any(m => m.Name is "Update" or "FixedUpdate" or "LateUpdate"), "no update or damage loop");
Check(!pt.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(Calls).Any(m => m.Name == "SetQuality"), "plugin never invokes shared-prefab SetQuality");
var helpers = pt.Single(t => t.Name == "EquipmentQuality");
Check(helpers.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Count(i => i.OpCode == OpCodes.Stfld && i.Operand is FieldReference f && f.Name == "m_quality") == 1, "one scoped ItemData quality write");
var metadata = pt.Single(t => t.Name == "Plugin");
var compatibility = metadata.CustomAttributes.Single(a => a.AttributeType.Name == "NetworkCompatibilityAttribute");
Check(compatibility.ConstructorArguments.Count == 2, "peer compatibility declared");
var level = compatibility.ConstructorArguments[0];
var strictness = compatibility.ConstructorArguments[1];
Check(Convert.ToInt32(level.Value) == Convert.ToInt32(level.Type.Resolve().Fields.Single(f => f.Name == "EveryoneMustHaveMod").Constant), "server and clients must all have fix");
Check(Convert.ToInt32(strictness.Value) == Convert.ToInt32(strictness.Type.Resolve().Fields.Single(f => f.Name == "Patch").Constant), "exact patch version required");
Check(Calls(Method(plugin, metadata.FullName, "Awake")).Any(m => m.Name == "UnpatchSelf"), "atomic patch cleanup on failure");
var scopedAssemblies = new HashSet<string> { "assembly_valheim", "UnityEngine", "UnityEngine.CoreModule", "RenegadeVikings", "BepInEx", "0Harmony", "Jotunn" };
foreach (var reference in plugin.MainModule.GetMemberReferences())
{
    if (reference.DeclaringType.Scope is not AssemblyNameReference scope || !scopedAssemblies.Contains(scope.Name)) continue;
    Check(reference switch { MethodReference method => method.Resolve() != null, FieldReference field => field.Resolve() != null, _ => true }, "pinned API resolves: " + reference.FullName);
}
Console.WriteLine(JsonSerializer.Serialize(new { status = "PASS", checks, gameStarted = false, savesLoaded = false, engineFreeRegression = true, pinnedAssemblyContracts = true }));
