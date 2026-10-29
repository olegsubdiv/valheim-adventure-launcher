using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EpicLoot;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.GuardBalance;

[BepInPlugin(Guid, "Adventure RPG - Guard Balance", "1.0.0")]
[BepInDependency("randyknapp.mods.epicloot", "0.14.13")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.guardbalance";
    private Harmony? harmony;
    private void Awake()
    {
        if (Chainloader.PluginInfos["randyknapp.mods.epicloot"].Metadata.Version != new System.Version(0, 14, 13))
            throw new NotSupportedException("Guard Balance 1.0.0 requires reviewed EpicLoot 0.14.13.");
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Player block armor x1.7 for shields/parry-capable equipment; parry bonus x1.2. NPC defense and vanilla parry rules unchanged.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    private static class PlayerDefense
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => GuardRewrite.Combat(instructions);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), "AddBlockTooltip")]
    private static class OrdinaryTooltip
    {
        [HarmonyPriority(Priority.Last)]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) => GuardRewrite.Tooltip(instructions);
    }

    [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPowerTooltip))]
    private static class SkillAdjustedTooltip
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(ItemDrop.ItemData __instance, ref float __result) => __result = GuardPolicy.BlockPower(__instance, __result);
    }

    [HarmonyPatch(typeof(MagicTooltip), nameof(MagicTooltip.GetBlockPowerValue))]
    private static class EnchantedBlockTooltip
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(ItemDrop.ItemData item, ref float __result) => __result = GuardPolicy.BlockPower(item, __result);
    }

    [HarmonyPatch(typeof(MagicTooltip), nameof(MagicTooltip.GetParryBonusValue))]
    private static class EnchantedParryTooltip
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(ItemDrop.ItemData item, ref float __result)
        {
            if (item.m_shared.m_timedBlockBonus > 1f) __result *= GuardPolicy.ParryMultiplier;
        }
    }
}

internal static class GuardPolicy
{
    internal const float BlockMultiplier = 1.7f;
    internal const float ParryMultiplier = 1.2f;
    internal static bool Eligible(ItemDrop.ItemData item) =>
        item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield || item.m_shared.m_timedBlockBonus > 1f;
    internal static float BlockPower(ItemDrop.ItemData item, float native) => Eligible(item) ? native * BlockMultiplier : native;

    internal static float CombatBlockPower(ItemDrop.ItemData item, float skillFactor, Humanoid defender)
    {
        // Call the original API first, including NorseDemigods/EpicLoot modifiers.
        float native = item.GetBlockPower(skillFactor);
        return defender.IsPlayer() ? BlockPower(item, native) : native;
    }
    internal static float CombatParryBonus(float native, Humanoid defender) => defender.IsPlayer() ? TooltipParryBonus(native) : native;
    internal static float TooltipBaseBlockPower(ItemDrop.ItemData item, int quality) => BlockPower(item, item.GetBaseBlockPower(quality));
    internal static float TooltipParryBonus(float native) => native > 1f ? native * ParryMultiplier : native;
}
