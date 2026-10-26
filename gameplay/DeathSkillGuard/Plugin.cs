using System.Reflection;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.DeathSkillGuard;

[BepInPlugin(Guid, "Adventure RPG - Death Skill Guard", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.deathskillguard";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Death Skill Guard active: death preserves every skill level and its XP progress; other death consequences and demigod class-switch cost are unchanged.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    private static void PreserveSkillsOnDeath(Skills skills) { }

    [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
    private static class PlayerDeath
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            DeathCallRewrite.Rewrite(instructions,
                AccessTools.Method(typeof(Skills), nameof(Skills.OnDeath), Type.EmptyTypes),
                AccessTools.Method(typeof(Skills), nameof(Skills.Clear), Type.EmptyTypes),
                AccessTools.Method(typeof(Plugin), nameof(PreserveSkillsOnDeath), new[] { typeof(Skills) }));
    }
}
