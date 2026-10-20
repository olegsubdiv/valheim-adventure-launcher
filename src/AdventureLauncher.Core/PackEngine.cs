using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace AdventureLauncher.Core;

public sealed class PackEngine(HttpClient http, EngineOptions? options = null, IProgress<PackProgress>? progress = null)
{
    private readonly EngineOptions _options = options ?? new();

    public async Task<PackManifest> FetchAsync(string location, string publicKeyPem, CancellationToken ct = default)
    {
        string json;
        if (ArtifactTransfer.IsLocal(location))
        {
            if (!_options.AllowLocalSources) throw new InvalidDataException("Local release sources are disabled.");
            var path = Uri.TryCreate(location, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : location;
            if (new FileInfo(path).Length > 24 * 1024 * 1024) throw new InvalidDataException("Manifest is too large.");
            json = await File.ReadAllTextAsync(path, ct);
        }
        else
        {
            var uri = ArtifactTransfer.ValidateUri(location, _options.AllowLocalSources);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var output = new MemoryStream();
            var buffer = new byte[65536];
            while (true)
            {
                var read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
                if (output.Length + read > 24 * 1024 * 1024) throw new InvalidDataException("Manifest is too large.");
                output.Write(buffer, 0, read);
            }
            json = Encoding.UTF8.GetString(output.ToArray());
        }
        return ManifestSigning.Verify(json, publicKeyPem);
    }

    public async Task<PackPlan> InspectAsync(PackManifest manifest, string profile, bool fullHash = false,
        CancellationToken ct = default)
    {
        ManifestValidation.Validate(manifest);
        if (!Directory.Exists(profile))
            return (await AnalyzeAsync(manifest, profile, null, fullHash, ct)).Plan;
        using var operation = Lock(profile);
        if (File.Exists(Meta(profile, "transaction.json")))
            throw new IOException("An interrupted update must be recovered before checking this profile.");
        var state = ReadState(profile);
        var analysis = await AnalyzeAsync(manifest, profile, state, fullHash, ct);
        if (fullHash) EngineStorage.AtomicJson(Meta(profile, "repair-pending.json"), analysis.Plan.ChangedFiles);
        return analysis.Plan;
    }

    public async Task<EngineResult> ApplyAsync(PackManifest manifest, string profile, string cacheDirectory,
        IReadOnlyDictionary<string, string>? localArtifacts = null, CancellationToken ct = default)
    {
        ManifestValidation.Validate(manifest);
        EnsureGameClosed(profile);
        Directory.CreateDirectory(profile);
        using var operation = Lock(profile);
        var recovered = await RecoverCoreAsync(profile, ct);
        var previous = ReadState(profile);
        var analysis = await AnalyzeAsync(manifest, profile, previous, false, ct);
        if (previous != null && analysis.Plan.IsCurrent && previous.Version == manifest.Version)
        {
            EngineStorage.AtomicJson(Meta(profile, "state.json"), await CaptureStateAsync(manifest, profile, analysis.Observed, ct));
            File.Delete(Meta(profile, "repair-pending.json"));
            return new(manifest.Version, 0, 0, recovered);
        }
        // Ask for every missing/manual archive before spending time or bandwidth on catalogue packages.
        var transfer = new ArtifactTransfer(http, _options, progress);
        var requiredIds = new HashSet<string>(analysis.Plan.RequiredArtifactIds, StringComparer.OrdinalIgnoreCase);
        await transfer.PreflightManualAsync(manifest.Artifacts.Where(a => requiredIds.Contains(a.Id)), cacheDirectory, localArtifacts, ct);        var id = Guid.NewGuid().ToString("N");
        var stage = Meta(profile, "staging/" + id);
        var stageFiles = Path.Combine(stage, "files");
        Directory.CreateDirectory(stageFiles);
        Transaction? transaction = null;
        try
        {
            CheckSpace(profile, manifest, analysis.Plan);
            var changes = new HashSet<string>(analysis.Plan.ChangedFiles, StringComparer.OrdinalIgnoreCase);
            var needed = manifest.Files.Where(f => changes.Contains(ManifestValidation.Normalize(f.Path))).ToList();
            foreach (var artifactId in analysis.Plan.RequiredArtifactIds)
            {
                var artifact = manifest.Artifacts.Single(a => a.Id.Equals(artifactId, StringComparison.OrdinalIgnoreCase));
                var archive = await transfer.GetAsync(artifact, cacheDirectory, localArtifacts, ct);
                var files = needed.Where(f => f.ArtifactId.Equals(artifactId, StringComparison.OrdinalIgnoreCase)).ToList();
                progress?.Report(new("extract", $"Preparing {artifact.FileName}"));
                await ExtractAsync(archive, artifact, files, stageFiles, stage, ct);
            }
            // Configuration policy is applied only after the signed source bytes were verified.
            foreach (var file in needed.Where(f => f.Policy == "config"))
            {
                var current = EngineStorage.InProfile(profile, file.Path);
                if (!File.Exists(current)) continue;
                var staged = EngineStorage.InProfile(stageFiles, file.Path);
                var merged = ConfigMerge.Apply(await File.ReadAllTextAsync(staged, ct),
                    await File.ReadAllTextAsync(current, ct), file.PreserveKeys);
                await File.WriteAllTextAsync(staged, merged, new UTF8Encoding(false), ct);
            }
            ct.ThrowIfCancellationRequested();
            transaction = await PrepareTransactionAsync(profile, manifest.Version, analysis.Plan, stageFiles, ct);
            transaction.Status = "applying";
            EngineStorage.AtomicJson(Meta(profile, "transaction.json"), transaction);
            foreach (var entry in transaction.Entries)
            {
                ct.ThrowIfCancellationRequested();
                EnsureGameClosed(profile);
                var target = EngineStorage.InProfile(profile, entry.Path);
                if (entry.Existed && (!File.Exists(target) ||
                    !(await EngineStorage.HashAsync(target, ct)).Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase)))
                    throw new IOException($"The file changed during the update: {entry.Path}");
                if (!entry.Existed && File.Exists(target))
                    throw new IOException($"A file appeared during the update: {entry.Path}");
                // Persist intent before mutation; recovery is idempotent even if this mutation never happened.
                entry.Applied = true;
                EngineStorage.AtomicJson(Meta(profile, "transaction.json"), transaction);
                if (entry.AfterHash == null) File.Delete(target);
                else EngineStorage.CopyDurable(EngineStorage.InProfile(stageFiles, entry.Path), target);
                progress?.Report(new("apply", entry.Path, transaction.Entries.Count(e => e.Applied), transaction.Entries.Count));
            }
            var next = await CaptureStateAsync(manifest, profile, analysis.Observed, ct);
            EngineStorage.AtomicJson(Meta(profile, "state.json"), next);
            transaction.Status = "committed";
            EngineStorage.AtomicJson(Meta(profile, "transaction.json"), transaction);
            CompleteTransaction(profile, transaction);
            File.Delete(Meta(profile, "repair-pending.json"));
            CleanupBackups(profile, transaction.BackupId);
            return new(manifest.Version, analysis.Plan.ChangedFiles.Count, analysis.Plan.RemovedFiles.Count, recovered);
        }
        catch
        {
            if (transaction != null && File.Exists(Meta(profile, "transaction.json")))
            {
                // Cancellation must not interrupt restoration of already modified files.
                await RestoreAsync(profile, transaction, CancellationToken.None);
                File.Delete(Meta(profile, "transaction.json"));
            }
            throw;
        }
        finally
        {
            try { EngineStorage.SafeDeleteTree(Meta(profile, ""), stage); }
            catch (IOException ex) { progress?.Report(new("warning", $"Staging cleanup: {ex.Message}")); }
        }
    }

