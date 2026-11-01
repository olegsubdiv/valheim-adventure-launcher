using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;
using Recycle_N_Reclaim.GamePatches.Recycling;
using ItemData = ItemDrop.ItemData;

namespace AdventureRpg.SafeReclaim;

[BepInPlugin(Guid, "Adventure RPG - Safe Reclaim", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[BepInDependency("Azumatt.Recycle_N_Reclaim", "1.4.5")]
[BepInDependency("org.bepinex.plugins.blacksmithing", "1.3.6")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.safereclaim";
    private Harmony? harmony;
    internal static readonly MethodInfo NativeAdd = AccessTools.DeclaredMethod(typeof(Inventory), nameof(Inventory.AddItem),
        new[] { typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(bool), typeof(bool) });
    internal static readonly MethodInfo SafeAdd = AccessTools.DeclaredMethod(typeof(Plugin), nameof(AddPositiveResource));
    internal static readonly AccessTools.FieldRef<InventoryGui, float> CraftTimer = AccessTools.FieldRefAccess<InventoryGui, float>("m_craftTimer");

    private void Awake()
    {
        foreach (var entry in new[] { ("Azumatt.Recycle_N_Reclaim", "1.4.5"), ("org.bepinex.plugins.blacksmithing", "1.3.6") })
            if (!Chainloader.PluginInfos.TryGetValue(entry.Item1, out var info) || info.Metadata.Version != new System.Version(entry.Item2))
                throw new InvalidOperationException("Safe Reclaim requires the reviewed exact version of " + entry.Item1);
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Safe Reclaim: zero yields skipped, Blacksmithing null result guarded, reclaim timer stopped before inventory changes.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    internal static ItemData? AddPositiveResource(Inventory inventory, string name, int amount, int quality, int variant,
        long crafterId, string crafterName, bool cheated, bool pickedUp)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        return amount == 0 ? null : inventory.AddItem(name, amount, quality, variant, crafterId, crafterName, cheated, pickedUp);
    }

    internal static bool BeginReclaim(RecyclingAnalysisContext context, Inventory inventory, bool recordUndo)
    {
        // The original UI resets this only after the entire reclaim returns.
        // Stop the request first so an exception cannot replay it next frame.
        if (recordUndo && InventoryGui.instance != null) CraftTimer(InventoryGui.instance) = -1f;
        return context?.Item != null && inventory != null && context.RecyclingImpediments.Count == 0 && inventory.ContainsItem(context.Item);
    }

    internal static void RestoreDurabilityIfPresent(ItemData? item)
    {
        if (item != null) item.m_durability = item.GetMaxDurability();
    }

    [HarmonyPatch(typeof(Reclaimer), nameof(Reclaimer.DoInventoryChanges))]
    internal static class ReclaimPatch
    {
        [HarmonyPriority(Priority.First)]
        internal static bool Prefix(RecyclingAnalysisContext analysisContext, Inventory inventory, bool recordUndo) => BeginReclaim(analysisContext, inventory, recordUndo);

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var result = instructions.ToList();
            var calls = result.Where(i => (i.opcode == OpCodes.Call || i.opcode == OpCodes.Callvirt) && Equals(i.operand, NativeAdd)).ToArray();
            if (calls.Length != 2) throw new InvalidOperationException("Expected exactly two reviewed reclaim AddItem calls.");
            foreach (var call in calls) { call.opcode = OpCodes.Call; call.operand = SafeAdd; }
            return result;
        }
    }

    [HarmonyPatch]
    internal static class BlacksmithingPatch
    {
        internal static MethodBase TargetMethod()
        {
            var type = AccessTools.TypeByName("Blacksmithing.Blacksmithing+IncreaseDurabilityToMax");
            var method = type == null ? null : AccessTools.DeclaredMethod(type, "Postfix");
            if (method == null || !method.IsStatic || method.ReturnType != typeof(void) ||
                !method.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(ItemData) }))
                throw new InvalidOperationException("Unsupported Blacksmithing durability patch.");
            return method;
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var body = instructions.ToList();
            var code = body.Where(i => i.opcode != OpCodes.Nop).ToList();
            if (code.Count != 5 || code[0].opcode != OpCodes.Ldarg_0 || code[1].opcode != OpCodes.Ldarg_0 ||
                code[2].operand is not MethodInfo get || get.Name != nameof(ItemData.GetMaxDurability) || get.DeclaringType != typeof(ItemData) ||
                code[3].opcode != OpCodes.Stfld || code[3].operand is not FieldInfo field || field.Name != nameof(ItemData.m_durability) || field.DeclaringType != typeof(ItemData) ||
                code[4].opcode != OpCodes.Ret || body.Any(i => i.blocks.Count != 0))
                throw new InvalidOperationException("Blacksmithing durability method differs from reviewed IL.");
            code[1].opcode = OpCodes.Call;
            code[1].operand = AccessTools.DeclaredMethod(typeof(Plugin), nameof(RestoreDurabilityIfPresent));
            body.Remove(code[2]); body.Remove(code[3]);
            return body;
        }
    }
}
