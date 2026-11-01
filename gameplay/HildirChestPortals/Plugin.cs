using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.HildirChestPortals;

[BepInPlugin(Guid, "Adventure RPG - Selected Item Portals", "1.0.1")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.hildirchestportals";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Hildir quest chests, the Dvergr extractor, black metal scrap and black metal can pass through portals.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    // Both ObjectDB.Awake and CopyOtherDB refresh these registers. Applying here
    // updates their shared item definitions before inventories load from prefabs.
    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    private static class RegisterChests
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(ObjectDB __instance) => EnableItems(__instance);
    }

    internal static void EnableItems(ObjectDB database)
    {
        foreach (string name in ItemNames)
        {
            var prefab = database.GetItemPrefab(name);
            if (prefab == null) continue;
            var drop = prefab.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared != null)
                drop.m_itemData.m_shared.m_teleportable = true;
        }
    }

    private static readonly string[] ItemNames =
        { "chest_hildir1", "chest_hildir2", "chest_hildir3", "DvergrNeedle", "BlackMetalScrap", "BlackMetal" };
}
