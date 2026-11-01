using BepInEx;
using BepInEx.Bootstrap;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureRpg.FirecastLifecycle;

[BepInPlugin(Guid, "Adventure RPG - Firecast Lifecycle", "1.0.0")]
[BepInDependency("Therzie.Wizardry", "1.2.3")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.firecastlifecycle";
    private FirecastPolicy? policy;

    private void Awake()
    {
        if (Chainloader.PluginInfos["Therzie.Wizardry"].Metadata.Version != new System.Version(1, 2, 3))
            throw new NotSupportedException("Firecast Lifecycle 1.0.0 requires reviewed Wizardry 1.2.3.");
        // Wizardry's dependency has already registered these assets. Its own
        // bundle cache returns the same prefab objects used by ItemManager.
        var bundle = ItemManager.PrefabManager.RegisterAssetBundle("warfarewizard", "assets");
        if (!bundle) throw new InvalidOperationException("Reviewed Wizardry bundle is unavailable.");
        policy = FirecastPolicy.Prepare(bundle.LoadAsset<GameObject>(FirecastPolicy.EffectName),
            bundle.LoadAsset<GameObject>("StaffPlains_TW"),
            bundle.LoadAsset<GameObject>("TMMonsterItemGoblinMage_FireballAttack"));
        int changed = policy.Apply();
        Logger.LogInfo($"Firecast lifecycle ready: {changed} effect entries changed from attachment to native following; network lifetime and combat values unchanged.");
    }

    private void OnDestroy() => policy?.Restore();
}