    public async Task<EngineResult> RollbackAsync(string profile, CancellationToken ct = default)
    {
        EnsureGameClosed(profile);
        using var operation = Lock(profile);
        var recovered = await RecoverCoreAsync(profile, ct);
        var last = EngineStorage.ReadJson<Transaction>(Meta(profile, "last-backup.json"));
        if (last == null || last.Status != "committed") throw new IOException("No previous release is available to restore.");
        ValidateTransaction(last);
        last.Status = "rolling-back";
        EngineStorage.AtomicJson(Meta(profile, "transaction.json"), last);
        await RestoreAsync(profile, last, CancellationToken.None);
        last.Status = "rolled-back";
        EngineStorage.AtomicJson(Meta(profile, "last-backup.json"), last);
        File.Delete(Meta(profile, "transaction.json"));
        var state = ReadState(profile);
        return new(state?.Version ?? "", 0, 0, recovered);
    }

    public async Task<bool> RecoverAsync(string profile, CancellationToken ct = default)
    {
        if (!Directory.Exists(profile)) return false;
        EnsureGameClosed(profile);
        using var operation = Lock(profile);
        return await RecoverCoreAsync(profile, ct);
    }

    private static string Meta(string profile, string relative) =>
        EngineStorage.InProfile(profile, ".arpg" + (relative.Length == 0 ? "" : "/" + relative));

