using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Bootstrap;
using EpicLoot;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.DartingThoughtsTuning;

[BepInPlugin(Guid, "Adventure RPG - Darting Thoughts Tuning", "1.0.0")]
[BepInDependency("randyknapp.mods.epicloot", "0.14.13")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.dartingthoughtstuning";
    private Harmony? harmony;
    private void Awake()
    {
        if (Chainloader.PluginInfos["randyknapp.mods.epicloot"].Metadata.Version != new System.Version(0, 14, 13))
            throw new NotSupportedException("Darting Thoughts Tuning 1.0.0 requires reviewed EpicLoot 0.14.13.");
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); RegisterDisplay(); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Darting Thoughts: maximum-eitr penalty reduced by 20%; eitr regeneration and enchantment rolls unchanged.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    internal const float PenaltyRatio = 0.4f;
    internal static object[] DisplayValues(float value) => new object[] { value, value * PenaltyRatio };
    internal static void RegisterDisplay() => MagicItem.RegisterDisplayValues("DartingThoughts", DisplayValues);
    private static MethodBase Target(string suffix, string method) =>
        AccessTools.Method(AccessTools.TypeByName("EpicLoot.Magic.MagicItemEffects.DartingThoughts" + suffix)
            ?? throw new MissingMemberException("Pinned EpicLoot Darting Thoughts type missing."), method)
        ?? throw new MissingMethodException("Pinned EpicLoot Darting Thoughts method missing.");

    [HarmonyPatch]
    private static class MaximumEitrPenalty
    {
        private static MethodBase TargetMethod() => Target("+ModifyMaxEitr", "Postfix");
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => RewritePenalty(instructions);
    }
    [HarmonyPatch]
    private static class DisplayRegistration
    {
        private static MethodBase TargetMethod() => Target("", "RegisterDisplayValues");
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => RegisterDisplay();
    }

    internal static IEnumerable<CodeInstruction> RewritePenalty(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        var literals = code.Where(i => i.opcode == OpCodes.Ldc_R4 && i.operand is float value && value == 0.5f).ToList();
        if (literals.Count != 1)
            throw new NotSupportedException($"Expected one Darting Thoughts penalty ratio 0.5 in EpicLoot 0.14.13; found {literals.Count}.");
        literals[0].operand = PenaltyRatio;
        return code;
    }
}
