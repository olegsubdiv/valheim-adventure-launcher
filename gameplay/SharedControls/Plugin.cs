using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace AdventureRpg.SharedControls;

[BepInPlugin(Guid, "Adventure RPG - Shared Controls", "1.0.0")]
[BepInDependency("randyknapp.mods.epicloot", "0.15.5")]
[BepInDependency("orax.AutoAttack", "0.1.0")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.sharedcontrols";
    private static Plugin? instance;
    private Harmony? harmony;
    private ConfigFile epic = null!, auto = null!;
    private ConfigEntry<int> marker = null!;
    private readonly NativeOverrides native = new();
    private bool failed, waiting, watching;

    private void Awake()
    {
        epic = OwnerConfig("randyknapp.mods.epicloot", new Version(0, 15, 5));
        auto = OwnerConfig("orax.AutoAttack", new Version(0, 1, 0));
        marker = Config.Bind("Migration", "AppliedVersion", 0, "Local one-time controls migration marker. Do not distribute this file. 1 means the shared defaults were applied; subsequent personal remaps are preserved.");
        instance = this;
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { instance = null; harmony.UnpatchSelf(); throw; }
        TryMigrate();
    }

    private static ConfigFile OwnerConfig(string guid, Version version)
    {
        var info = Chainloader.PluginInfos[guid];
        if (info.Metadata.Version != version || info.Instance is not BaseUnityPlugin owner)
            throw new NotSupportedException("Shared Controls requires reviewed " + guid + " " + version + ".");
        return owner.Config;
    }

    private void TryMigrate()
    {
        if (failed) return;
        try
        {
            var result = ControlMigration.Apply(epic, auto, Config, marker);
            if (result == MigrationResult.Pending)
            {
                if (!waiting) Logger.LogInfo("Waiting for AutoAttack's dynamic movement controls; migration marker remains 0.");
                waiting = true;
                return;
            }
            if (!watching)
            {
                epic.SettingChanged += BindingChanged;
                auto.SettingChanged += BindingChanged;
                watching = true;
            }
            RefreshNative();
            if (result == MigrationResult.Applied) Logger.LogInfo("Shared keyboard defaults applied once: set actions G, spirit animal Z, ability slot 1 U, AutoAttack F; movement does not pause AutoAttack. Future personal remaps are preserved.");
        }
        catch (Exception ex)
        {
            failed = true;
            native.Restore();
            Logger.LogError("Shared Controls could not complete its migration. No success marker was committed; review the error before retrying. " + ex);
        }
    }

    private void BindingChanged(object? sender, SettingChangedEventArgs args)
    {
        var definition = args.ChangedSetting.Definition;
        if (!failed && ((definition.Section == ControlMigration.EpicSection && ControlMigration.SetKeys.Contains(definition.Key)) ||
            (definition.Section == "Keyboard" && definition.Key == "Toggle auto-attack key"))) RefreshNative();
    }
    private void RefreshNative()
    {
        if (ZInput.instance == null) return;
        bool useG = ControlMigration.SetKeys.Any(key => epic.TryGetEntry<KeyCode>(ControlMigration.EpicSection, key, out var entry) && entry.Value == KeyCode.G);
        bool useF = auto.TryGetEntry<KeyboardShortcut>("Keyboard", "Toggle auto-attack key", out var toggle) &&
            toggle.Value.MainKey == KeyCode.F && !toggle.Value.Modifiers.Any();
        native.Refresh(ZInput.instance, useG, useF);
    }
    private void OnDestroy()
    {
        if (watching) { epic.SettingChanged -= BindingChanged; auto.SettingChanged -= BindingChanged; }
        native.Restore();
        if (ReferenceEquals(instance, this)) instance = null;
        harmony?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.Initialize))]
    private static class InputReady
    {
        [HarmonyAfter("orax.AutoAttack")]
        private static void Postfix() => instance?.TryMigrate();
    }

    [HarmonyPatch]
    private static class DynamicControlsReady
    {
        private static MethodBase TargetMethod() => AccessTools.Method(AccessTools.TypeByName("AutoAttack.DynamicButtonConfig"), "Generate", new[] { typeof(ConfigFile) })
            ?? throw new MissingMethodException("Reviewed AutoAttack.DynamicButtonConfig.Generate(ConfigFile) not found.");
        private static void Postfix() => instance?.TryMigrate();
    }

    [HarmonyPatch(typeof(ZInput), nameof(ZInput.Save))]
    private static class KeepNativePreferences
    {
        private static void Prefix(ZInput __instance, out NativeOverrides.SaveScope? __state) => __state = instance?.native.BeforeSave(__instance);
        // A void finalizer preserves the original exception, including failures
        // from another Save patch. Only our temporarily restored bindings reset.
        private static void Finalizer(NativeOverrides.SaveScope? __state) => __state?.Owner.AfterSave(__state);
    }
}
