using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using EpicLoot.ShardStones;
using Jotunn.Utils;

namespace AdventureRpg.ShardCategoryFreedom;

[BepInPlugin(Guid, "Adventure RPG - Shard Category Freedom", "1.0.0")]
[BepInDependency("randyknapp.mods.epicloot", "0.14.13")]
[BepInDependency("com.jotunn.jotunn")]
[NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Patch)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "arpg.shardcategoryfreedom";
    private CategoryOverride? categoryOverride;

    private void Awake()
    {
        if (Chainloader.PluginInfos["randyknapp.mods.epicloot"].Metadata.Version != new System.Version(0, 14, 13))
            throw new NotSupportedException("Shard Category Freedom 1.0.0 requires reviewed EpicLoot 0.14.13.");
        var field = typeof(Shards).GetField("ExclusiveCategories", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingFieldException("Pinned EpicLoot exclusive-category registry missing.");
        if (field.GetValue(null) is not HashSet<ShardCategory> categories)
            throw new NotSupportedException("Pinned EpicLoot exclusive-category registry has changed.");
        categoryOverride = new CategoryOverride(categories);
        Logger.LogInfo("Boss and Unique shard category limits removed; socket counts, duplicate-effect rules and values unchanged.");
    }

    private void OnDestroy() => categoryOverride?.Restore();
}

internal sealed class CategoryOverride
{
    private readonly HashSet<ShardCategory> categories;
    private readonly bool removedBoss;
    private readonly bool removedUnique;
    private bool restored;

    internal CategoryOverride(HashSet<ShardCategory> categories)
    {
        this.categories = categories;
        removedBoss = categories.Remove(ShardCategory.Boss);
        removedUnique = categories.Remove(ShardCategory.Unique);
    }

    internal void Restore()
    {
        if (restored) return;
        if (removedBoss) categories.Add(ShardCategory.Boss);
        if (removedUnique) categories.Add(ShardCategory.Unique);
        restored = true;
    }
}
