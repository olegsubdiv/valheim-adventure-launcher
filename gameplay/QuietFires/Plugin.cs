using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace AdventureRpg.QuietFires;

[BepInPlugin(Guid, "Adventure RPG - Quiet Fires", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.quietfires";
    private Harmony? harmony;

    private void Awake()
    {
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Hearth and brazier fire loops muted, including their BoneAppetit smokeless variants.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch(typeof(Fireplace), nameof(Fireplace.Awake))]
    private static class InitializeFire
    {
        [HarmonyPriority(Priority.First)]
        internal static void Prefix(Fireplace __instance) => MuteLoops(__instance);
    }

    internal static void MuteLoops(Fireplace fire)
    {
        if (!IsSelected(fire.gameObject.name)) return;
        // Include inactive high/low fire states so fuel and weather changes do
        // not bring the noise back. ZSFX adjusts volume, but never resets mute.
        foreach (var audio in fire.GetComponentsInChildren<AudioSource>(true))
            if (audio.loop) audio.mute = true;
    }

    internal static bool IsSelected(string name)
    {
        const string clone = "(Clone)";
        if (name.EndsWith(clone, System.StringComparison.Ordinal))
            name = name.Substring(0, name.Length - clone.Length);
        return name is "hearth" or "rk_hearth" or "rk_brazier" or
            "piece_brazierceiling01" or "piece_brazierfloor01" or "piece_brazierfloor02";
    }
}
