using System.Runtime.CompilerServices;
using System.Security.Cryptography;

[assembly: InternalsVisibleTo("AdventureLauncher.Bootstrap.Tests")]

namespace AdventureLauncher.Core;

public sealed record BootstrapInstallResult(int WrittenFiles, int MigratedFiles, string? BackupPath);

/// <summary>Installs signed bootstrap files with the game closed; preserves an existing game INI.</summary>
public static class BootstrapInstaller
{
    private const string NewPackage = "denikson-BepInExPack_Valheim-5.4.2360";
    // Exact official binaries previously shipped in the signed .31.3 collection.
    // A foreign loader is never treated as an upgrade candidate.
    private static readonly IReadOnlyDictionary<string, (string Old, string New)> KnownTransitions =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["winhttp.dll"] = ("93406d0a02e7c164b89828cbfe3b289930a112d2eca50bd4a52e72ece169e6a8", "0f39399eba05dea9419781187ae2a6b0555afcdf1859f1319b6213be7729013b"),
            [".doorstop_version"] = ("84fa42545c0993edf8a5559d2a0772260f85f4a7ed29ec75e6590be64ed98067", "aab86e1db7618ee7d82b374862d73b7494e0267fa201410012a775de66f5eaba")
        };

    public static void Validate(PackManifest manifest, string game) => _ = ReadPlan(manifest, game);

    public static BootstrapInstallResult Ensure(PackManifest manifest, LauncherSettings settings) =>
        EnsureCore(manifest, settings, null);

    internal static BootstrapInstallResult EnsureCore(PackManifest manifest, LauncherSettings settings, Action<int>? afterWrite)
    {
        var game = Path.GetFullPath(settings.GamePath);
        var profile = Path.GetFullPath(settings.ProfilePath);
        if (Overlaps(PhysicalRoot(game), PhysicalRoot(profile)))
            throw new InvalidOperationException("Game and profile must use separate folders.");
        var plan = ReadPlan(manifest, game);
        // Verify the complete staged bootstrap subset before creating backups or
        // changing the game, including the INI that may be preserved in the game.
        foreach (var entry in plan)
        {
            entry.Source = EngineStorage.InProfile(profile, entry.File.Path);
            if (!File.Exists(entry.Source) || new FileInfo(entry.Source).Length != entry.File.Size ||
                !Equal(Hash(entry.Source), entry.File.Sha256))
                throw new IOException("The profile loader bootstrap does not match the signed collection (" + entry.Name + "). Nothing was changed.");
        }
        var changes = plan.Where(e => e.NeedsWrite).ToArray();
        if (changes.Length == 0) return new(0, 0, null);

        var backup = EngineStorage.InProfile(game, ".arpg-bootstrap-backups/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        var journal = Path.Combine(backup, "transaction.json");
        var state = new BootstrapTransaction { Version = manifest.Version, Entries = changes.Select(e =>
            new BootstrapEntry { Name = e.Name, BeforeHash = e.BeforeHash, AfterHash = e.File.Sha256,
                BeforeLastWriteTicks = e.BeforeLastWriteTicks }).ToList() };
        EngineStorage.AtomicJson(journal, state);
        foreach (var entry in changes)
        {
            var staged = EngineStorage.InProfile(backup, "stage/" + entry.Name);
            EngineStorage.CopyDurable(entry.Source!, staged);
            if (!Equal(Hash(staged), entry.File.Sha256)) throw new IOException("Loader staging checksum mismatch.");
            entry.Staged = staged;
            if (entry.BeforeHash != null)
            {
                var copy = EngineStorage.InProfile(backup, "before/" + entry.Name);
                EngineStorage.CopyDurable(entry.Target, copy);
                if (!Equal(Hash(copy), entry.BeforeHash)) throw new IOException("Loader changed during backup. Nothing was installed.");
            }
        }
        // Recheck every original native file, including the preserved INI, after
        // staging; a concurrent external loader change must not be overwritten.
        foreach (var entry in plan) RequireBefore(entry);
        state.Status = "prepared";
        EngineStorage.AtomicJson(journal, state);
        var applied = new List<PlannedFile>();
        try
        {
            state.Status = "applying";
            EngineStorage.AtomicJson(journal, state);
            foreach (var entry in changes)
            {
                RequireBefore(entry);
                applied.Add(entry);
                EngineStorage.CopyDurable(entry.Staged!, entry.Target);
                if (!Equal(Hash(entry.Target), entry.File.Sha256)) throw new IOException("Installed loader checksum mismatch.");
                state.Entries.Single(e => e.Name == entry.Name).Applied = true;
                EngineStorage.AtomicJson(journal, state);
                afterWrite?.Invoke(applied.Count);
            }
            state.Status = "committed";
            EngineStorage.AtomicJson(journal, state);
            return new(applied.Count, applied.Count(e => e.BeforeHash != null), backup);
        }
        catch (Exception failure)
        {
            try
            {
                // Complete rollback preflight before restoring any file. Never
                // delete/replace a file edited externally after our atomic write.
                foreach (var entry in applied)
                {
                    var actual = ExistingHash(entry.Target);
                    if (!Equal(actual, entry.BeforeHash) && !Equal(actual, entry.File.Sha256))
                        throw new IOException("Loader rollback found an external edit: " + entry.Name);
                    if (entry.BeforeHash != null && !Equal(Hash(EngineStorage.InProfile(backup, "before/" + entry.Name)), entry.BeforeHash))
                        throw new IOException("Loader rollback backup checksum mismatch: " + entry.Name);
                }
                foreach (var entry in applied.AsEnumerable().Reverse())
                {
                    if (Equal(ExistingHash(entry.Target), entry.BeforeHash)) continue;
                    if (entry.BeforeHash == null) File.Delete(entry.Target);
                    else
                    {
                        EngineStorage.CopyDurable(EngineStorage.InProfile(backup, "before/" + entry.Name), entry.Target);
                        File.SetLastWriteTimeUtc(entry.Target, new DateTime(entry.BeforeLastWriteTicks, DateTimeKind.Utc));
                    }
                    if (!Equal(ExistingHash(entry.Target), entry.BeforeHash)) throw new IOException("Loader rollback verification failed: " + entry.Name);
                }
                state.Status = "rolled-back";
                EngineStorage.AtomicJson(journal, state);
            }
            catch (Exception rollbackFailure)
            {
                state.Status = "rollback-conflict";
                EngineStorage.AtomicJson(journal, state);
                throw new AggregateException("Loader installation failed; backups were retained and an external change blocked rollback.", failure, rollbackFailure);
            }
            throw;
        }
    }

    private static List<PlannedFile> ReadPlan(PackManifest manifest, string game)
    {
        if (string.IsNullOrWhiteSpace(game) || !Directory.Exists(game)) throw new DirectoryNotFoundException("Game folder is missing.");
        var plan = new List<PlannedFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files.Where(f => f.Path.StartsWith("bootstrap/", StringComparison.OrdinalIgnoreCase)))
        {
            var name = file.Path["bootstrap/".Length..];
            if (name.Contains('/') || name.Contains('\\') || name is not ("winhttp.dll" or "doorstop_config.ini" or ".doorstop_version") || !seen.Add(name))
                throw new InvalidOperationException("Unsupported loader bootstrap file.");
            var target = EngineStorage.InProfile(game, name);
            if (Directory.Exists(target)) throw new IOException("A loader file path is occupied by a directory: " + name);
            var before = ExistingHash(target);
            bool preserved = name == "doorstop_config.ini" && before != null;
            if (!preserved && before != null && !Equal(before, file.Sha256) &&
                !(file.ArtifactId == NewPackage && KnownTransitions.TryGetValue(name, out var pins) &&
                  Equal(before, pins.Old) && Equal(file.Sha256, pins.New)))
                throw new InvalidOperationException("A different mod loader exists in the game folder (" + name + "). Nothing was changed.");
            plan.Add(new PlannedFile { File = file, Name = name, Target = target, BeforeHash = before,
                BeforeLastWriteTicks = before == null ? 0 : File.GetLastWriteTimeUtc(target).Ticks,
                NeedsWrite = !preserved && !Equal(before, file.Sha256) });
        }
        return plan;
    }

    private static void RequireBefore(PlannedFile entry)
    {
        // Re-resolve the path to reject a new nested reparse point as well.
        _ = EngineStorage.InProfile(Path.GetDirectoryName(entry.Target)!, entry.Name);
        if (!Equal(ExistingHash(entry.Target), entry.BeforeHash))
            throw new IOException("Loader changed after preflight: " + entry.Name);
    }
    private static string PhysicalRoot(string root) => Path.GetFullPath(new DirectoryInfo(root).ResolveLinkTarget(true)?.FullName ?? root)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    private static bool Overlaps(string a, string b) => Equal(a, b) || a.StartsWith(b + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || b.StartsWith(a + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool Equal(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string? ExistingHash(string path) => File.Exists(path) ? Hash(path) : null;
    private static string Hash(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }
    private sealed class PlannedFile
    {
        public required PackFile File { get; init; }
        public required string Name { get; init; }
        public required string Target { get; init; }
        public string? Source { get; set; }
        public string? Staged { get; set; }
        public string? BeforeHash { get; init; }
        public long BeforeLastWriteTicks { get; init; }
        public bool NeedsWrite { get; init; }
    }
    private sealed class BootstrapTransaction
    {
        public string Status { get; set; } = "preparing";
        public string Version { get; set; } = "";
        public List<BootstrapEntry> Entries { get; set; } = [];
    }
    private sealed class BootstrapEntry
    {
        public string Name { get; set; } = "";
        public string? BeforeHash { get; set; }
        public string AfterHash { get; set; } = "";
        public long BeforeLastWriteTicks { get; set; }
        public bool Applied { get; set; }
    }
}
