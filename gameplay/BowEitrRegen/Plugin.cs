using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.BowEitrRegen;

[BepInPlugin(Guid, "Adventure RPG - Bow Eitr Regen", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.boweitrregen";
    private Harmony? harmony;
    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Eitr can regenerate during bow attacks; stamina, dodge and actual eitr-spending delays retain their original rules.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    internal static bool AttackStopsEitrRegen(Player player)
    {
        if (!player.InAttack()) return false;
        return player.GetCurrentWeapon()?.m_shared.m_skillType != Skills.SkillType.Bows;
    }

    [HarmonyPatch(typeof(Player), "UpdateStats", new[] { typeof(float) })]
    private static class EitrAttackGate
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => Rewrite(instructions);
    }

    internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions)
    {
        var code = instructions.Select(i => new CodeInstruction(i)).ToList();
        var maxEitr = AccessTools.Method(typeof(Character), nameof(Character.GetMaxEitr), Type.EmptyTypes);
        var attack = AccessTools.Method(typeof(Character), nameof(Character.InAttack), Type.EmptyTypes);
        var dodge = AccessTools.Method(typeof(Character), nameof(Character.InDodge), Type.EmptyTypes);
        var regen = AccessTools.Field(typeof(Player), "m_eiterRegen");
        var anchors = code.Select((i, n) => (i, n)).Where(x => x.i.Calls(maxEitr)).Select(x => x.n).ToArray();
        int regenIndex = code.FindIndex(i => i.opcode == OpCodes.Ldfld && Equals(i.operand, regen));
        if (anchors.Length != 1 || regenIndex <= anchors[0] || code.Count(i => i.Calls(attack)) != 2)
            throw new NotSupportedException("Bow Eitr Regen expected separate stamina/eitr attack gates in pinned Player.UpdateStats(float).");
        var region = code.Skip(anchors[0] + 1).Take(regenIndex - anchors[0] - 1).ToList();
        var gates = region.Where(i => i.Calls(attack)).ToList();
        if (gates.Count != 1 || region.Count(i => i.Calls(dodge)) != 1)
            throw new NotSupportedException("Bow Eitr Regen could not isolate the existing eitr attack gate while retaining dodge.");
        // One call-site substitution; labels, branches, locals and the entire
        // stamina, regen multiplier, delay and synchronization code remain intact.
        gates[0].opcode = OpCodes.Call;
        gates[0].operand = AccessTools.Method(typeof(Plugin), nameof(AttackStopsEitrRegen));
        return code;
    }
}
