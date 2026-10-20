using System.Text.Json;

namespace AdventureLauncher.Core;

public sealed class PackManifest
{
    public int SchemaVersion { get; set; } = 1;
    public string PackId { get; set; } = "adventure-rpg";
    public string Title { get; set; } = "Valheim Adventure RPG";
    public string Version { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string Notes { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<PackArtifact> Artifacts { get; set; } = [];
    public List<PackFile> Files { get; set; } = [];
}
public sealed class PackArtifact
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "zip"; // zip, 7z, raw
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public List<string> Urls { get; set; } = [];
    public string? ManualUrl { get; set; }
    public string? Instructions { get; set; }
}
public sealed class PackFile
{
    public string Path { get; set; } = ""; // portable, profile-relative; bootstrap/ is copied to game only by client bootstrap service
    public string ArtifactId { get; set; } = "";
    public string Entry { get; set; } = ""; // archive member; empty for raw
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Policy { get; set; } = "managed"; // managed, config, preserve
    public List<string> PreserveKeys { get; set; } = []; // config keys as Section/Key
}
public sealed class SignedEnvelope
{
    public string KeyId { get; set; } = "release-v1";
    public string Payload { get; set; } = ""; // base64 exact UTF-8 manifest JSON bytes
    public string Signature { get; set; } = ""; // base64 ECDSA-P256 SHA256 DER
}
public static class PackJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
}
public sealed class EngineOptions
{
    public bool AllowLocalSources { get; init; }
    public string? TarExecutable { get; init; }
}
public sealed record PackProgress(string Phase, string Message, long Completed = 0, long Total = 0);
public sealed class PackPlan
{
    public string Version { get; init; } = "";
    public List<string> ChangedFiles { get; init; } = [];
    public List<string> RemovedFiles { get; init; } = [];
    public List<string> PreservedFiles { get; init; } = [];
    public List<string> RequiredArtifactIds { get; init; } = [];
    public long DownloadBytes { get; init; }
    public bool IsCurrent => ChangedFiles.Count == 0 && RemovedFiles.Count == 0;
}
public sealed record EngineResult(string Version, int WrittenFiles, int RemovedFiles, bool Recovered = false);
public sealed class ManualArtifactRequiredException(PackArtifact artifact, string? reason = null)
    : Exception($"{(string.IsNullOrWhiteSpace(reason) ? "" : reason + " ")}Import the original archive for {artifact.FileName}. {artifact.Instructions}")
{
    public PackArtifact Artifact { get; } = artifact;
}
