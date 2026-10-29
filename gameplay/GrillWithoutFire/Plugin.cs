using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.GrillWithoutFire;

[BepInPlugin(Guid, "Adventure RPG - Grill Without Fire", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.grillwithoutfire";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Stone Grill no longer requires a separate fire underneath it.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch(typeof(CraftingStation), "Start")]
    private static class InitializeGrill
    {
        // Apply before vanilla schedules its fire lookup. Other station requirements
        // and all cooking/smelting/fireplace components keep their original behavior.
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(CraftingStation __instance)
        {
            if (GrillNames.IsStoneGrill(__instance.gameObject.name))
                __instance.m_craftRequireFire = false;
        }
    }
}

internal static class GrillNames
{
    internal static bool IsStoneGrill(string? name) => name is
        "piece_cooking_pot" or "piece_cooking_pot(Clone)" or
        "rk_grill" or "rk_grill(Clone)";
}
