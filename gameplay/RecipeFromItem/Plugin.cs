using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.RecipeFromItem;

[BepInPlugin(Guid, "Adventure RPG - Recipe From Item", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.NotEnforced, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.recipefromitem";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Recipe From Item: obtaining an item reveals its available recipe immediately; actual crafting costs and station requirements remain unchanged.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), new[] { typeof(Recipe), typeof(bool), typeof(int), typeof(int) })]
    private static class DiscoverOwnedOutput
    {
        // Only discovery may skip ingredient/station knowledge. The caller keeps
        // enabled/seasonal filtering. Actual crafting still uses the original result.
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(Player __instance, Recipe recipe, bool discover, ref bool __result)
        {
            if (__result || !discover || recipe == null || recipe.m_item == null) return;
            var shared = recipe.m_item.m_itemData?.m_shared;
            if (shared == null || string.IsNullOrEmpty(shared.m_name) || !__instance.IsKnownMaterial(shared.m_name)) return;
            // Do not unlock content whose required DLC is absent.
            if (!string.IsNullOrEmpty(shared.m_dlc) && (DLCMan.instance == null || !DLCMan.instance.IsDLCInstalled(shared.m_dlc))) return;
            __result = true;
        }
    }
}
