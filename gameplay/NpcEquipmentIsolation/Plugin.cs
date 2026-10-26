using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;
using RenegadeVikings.Functions;

namespace AdventureRpg.NpcEquipmentIsolation;

[BepInPlugin(Guid, "Adventure RPG - NPC Equipment Isolation", "1.0.0")]
[BepInDependency("blacks7ar.RenegadeVikings", "1.4.2")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.npcequipmentisolation";
    private Harmony? harmony;

    private void Awake()
    {
        var upstream = Chainloader.PluginInfos["blacks7ar.RenegadeVikings"].Metadata.Version;
        if (upstream != new System.Version(1, 4, 2))
            throw new NotSupportedException("NPC Equipment Isolation 1.0.0 requires reviewed RenegadeVikings 1.4.2.");
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("NPC equipment quality isolated: shared item prefabs stay unchanged; RenegadeVikings loadout quality applies only to NPC inventory copies.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch(typeof(EquipmentAdjustment), "Awake")]
    private static class StopSharedPrefabWrites
    {
        // Awake only changes prefab quality and registers callbacks that do the same.
        // Do not let either path modify global ObjectDB items.
        [HarmonyPriority(Priority.First)]
        private static bool Prefix() => false;
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GiveDefaultItems))]
    private static class ApplyPrivateLoadoutQuality
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Humanoid __instance)
        {
            if (__instance.IsPlayer() || __instance.GetComponent<EquipmentAdjustment>() == null) return;
            var profile = EquipmentQuality.ForFaction((int)__instance.GetFaction());
            var values = profile switch
            {
                EquipmentQuality.Profile.Neutral => (ConfigData._neutralDmgLevel.Value, ConfigData._neutralArmorLevel.Value),
                EquipmentQuality.Profile.Renegade => (ConfigData._renegadeDmgLevel.Value, ConfigData._renegadeArmorLevel.Value),
                EquipmentQuality.Profile.Overlord => (ConfigData._overlordDmgLevel.Value, ConfigData._overlordArmorLevel.Value),
                _ => (0, 0)
            };
            if (profile == EquipmentQuality.Profile.None) return;
            EquipmentQuality.ApplyToInventory(__instance.GetInventory().GetAllItems(),
                __instance.m_randomWeapon, __instance.m_randomSets, values.Item1, values.Item2);
        }
    }
}
