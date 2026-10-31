using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace AdventureRpg.HildirChestPortals;

[BepInPlugin(Guid, "Adventure RPG - Hildir Chest Portals", "1.0.0")]
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
        Logger.LogInfo("The three Hildir quest chests can pass through portals.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    // Both ObjectDB.Awake and CopyOtherDB refresh these registers. Applying here
    // updates their shared item definitions before inventories load from prefabs.
    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    private static class RegisterChests
    {
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix(ObjectDB __instance) => EnableChests(__instance);
    }

    internal static void EnableChests(ObjectDB database)
    {
        foreach (string name in ChestNames)
        {
            var prefab = database.GetItemPrefab(name);
            if (prefab == null) continue;
            var drop = prefab.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared != null)
                drop.m_itemData.m_shared.m_teleportable = true;
        }
    }

    private static readonly string[] ChestNames =
        { "chest_hildir1", "chest_hildir2", "chest_hildir3" };
}