    private static FileStream Lock(string profile)
    {
        var metadata = Meta(profile, "");
        Directory.CreateDirectory(metadata);
        return new FileStream(Meta(profile, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private static InstalledState? ReadState(string profile)
    {
        var state = EngineStorage.ReadJson<InstalledState>(Meta(profile, "state.json"));
        if (state == null) return null;
        if (state.SchemaVersion != 1 || state.Files == null) throw new InvalidDataException("Unsupported installed state.");
        state.Files = new Dictionary<string, InstalledFile>(state.Files, StringComparer.OrdinalIgnoreCase);
        foreach (var path in state.Files.Keys) EngineStorage.InProfile(profile, path);
        return state;
    }

    private async Task<Analysis> AnalyzeAsync(PackManifest manifest, string profile, InstalledState? state,
        bool fullHash, CancellationToken ct)
    {
        if (state != null && state.PackId != manifest.PackId) throw new IOException("The selected folder belongs to another modpack.");
        var changed = new List<string>();
        var removed = new List<string>();
        var preserved = new List<string>();
        var observed = new Dictionary<string, InstalledFile>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var forceHash = new HashSet<string>(EngineStorage.ReadJson<List<string>>(Meta(profile, "repair-pending.json")) ?? [], StringComparer.OrdinalIgnoreCase);
        var index = 0;
        foreach (var file in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            var path = ManifestValidation.Normalize(file.Path);
            destinations.Add(path);
            var target = EngineStorage.InProfile(profile, path);
            var info = new FileInfo(target);
            InstalledFile? prior = null;
            if (state != null) state.Files.TryGetValue(path, out prior);
            if (!info.Exists) changed.Add(path);
            else if (file.Policy == "preserve" ||
                file.Policy == "config" && prior?.SourceHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) == true)
            {
                var actual = await ObserveAsync(target, file, prior, fullHash, ct);
                observed[path] = actual;
                preserved.Add(path);
            }
            else if (!fullHash && !forceHash.Contains(path) && prior != null && prior.SourceHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase) &&
                prior.Policy == file.Policy && prior.Size == info.Length && prior.LastWriteTicks == info.LastWriteTimeUtc.Ticks)
                observed[path] = prior;
            else
            {
                var hash = await EngineStorage.HashAsync(target, ct);
                if (info.Length == file.Size && hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    observed[path] = new InstalledFile { SourceHash = file.Sha256, ActualHash = hash, Size = info.Length,
                        LastWriteTicks = info.LastWriteTimeUtc.Ticks, Policy = file.Policy };
                else changed.Add(path);
            }
            progress?.Report(new("verify", path, ++index, manifest.Files.Count));
        }
        if (state != null)
        {
            foreach (var (path, prior) in state.Files)
            {
                if (destinations.Contains(path)) continue;
                var target = EngineStorage.InProfile(profile, path);
                if (!File.Exists(target)) continue;
                if (await EngineStorage.MatchesAsync(target, prior.Size, prior.ActualHash, ct)) removed.Add(path);
                else preserved.Add(path);
            }
        }
        var changedSet = new HashSet<string>(changed, StringComparer.OrdinalIgnoreCase);
        var artifactIds = manifest.Files.Where(f => changedSet.Contains(ManifestValidation.Normalize(f.Path)))
            .Select(f => f.ArtifactId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var bytes = manifest.Artifacts.Where(a => artifactIds.Contains(a.Id, StringComparer.OrdinalIgnoreCase))
            .Aggregate(0L, (sum, artifact) => checked(sum + artifact.Size));
        return new(new PackPlan { Version = manifest.Version, ChangedFiles = changed, RemovedFiles = removed,
            PreservedFiles = preserved, RequiredArtifactIds = artifactIds, DownloadBytes = bytes }, observed);
    }

    private static async Task<InstalledFile> ObserveAsync(string target, PackFile source, InstalledFile? prior,
        bool fullHash, CancellationToken ct)
    {
        var info = new FileInfo(target);
        if (!fullHash && prior != null && info.Length == prior.Size && info.LastWriteTimeUtc.Ticks == prior.LastWriteTicks &&
            prior.SourceHash.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
            return prior;
        return new InstalledFile { SourceHash = source.Sha256, ActualHash = await EngineStorage.HashAsync(target, ct),
            Size = info.Length, LastWriteTicks = info.LastWriteTimeUtc.Ticks, Policy = source.Policy };
    }

    private static async Task<InstalledState> CaptureStateAsync(PackManifest manifest, string profile,
        Dictionary<string, InstalledFile> observed, CancellationToken ct)
    {
        var state = new InstalledState { PackId = manifest.PackId, Version = manifest.Version };
        foreach (var file in manifest.Files)
        {
            var path = ManifestValidation.Normalize(file.Path);
            var target = EngineStorage.InProfile(profile, path);
            if (!File.Exists(target)) throw new IOException($"Installed file missing: {path}");
            observed.TryGetValue(path, out var prior);
            state.Files[path] = await ObserveAsync(target, file, prior, false, ct);
        }
        return state;
    }

    private static async Task<Transaction> PrepareTransactionAsync(string profile, string version, PackPlan plan,
        string stageFiles, CancellationToken ct)
    {
        var transaction = new Transaction { BackupId = Guid.NewGuid().ToString("N"), Version = version,
            HadState = File.Exists(Meta(profile, "state.json")) };
        var backup = Meta(profile, "backups/" + transaction.BackupId);
        Directory.CreateDirectory(backup);
        if (transaction.HadState) EngineStorage.CopyDurable(Meta(profile, "state.json"), Path.Combine(backup, "state.json"));
        var changed = new HashSet<string>(plan.ChangedFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var path in plan.ChangedFiles.Concat(plan.RemovedFiles))
        {
            ct.ThrowIfCancellationRequested();
            var target = EngineStorage.InProfile(profile, path);
            var entry = new TransactionEntry { Path = path, Existed = File.Exists(target) };
            if (entry.Existed)
            {
                var copy = EngineStorage.InProfile(backup, "files/" + path);
                entry.BeforeHash = await EngineStorage.HashAsync(target, ct);
                EngineStorage.CopyDurable(target, copy);
                if (!(await EngineStorage.HashAsync(copy, ct)).Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"File changed while making a backup: {path}");
            }
            if (changed.Contains(path)) entry.AfterHash = await EngineStorage.HashAsync(EngineStorage.InProfile(stageFiles, path), ct);
            transaction.Entries.Add(entry);
        }
        return transaction;
    }

    private async Task<bool> RecoverCoreAsync(string profile, CancellationToken ct)
    {
        var journal = Meta(profile, "transaction.json");
        var transaction = EngineStorage.ReadJson<Transaction>(journal);
        if (transaction == null) return false;
        ValidateTransaction(transaction);
        if (transaction.Status == "committed")
        {
            CompleteTransaction(profile, transaction);
            return true;
        }
        progress?.Report(new("recover", "Restoring the last complete profile after an interrupted update."));
        await RestoreAsync(profile, transaction, ct);
        File.Delete(journal);
        return true;
    }

    private static void ValidateTransaction(Transaction transaction)
    {
        if (!Guid.TryParseExact(transaction.BackupId, "N", out _) || transaction.Entries == null)
            throw new InvalidDataException("Invalid recovery journal.");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in transaction.Entries)
        {
            ManifestValidation.ValidateRelativePath(entry.Path);
            if (entry.Path.Equals(".arpg", StringComparison.OrdinalIgnoreCase) ||
                entry.Path.StartsWith(".arpg/", StringComparison.OrdinalIgnoreCase) || !unique.Add(entry.Path))
                throw new InvalidDataException("Invalid recovery destination.");
        }
    }

    private static async Task RestoreAsync(string profile, Transaction transaction, CancellationToken ct)
    {
        ValidateTransaction(transaction);
        var backup = Meta(profile, "backups/" + transaction.BackupId);
        EnsureGameClosed(profile);
        // Preflight the entire restoration before changing any file, including externally modified files.
        foreach (var entry in transaction.Entries.Where(e => e.Applied))
        {
            var target = EngineStorage.InProfile(profile, entry.Path);
            var current = File.Exists(target) ? await EngineStorage.HashAsync(target, ct) : null;
            if (current != null && !current.Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase) &&
                !current.Equals(entry.AfterHash, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"Recovery preserved a file changed outside the launcher: {entry.Path}");
            if (entry.Existed && current?.Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase) != true)
            {
                var saved = EngineStorage.InProfile(backup, "files/" + entry.Path);
                if (!File.Exists(saved) || !(await EngineStorage.HashAsync(saved, ct)).Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Recovery backup missing or corrupt: {entry.Path}");
            }
        }
        if (transaction.HadState && !File.Exists(EngineStorage.InProfile(backup, "state.json")))
            throw new IOException("Recovery state backup is missing.");
        foreach (var entry in transaction.Entries.AsEnumerable().Reverse().Where(e => e.Applied))
        {
            ct.ThrowIfCancellationRequested();
            EnsureGameClosed(profile);
            var target = EngineStorage.InProfile(profile, entry.Path);
            var currentHash = File.Exists(target) ? await EngineStorage.HashAsync(target, ct) : null;
            if (entry.Existed)
            {
                if (currentHash?.Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase) == true) continue;
                if (currentHash != null && !currentHash.Equals(entry.AfterHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Recovery preserved a file changed outside the launcher: {entry.Path}");
                var saved = EngineStorage.InProfile(backup, "files/" + entry.Path);
                if (!File.Exists(saved) || !(await EngineStorage.HashAsync(saved, ct)).Equals(entry.BeforeHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Recovery backup missing or corrupt: {entry.Path}");
                EngineStorage.CopyDurable(saved, target);
            }
            else if (currentHash != null)
            {
                if (!currentHash.Equals(entry.AfterHash, StringComparison.OrdinalIgnoreCase))
                    throw new IOException($"Recovery preserved a new file changed outside the launcher: {entry.Path}");
                File.Delete(target);
            }
        }
        var state = Meta(profile, "state.json");
        if (transaction.HadState)
        {
            var savedState = EngineStorage.InProfile(backup, "state.json");
            if (!File.Exists(savedState)) throw new IOException("Recovery state backup is missing.");
            EngineStorage.CopyDurable(savedState, state);
        }
        else if (File.Exists(state)) File.Delete(state);
    }

    private static void CompleteTransaction(string profile, Transaction transaction)
    {
        EngineStorage.AtomicJson(Meta(profile, "last-backup.json"), transaction);
        File.Delete(Meta(profile, "transaction.json"));
    }

    private void CleanupBackups(string profile, string keepId)
    {
        var backups = Meta(profile, "backups");
        if (!Directory.Exists(backups)) return;
        foreach (var directory in Directory.EnumerateDirectories(backups))
        {
            if (Path.GetFileName(directory) == keepId) continue;
            try { EngineStorage.SafeDeleteTree(Meta(profile, ""), directory); }
            catch (IOException ex) { progress?.Report(new("warning", $"Backup cleanup: {ex.Message}")); }
        }
    }

    private static void EnsureGameClosed(string profile)
    {
        var marker = Meta(profile, "launch-pending.json");
        try
        {
            if (File.Exists(marker))
            {
                var pending = EngineStorage.ReadJson<PendingLaunch>(marker);
                if (pending == null || pending.StartedAt < DateTimeOffset.UnixEpoch ||
                    pending.StartedAt > DateTimeOffset.UtcNow.AddMinutes(5))
                    throw new IOException("The Steam launch marker is invalid. Close Steam's pending launch before updating.");
                if (DateTimeOffset.UtcNow - pending.StartedAt < TimeSpan.FromMinutes(3))
                    throw new IOException("Steam is starting Valheim. Wait for the game to open, then close it before updating.");
            }
        }
        catch (FileNotFoundException) { } // Launcher may remove the marker once the process appears.
        catch (JsonException ex) { throw new IOException("The Steam launch marker is invalid. Close the pending launch before updating.", ex); }
        // Check after reading the marker to cover its removal when the process first appears.
        foreach (var process in Process.GetProcessesByName("valheim"))
        {
            using (process) if (!process.HasExited) throw new IOException("Close Valheim before changing the modpack.");
        }
    }

    private sealed class PendingLaunch
    {
        public DateTimeOffset StartedAt { get; set; }
    }
    private static void CheckSpace(string profile, PackManifest manifest, PackPlan plan)
    {
        var changed = new HashSet<string>(plan.ChangedFiles, StringComparer.OrdinalIgnoreCase);
        long staged = 0, backup = 0, targetGrowth = 0, largestTemporary = 0;
        foreach (var file in manifest.Files.Where(f => changed.Contains(ManifestValidation.Normalize(f.Path))))
        {
            var target = EngineStorage.InProfile(profile, file.Path);
            var existing = File.Exists(target) ? new FileInfo(target).Length : 0;
            staged = checked(staged + file.Size);
            targetGrowth = checked(targetGrowth + Math.Max(0L, file.Size - existing));
            largestTemporary = Math.Max(largestTemporary, Math.Max(file.Size, existing));
        }
        foreach (var file in plan.ChangedFiles.Concat(plan.RemovedFiles))
        {
            var target = EngineStorage.InProfile(profile, file);
            if (File.Exists(target)) backup = checked(backup + new FileInfo(target).Length);
        }
        // Conservative upper bound includes rollback's replacement temporary, target growth and metadata.
        var needed = checked(staged + backup + targetGrowth + largestTemporary);        var directory = new DirectoryInfo(Path.GetFullPath(profile));
        var physical = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
        var root = Path.GetPathRoot(physical);
        if (root != null && !root.StartsWith("\\\\"))
        {
            var drive = new DriveInfo(root);
            if (drive.AvailableFreeSpace < needed + 16 * 1024 * 1024)
                throw new IOException($"Not enough space for staging and rollback: {needed / 1073741824d:F1} GiB needed.");
        }
    }

    private async Task ExtractAsync(string archive, PackArtifact artifact, List<PackFile> files,
        string stageFiles, string stage, CancellationToken ct)
    {
        if (artifact.Kind == "zip")
        {
            using var zip = ZipFile.OpenRead(archive);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in zip.Entries)
            {
                var name = ManifestValidation.Normalize(entry.FullName).TrimEnd('/');
                if (name.Length == 0) continue;
                ManifestValidation.ValidateRelativePath(name);
                if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000)
                    throw new InvalidDataException("Archive links are not supported.");
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue;
                if (!entries.TryAdd(name, entry)) throw new InvalidDataException("Duplicate archive entry.");
            }
            foreach (var file in files)
            {
                if (!entries.TryGetValue(ManifestValidation.Normalize(file.Entry), out var entry) || entry.Length != file.Size)
                    throw new InvalidDataException($"Missing or incorrect archive member: {file.Entry}");
                var target = EngineStorage.InProfile(stageFiles, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using (var source = entry.Open())
                await using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    1024 * 1024, FileOptions.Asynchronous))
                    await source.CopyToAsync(output, ct);
                if (!await EngineStorage.MatchesAsync(target, file.Size, file.Sha256, ct))
                    throw new InvalidDataException($"Prepared file checksum mismatch: {file.Path}");
            }
        }
        else if (artifact.Kind == "raw")
        {
            foreach (var file in files)
            {
                var target = EngineStorage.InProfile(stageFiles, file.Path);
                EngineStorage.CopyDurable(archive, target);
            }
        }
        else
        {
            var tar = _options.TarExecutable ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "tar.exe");
            if (!File.Exists(tar)) throw new IOException("Windows tar.exe is required to extract the HD archive.");
            var listing = await RunTarAsync(tar, ["-tf", archive], ct);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in listing.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var name = ManifestValidation.Normalize(line.TrimEnd('\r')).TrimEnd('/');
                if (name.Length == 0) continue;
                ManifestValidation.ValidateRelativePath(name);
                names.Add(name);
            }
            var verbose = await RunTarAsync(tar, ["-tvf", archive], ct);
            foreach (var line in verbose.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                if (line[0] is not ('-' or 'd')) throw new InvalidDataException("7z archive contains a link or a special file.");
            foreach (var file in files)
                if (!names.Contains(ManifestValidation.Normalize(file.Entry)))
                    throw new InvalidDataException($"Missing HD archive member: {file.Entry}");
            var extracted = Path.Combine(stage, "extracted", artifact.Id);
            Directory.CreateDirectory(extracted);
            var args = new List<string> { "-xf", archive, "-C", extracted, "--" };
            args.AddRange(files.Select(f => f.Entry).Distinct(StringComparer.OrdinalIgnoreCase));
            if (args.Sum(a => a.Length + 3) > 30000) throw new InvalidDataException("Too many 7z extraction members for Windows.");
            await RunTarAsync(tar, args, ct);
            foreach (var file in files)
            {
                var source = EngineStorage.InProfile(extracted, file.Entry);
                if (!await EngineStorage.MatchesAsync(source, file.Size, file.Sha256, ct))
                    throw new InvalidDataException($"Prepared HD file checksum mismatch: {file.Path}");
                var destination = EngineStorage.InProfile(stageFiles, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination); // Same staging volume: no second HD data copy.
            }
        }
    }

    private static async Task<string> RunTarAsync(string executable, IEnumerable<string> arguments, CancellationToken ct)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Windows archive extractor.");
        try
        {
            var stdout = ReadLimitedAsync(process.StandardOutput, 8 * 1024 * 1024, ct);
            var stderr = ReadLimitedAsync(process.StandardError, 65536, ct);
            var pending = new List<Task> { stdout, stderr, process.WaitForExitAsync(ct) };
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);
                await finished; // Surface a reader limit/error before the process can block on a full pipe.
            }
            var output = await stdout;
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException($"Windows archive extraction failed: {error}");
            return output;
        }
        catch
        {
            if (!process.HasExited) process.Kill(true);
            throw;
        }
    }

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int limit, CancellationToken ct)
    {
        var result = new StringBuilder();
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct);
            if (read == 0) break;
            if (result.Length + read > limit) throw new InvalidDataException("Archive listing exceeds the supported size.");
            result.Append(buffer, 0, read);
        }
        return result.ToString();
    }

    private sealed record Analysis(PackPlan Plan, Dictionary<string, InstalledFile> Observed);
}
