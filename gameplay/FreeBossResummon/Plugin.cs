using System;
using BepInEx;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureRpg.FreeBossResummon;

[BepInPlugin(Guid, "Adventure RPG - Free Boss Resummon", "1.0.0")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.freebossresummon";
    private Harmony? harmony;
    internal static readonly Func<OfferingBowl, bool> IsQueued =
        AccessTools.MethodDelegate<Func<OfferingBowl, bool>>(AccessTools.Method(typeof(OfferingBowl), "IsBossSpawnQueued"));
    internal static readonly Func<OfferingBowl, Vector3> GetPosition =
        AccessTools.MethodDelegate<Func<OfferingBowl, Vector3>>(AccessTools.Method(typeof(OfferingBowl), "GetSpawnPosition"));
    internal static readonly Action<OfferingBowl, Vector3, bool> Initiate =
        AccessTools.MethodDelegate<Action<OfferingBowl, Vector3, bool>>(AccessTools.Method(typeof(OfferingBowl), "InitiateSpawnBoss"));

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Defeated bosses can be summoned again without offerings at their altars.");
    }
    private void OnDestroy() => harmony?.UnpatchSelf();

    internal static bool IsFree(OfferingBowl bowl)
    {
        if (!bowl.m_bossPrefab || !ZoneSystem.instance) return false;
        var boss = bowl.m_bossPrefab!.GetComponent<Character>();
        return boss && boss!.m_boss && !string.IsNullOrEmpty(boss.m_defeatSetGlobalKey)
            && ZoneSystem.instance.GetGlobalKey(boss.m_defeatSetGlobalKey);
    }

    internal static void StartFree(OfferingBowl bowl, Humanoid user, ref Humanoid interactUser)
    {
        interactUser = user;
        // Reuse the existing request, owner validation, position search, queue,
        // animation, delay and notification. Only the offering is waived.
        Initiate(bowl, GetPosition(bowl), false);
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
    [HarmonyAfter("org.bepinex.plugins.creaturelevelcontrol")]
    internal static class InteractPatch
    {
        internal static bool Prefix(OfferingBowl __instance, Humanoid user, bool hold,
            ref Humanoid ___m_interactUser, ref bool __result)
        {
            if (!IsFree(__instance)) return true;
            __result = false;
            if (!hold && !IsQueued(__instance))
            {
                StartFree(__instance, user, ref ___m_interactUser);
                __result = true;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.UseItem))]
    [HarmonyAfter("org.bepinex.plugins.creaturelevelcontrol")]
    internal static class UseItemPatch
    {
        internal static bool Prefix(OfferingBowl __instance, Humanoid user, ItemDrop.ItemData item,
            ref Humanoid ___m_interactUser, ref bool __result)
        {
            // Keep normal wrong-item behavior; offering the expected item also
            // starts a free summon and never consumes that item.
            if (!IsFree(__instance) || __instance.m_useItemStands || !__instance.m_bossItem
                || item.m_shared.m_name != __instance.m_bossItem!.m_itemData.m_shared.m_name) return true;
            if (!IsQueued(__instance)) StartFree(__instance, user, ref ___m_interactUser);
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.GetHoverText))]
    internal static class HoverPatch
    {
        internal static void Postfix(OfferingBowl __instance, ref string __result)
        {
            if (!IsFree(__instance)) return;
            string action = Localization.instance.GetSelectedLanguage() == "Russian"
                ? "Призвать повторно (бесплатно)" : "Summon again (free)";
            __result = Localization.instance.Localize(__instance.m_name
                + "\n[<color=yellow><b>$KEY_Use</b></color>] " + action);
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "RPC_SpawnBoss")]
    internal static class SpawnRequestPatch
    {
        internal static void Prefix(OfferingBowl __instance, ref bool removeItemsFromInventory)
        {
            if (IsFree(__instance)) removeItemsFromInventory = false;
        }
    }

    [HarmonyPatch(typeof(OfferingBowl), "RemoveAltarItems")]
    internal static class AltarRemovalPatch
    {
        // Vanilla's false inventory flag otherwise consumes nearby item stands.
        // This matters for the Queen/Fader style altars. Keep their offerings.
        internal static bool Prefix(OfferingBowl __instance) => !IsFree(__instance);
    }

    [HarmonyPatch(typeof(OfferingBowl), "RPC_RemoveBossSpawnInventoryItems")]
    internal static class InventoryRemovalPatch
    {
        internal static bool Prefix(OfferingBowl __instance) => !IsFree(__instance);
    }
}
