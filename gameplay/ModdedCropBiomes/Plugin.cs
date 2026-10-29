using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using Jotunn.Utils;
using UnityEngine;

namespace AdventureRpg.ModdedCropBiomes;

[BepInPlugin(Guid, "Adventure RPG - Modded Crop Biomes", "1.0.0")]
[BepInDependency("advize.PlantEverything", "1.21.4")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.moddedcropbiomes";
    private Harmony? harmony;

    private void Awake()
    {
        if (Chainloader.PluginInfos["advize.PlantEverything"].Metadata.Version != new System.Version(1, 21, 4))
            throw new NotSupportedException("Modded Crop Biomes 1.0.0 requires reviewed PlantEverything 1.21.4.");
        CropBiomes.Validate();
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(Plugin).Assembly); }
        catch { harmony.UnpatchSelf(); throw; }
        Logger.LogInfo("Modded crops now follow PlantEverything EnforceBiomesVanilla without enabling crop stat overrides.");
    }

    private void OnDestroy() => harmony?.UnpatchSelf();

    [HarmonyPatch]
    private static class FinishCropInitialization
    {
        private static MethodBase TargetMethod() => CropBiomes.InitCrops;
        [HarmonyPriority(Priority.Last)]
        internal static void Postfix() => CropBiomes.Apply();
    }
}

internal static class CropBiomes
{
    private static Type Type(string name) => AccessTools.TypeByName("Advize_PlantEverything." + name)
        ?? throw new MissingMemberException("Missing PlantEverything type " + name);
    private static FieldInfo Field(Type type, string name) => AccessTools.Field(type, name)
        ?? throw new MissingFieldException(type.FullName, name);
    private static PropertyInfo Property(Type type, string name) => AccessTools.Property(type, name)
        ?? throw new MissingMemberException(type.FullName, name);

    private static readonly Type Members = Type("StaticMembers");
    private static readonly FieldInfo Config = Field(Members, "config");
    private static readonly FieldInfo Crops = Field(Members, "moddedCropRefs");
    private static readonly FieldInfo AllBiomes = Field(Members, "AllBiomes");
    private static readonly PropertyInfo EnforceBiomes = Property(Type("ModConfig"), "EnforceBiomesVanilla");
    private static readonly PropertyInfo Prefab = Property(Type("PrefabDB"), "Prefab");
    internal static readonly MethodInfo InitCrops = AccessTools.Method(Type("PluginInitUtils"), "InitCrops")
        ?? throw new MissingMethodException("PlantEverything InitCrops");
    // Only prefab pieces are retained, never world instances. Restore their original
    // placement restriction if the existing server-synced biome option is enabled.
    private static readonly Dictionary<Piece, Heightmap.Biome> OriginalPlacementBiomes = new();

    internal static void Validate()
    {
        if (Config.GetValue(null) == null || EnforceBiomes.PropertyType != typeof(bool)
            || AllBiomes.FieldType != typeof(Heightmap.Biome) || InitCrops.GetParameters().Length != 0)
            throw new NotSupportedException("Unexpected PlantEverything configuration API.");
    }

    internal static void Apply()
    {
        bool enforce = (bool)EnforceBiomes.GetValue(Config.GetValue(null), null)!;
        var mask = (Heightmap.Biome)AllBiomes.GetValue(null)!;
        foreach (object crop in (IEnumerable)Crops.GetValue(null)!)
        {
            var prefab = (GameObject?)Prefab.GetValue(crop, null);
            if (prefab == null) continue;
            var plant = prefab.GetComponent<Plant>();
            if (plant == null) continue;
            var piece = prefab.GetComponent<Piece>();
            if (piece != null)
            {
                if (!OriginalPlacementBiomes.TryGetValue(piece, out var original))
                    OriginalPlacementBiomes.Add(piece, original = piece.m_onlyInBiome);
                piece.m_onlyInBiome = enforce ? original : Heightmap.Biome.None;
            }
            // Upstream InitCrops has already restored the authored biome when
            // enforcement is enabled. All other crop fields remain untouched.
            if (!enforce) plant.m_biome = mask;
        }
    }
}
