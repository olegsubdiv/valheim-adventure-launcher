using System.Text.Json;

namespace AdventureLauncher.Core;

public static class TextureVariants
{
    public const string Medium = "medium";
    public const string Higher = "higher";
    public const string HigherArtifactId = "Willybach-HDValheim_Higher-2.0.0";
    public const string MediumArtifactId = "Willybach-HDValheim_Medium-2.0.0";
    private const string PluginPath = "BepInEx/plugins/HDValheimTextures.dll";
    private const string DataPath = "BepInEx/plugins/HDValheimTexturesData/";

    public static string ResolveTextureQuality(string profile, string? requested = null)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Normalize(requested);
        if (string.IsNullOrWhiteSpace(profile)) return Medium;
        // Also used while the editable profile field contains an incomplete path.
        try
        {
            var state = EngineStorage.ReadJson<InstalledState>(EngineStorage.InProfile(profile, ".arpg/state.json"));
            if (!string.IsNullOrWhiteSpace(state?.TextureQuality)) return Normalize(state.TextureQuality);
            return File.Exists(EngineStorage.InProfile(profile, PluginPath)) ? Higher : Medium;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return Medium;
        }
    }

    public static string Normalize(string quality) => quality.Trim().ToLowerInvariant() switch
    {
        Medium => Medium,
        Higher => Higher,
        _ => throw new InvalidDataException("Choose Medium or Higher textures.")
    };

    // Both inputs must first pass ManifestSigning.Verify with the bundled release key.
    // The baseline remains compatible with old launchers, which keep installing Higher.
    public static PackManifest Compose(PackManifest baseline, string quality, PackManifest? variant = null)
    {
        ManifestValidation.Validate(baseline);
        quality = Normalize(quality);
        if (baseline.PackId != "adventure-rpg" || baseline.TextureVariant != null || baseline.TextureQuality != null)
            throw new InvalidDataException("Texture variants require the original collection manifest.");
        var result = JsonSerializer.Deserialize<PackManifest>(JsonSerializer.Serialize(baseline, PackJson.Options), PackJson.Options)!;
        result.TextureQuality = quality;
        if (quality == Higher) return result;
        if (variant == null) throw new InvalidDataException("The Medium texture manifest is missing.");
        ManifestValidation.Validate(variant);
        var scope = variant.TextureVariant;
        var higher = baseline.Artifacts.SingleOrDefault(a => a.Id == HigherArtifactId);
        if (higher == null || scope == null || scope.Id != Medium || scope.BaseArtifactId != higher.Id ||
            !scope.BaseArtifactSha256.Equals(higher.Sha256, StringComparison.OrdinalIgnoreCase) ||
            variant.PackId != "adventure-rpg-textures-medium" || variant.TextureQuality != null ||
            variant.Artifacts.Count != 1 || variant.Artifacts[0].Id != MediumArtifactId ||
            variant.Artifacts[0].Kind != "7z")
            throw new InvalidDataException("The Medium texture manifest does not match this collection.");
        var higherFiles = baseline.Files.Where(f => f.ArtifactId.Equals(higher.Id, StringComparison.OrdinalIgnoreCase)).ToList();
        if (higherFiles.Count == 0 || higherFiles.Any(f => !InScope(f)))
            throw new InvalidDataException("The baseline texture inventory has an invalid scope.");
        if (variant.Files.Count == 0 || variant.Files.Any(f => !InScope(f)) ||
            !variant.Files.Any(f => ManifestValidation.Normalize(f.Path).Equals(PluginPath, StringComparison.OrdinalIgnoreCase)) ||
            !variant.Files.Any(f => ManifestValidation.Normalize(f.Path).Equals(DataPath + "Textures.dat", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Medium may only replace the complete HD texture plugin and its data.");
        var replacementPaths = new HashSet<string>(variant.Files.Select(f => ManifestValidation.Normalize(f.Path)), StringComparer.OrdinalIgnoreCase);
        var otherPaths = new HashSet<string>(baseline.Files.Except(higherFiles).Select(f => ManifestValidation.Normalize(f.Path)), StringComparer.OrdinalIgnoreCase);
        if (replacementPaths.Overlaps(otherPaths)) throw new InvalidDataException("Medium cannot replace files owned by another package.");
        result.Artifacts.RemoveAll(a => a.Id.Equals(higher.Id, StringComparison.OrdinalIgnoreCase));
        result.Files.RemoveAll(f => f.ArtifactId.Equals(higher.Id, StringComparison.OrdinalIgnoreCase));
        result.Artifacts.AddRange(variant.Artifacts);
        result.Files.AddRange(variant.Files);
        result.LegacyTextureFiles = higherFiles.Where(f => !replacementPaths.Contains(ManifestValidation.Normalize(f.Path))).ToList();
        result.Version = baseline.Version + "+textures.medium." + variant.Version;
        ManifestValidation.Validate(result);
        return result;
    }

    private static bool InScope(PackFile file)
    {
        return file.Policy == "managed" && file.PreserveKeys.Count == 0 &&
            IsTexturePath(file.Path);
    }
    internal static bool IsTexturePath(string path) =>
        ManifestValidation.Normalize(path).Equals(PluginPath, StringComparison.OrdinalIgnoreCase) ||
        ManifestValidation.Normalize(path).StartsWith(DataPath, StringComparison.OrdinalIgnoreCase);
}
