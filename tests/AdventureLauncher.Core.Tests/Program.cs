using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventureLauncher.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("signature and pinned key verification", Signature),
    ("unsafe manifest paths and case collisions rejected", ManifestPaths),
    ("install, delta update, owned removal and rollback", UpdateAndRollback),
    ("modified removed file and foreign files preserved", ForeignFiles),
    ("config update preserves selected personal keys", ConfigPolicy),
    ("corrupt archive never changes installed files", CorruptArchive),
    ("archive traversal rejected before mutation", ArchiveTraversal),
    ("manual archive import verifies checksum", ManualArchive),
    ("full verify repairs same-size same-mtime corruption", FullRepair),
    ("cancelled commit restores files and state", CancelledCommit),
    ("interrupted transaction recovery is idempotent", InterruptedRecovery),
    ("profile lock prevents concurrent update", ConcurrentLock),
    ("HTTP interruption resumes from verified range", HttpResume),
    ("invalid Content-Range is never appended", InvalidRange),
    ("root junction accepted; nested junction rejected", Junctions),
    ("Windows tar selected extraction pipeline", TarExtraction),
    ("Steam launch marker blocks update until process starts", PendingLaunch),
    ("rollback preflight preserves external edits", RollbackConflict),
    ("foreign sidecar is not used as replacement temporary", ForeignTemporary),
    ("public Drive confirmation preserves exact file identity", DriveConfirmation),
    ("Drive confirmation rejects different file and external action", UnsafeDriveConfirmation),
    ("manual download links reject executable schemes", UnsafeManualLink),
    ("all required manual archives are requested before downloads and extraction", EarlyManualPreflight),
    ("wrong and missing imports allow choosing another archive", RetryManualImport)
};
var failures = 0;
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} checks passed.");
return failures == 0 ? 0 : 1;

static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); }
    catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
static PackEngine Engine(IProgress<PackProgress>? progress = null, HttpClient? http = null) =>
    new(http ?? new HttpClient(), new EngineOptions { AllowLocalSources = true }, progress);

static async Task Signature()
{
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var manifest = new PackManifest { Version = "1.0.0" };
    var signed = ManifestSigning.Sign(manifest, key.ExportECPrivateKeyPem());
    Assert(ManifestSigning.Verify(signed, key.ExportSubjectPublicKeyInfoPem()).Version == "1.0.0", "Signature roundtrip failed.");
    var envelope = JsonSerializer.Deserialize<SignedEnvelope>(signed, PackJson.Options)!;
    envelope.Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"schemaVersion\": 1, \"version\": \"evil\" }"));
    await Throws<CryptographicException>(() => Task.FromResult(ManifestSigning.Verify(JsonSerializer.Serialize(envelope, PackJson.Options), key.ExportSubjectPublicKeyInfoPem())));
    using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    await Throws<CryptographicException>(() => Task.FromResult(ManifestSigning.Verify(signed, other.ExportSubjectPublicKeyInfoPem())));
}
static async Task ManifestPaths()
{
    foreach (var path in new[] { "../x.dll", "C:/x.dll", "BepInEx/../x", "BepInEx/x:stream", "BepInEx/NUL.dll", "BepInEx/x.", ".arpg/state.json", "\\\\host\\x" })
    {
        using var fixture = new Fixture();
        var manifest = fixture.Zip("1", (path, "test"));
        await Throws<InvalidDataException>(() => Task.Run(() => ManifestValidation.Validate(manifest)));
    }
    using var f = new Fixture();
    var duplicate = f.Zip("1", ("BepInEx/x.dll", "x"), ("BepInEx/X.dll", "y"));
    await Throws<InvalidDataException>(() => Task.Run(() => ManifestValidation.Validate(duplicate)));
}
static async Task UpdateAndRollback()
{
    using var f = new Fixture();
    var engine = Engine();
    var one = f.Zip("1", ("BepInEx/a.dll", "old"), ("BepInEx/remove.dll", "owned"));
    await engine.ApplyAsync(one, f.Profile, f.Cache);
    var two = f.Zip("2", ("BepInEx/a.dll", "new"), ("BepInEx/b.dll", "added"));
    var plan = await engine.InspectAsync(two, f.Profile);
    Assert(plan.ChangedFiles.Count == 2 && plan.RemovedFiles.Count == 1, "Incorrect delta.");
    await engine.ApplyAsync(two, f.Profile, f.Cache);
    Assert(f.Read("BepInEx/a.dll") == "new" && !File.Exists(f.Path("BepInEx/remove.dll")), "Delta failed.");
    // No-op launch/update must not discard the backup from the actual update.
    await engine.ApplyAsync(two, f.Profile, f.Cache);
    await engine.RollbackAsync(f.Profile);
    Assert(f.Read("BepInEx/a.dll") == "old" && f.Read("BepInEx/remove.dll") == "owned", "Rollback failed.");
    Assert(!File.Exists(f.Path("BepInEx/b.dll")), "Rollback retained a new file.");
}
static async Task ForeignFiles()
{
    using var f = new Fixture();
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/removed.dll", "original")), f.Profile, f.Cache);
    File.WriteAllText(f.Path("BepInEx/removed.dll"), "player-changed");
    File.WriteAllText(f.Path("BepInEx/foreign.dll"), "foreign");
    await engine.ApplyAsync(f.Zip("2", ("BepInEx/new.dll", "new")), f.Profile, f.Cache);
    Assert(f.Read("BepInEx/removed.dll") == "player-changed" && f.Read("BepInEx/foreign.dll") == "foreign", "Foreign file deleted.");
}
static async Task ConfigPolicy()
{
    using var f = new Fixture();
    var engine = Engine();
    var one = f.Zip("1", ("BepInEx/config/example.cfg", "[Controls]\nKey = F1\nDamage = 10\n"));
    one.Files[0].Policy = "config";
    one.Files[0].PreserveKeys = ["Controls/Key"];
    await engine.ApplyAsync(one, f.Profile, f.Cache);
    File.WriteAllText(f.Path(one.Files[0].Path), "[Controls]\nKey = F9\nDamage = 999\n");
    await engine.ApplyAsync(one, f.Profile, f.Cache);
    Assert(f.Read(one.Files[0].Path).Contains("Damage = 999"), "Unchanged config source reset personal edits.");
    var two = f.Zip("2", ("BepInEx/config/example.cfg", "[Controls]\nKey = F2\nDamage = 20\n"));
    two.Files[0].Policy = "config";
    two.Files[0].PreserveKeys = ["Controls/Key"];
    await engine.ApplyAsync(two, f.Profile, f.Cache);
    Assert(f.Read(two.Files[0].Path).Contains("Key = F9") && f.Read(two.Files[0].Path).Contains("Damage = 20"), "Config merge failed.");
}
static async Task CorruptArchive()
{
    using var f = new Fixture();
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "old")), f.Profile, f.Cache);
    var two = f.Zip("2", ("BepInEx/a.dll", "new"));
    File.WriteAllBytes(new Uri(two.Artifacts[0].Urls[0]).LocalPath, [1, 2, 3]);
    await Throws<IOException>(() => engine.ApplyAsync(two, f.Profile, f.Cache));
    Assert(f.Read("BepInEx/a.dll") == "old", "Corrupt input changed the profile.");
}
static async Task ArchiveTraversal()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/good.dll", "good"));
    var archivePath = new Uri(manifest.Artifacts[0].Urls[0]).LocalPath;
    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
    {
        using var writer = new StreamWriter(zip.CreateEntry("../outside.dll").Open());
        writer.Write("bad");
    }
    var bytes = File.ReadAllBytes(archivePath);
    manifest.Artifacts[0].Size = bytes.Length;
    manifest.Artifacts[0].Sha256 = Sha(bytes);
    await Throws<InvalidDataException>(() => Engine().ApplyAsync(manifest, f.Profile, f.Cache));
    Assert(!File.Exists(f.Path("BepInEx/good.dll")) && !File.Exists(System.IO.Path.Combine(f.Root, "outside.dll")), "Traversal mutated files.");
}
static async Task ManualArchive()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/manual.dll", "manual"));
    var artifact = manifest.Artifacts[0];
    var local = new Uri(artifact.Urls[0]).LocalPath;
    artifact.Urls = [];
    artifact.ManualUrl = "https://example.com/download";
    var engine = Engine();
    await Throws<ManualArtifactRequiredException>(() => engine.ApplyAsync(manifest, f.Profile, f.Cache));
    await engine.ApplyAsync(manifest, f.Profile, f.Cache, new Dictionary<string,string> { [artifact.Id] = local });
    Assert(f.Read("BepInEx/manual.dll") == "manual", "Manual import failed.");
}
static async Task FullRepair()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/a.dll", "correct"));
    var engine = Engine();
    await engine.ApplyAsync(manifest, f.Profile, f.Cache);
    var target = f.Path("BepInEx/a.dll");
    var stamp = File.GetLastWriteTimeUtc(target);
    File.WriteAllText(target, "damaged");
    File.SetLastWriteTimeUtc(target, stamp);
    var plan = await engine.InspectAsync(manifest, f.Profile, true);
    Assert(plan.ChangedFiles.Count == 1, "Full verify missed corruption.");
    await engine.ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(f.Read("BepInEx/a.dll") == "correct", "Fast Apply ignored pending repair.");
}
static async Task CancelledCommit()
{
    using var f = new Fixture();
    await Engine().ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "old-a"), ("BepInEx/b.dll", "old-b")), f.Profile, f.Cache);
    using var cancellation = new CancellationTokenSource();
    var engine = Engine(new SyncProgress(p => { if (p.Phase == "apply" && p.Completed == 1) cancellation.Cancel(); }));
    await Throws<OperationCanceledException>(() => engine.ApplyAsync(f.Zip("2", ("BepInEx/a.dll", "new-a"), ("BepInEx/b.dll", "new-b")),
        f.Profile, f.Cache, ct: cancellation.Token));
    Assert(f.Read("BepInEx/a.dll") == "old-a" && f.Read("BepInEx/b.dll") == "old-b", "Cancelled commit was not rolled back.");
    Assert(!File.Exists(f.Path(".arpg/transaction.json")), "Restored transaction left a journal.");
}
static async Task InterruptedRecovery()
{
    using var f = new Fixture();
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "old")), f.Profile, f.Cache);
    await engine.ApplyAsync(f.Zip("2", ("BepInEx/a.dll", "new"), ("BepInEx/b.dll", "new")), f.Profile, f.Cache);
    var journal = File.ReadAllText(f.Path(".arpg/last-backup.json")).Replace("\"committed\"", "\"applying\"");
    File.WriteAllText(f.Path(".arpg/transaction.json"), journal);
    Assert(await engine.RecoverAsync(f.Profile), "Recovery not reported.");
    Assert(f.Read("BepInEx/a.dll") == "old" && !File.Exists(f.Path("BepInEx/b.dll")), "Interrupted recovery failed.");
    Assert(!await engine.RecoverAsync(f.Profile), "Second recovery was not a no-op.");
}
static async Task ConcurrentLock()
{
    using var f = new Fixture();
    Directory.CreateDirectory(f.Path(".arpg"));
    using var locked = new FileStream(f.Path(".arpg/operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    await Throws<IOException>(() => Engine().ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "new")), f.Profile, f.Cache));
    Assert(!File.Exists(f.Path("BepInEx/a.dll")), "Concurrent update changed the profile.");
}
static async Task HttpResume()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/a.dll", "remote"));
    var bytes = File.ReadAllBytes(new Uri(manifest.Artifacts[0].Urls[0]).LocalPath);
    manifest.Artifacts[0].Urls = ["http://localhost/test.zip"];
    var handler = new ResumeHandler(bytes, false);
    using var http = new HttpClient(handler);
    await Engine(http: http).ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(handler.Calls == 2 && handler.SawRange, "Interrupted HTTP download did not resume.");
    Assert(f.Read("BepInEx/a.dll") == "remote", "HTTP result was corrupted.");
}
static async Task InvalidRange()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/a.dll", "remote"));
    var bytes = File.ReadAllBytes(new Uri(manifest.Artifacts[0].Urls[0]).LocalPath);
    manifest.Artifacts[0].Urls = ["http://localhost/test.zip"];
    var handler = new ResumeHandler(bytes, true);
    using var http = new HttpClient(handler);
    await Engine(http: http).ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(handler.Calls == 3, "Invalid range was not retried from zero.");
    Assert(f.Read("BepInEx/a.dll") == "remote", "Invalid range corrupted the archive.");
}
static async Task Junctions()
{
    if (!OperatingSystem.IsWindows()) return;
    using var f = new Fixture();
    Directory.CreateDirectory(f.Profile);
    var alias = System.IO.Path.Combine(f.Root, "alias");
    Junction(alias, f.Profile);
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "root-link")), alias, f.Cache);
    Assert(f.Read("BepInEx/a.dll") == "root-link", "Root junction failed.");
    var outside = System.IO.Path.Combine(f.Root, "outside");
    Directory.CreateDirectory(outside);
    Junction(f.Path("BepInEx/plugins"), outside);
    await Throws<IOException>(() => engine.ApplyAsync(f.Zip("2", ("BepInEx/plugins/escape.dll", "bad")), alias, f.Cache));
    Assert(!File.Exists(System.IO.Path.Combine(outside, "escape.dll")), "Nested junction allowed an escape.");
    Directory.Delete(outside);
    await Throws<IOException>(() => engine.ApplyAsync(f.Zip("3", ("BepInEx/plugins/escape.dll", "bad")), alias, f.Cache));
    Assert(!Directory.Exists(outside), "Dangling nested junction created an outside destination.");
}
static void Junction(string link, string target)
{
    var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
    start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("mklink");
    start.ArgumentList.Add("/J"); start.ArgumentList.Add(link); start.ArgumentList.Add(target);
    using var p = Process.Start(start)!; p.WaitForExit();
    if (p.ExitCode != 0) throw new Exception("Cannot create test junction: " + p.StandardError.ReadToEnd());
}
static async Task TarExtraction()
{
    if (!OperatingSystem.IsWindows()) return;
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/a.dll", "tar-selected"), ("BepInEx/b.dll", "tar-second"));
    // BSD tar auto-detects archive format. This exercises the selected-member command pipeline;
    // it does not claim that a real HD 7z payload was installed.
    manifest.Artifacts[0].Kind = "7z";
    await Engine().ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(f.Read("BepInEx/a.dll") == "tar-selected" && f.Read("BepInEx/b.dll") == "tar-second", "tar selection failed.");
}

static async Task PendingLaunch()
{
    using var f = new Fixture();
    Directory.CreateDirectory(f.Path(".arpg"));
    var manifest = f.Zip("1", ("BepInEx/a.dll", "safe"));
    var marker = f.Path(".arpg/launch-pending.json");
    File.WriteAllText(marker, JsonSerializer.Serialize(new { startedAt = DateTimeOffset.UtcNow }));
    await Throws<IOException>(() => Engine().ApplyAsync(manifest, f.Profile, f.Cache));
    Assert(!File.Exists(f.Path("BepInEx/a.dll")), "Update ran while Steam launch was pending.");
    File.WriteAllText(marker, "{ invalid }");
    await Throws<IOException>(() => Engine().ApplyAsync(manifest, f.Profile, f.Cache));
    File.WriteAllText(marker, JsonSerializer.Serialize(new { startedAt = DateTimeOffset.UtcNow.AddHours(-1) }));
    await Engine().ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(f.Read("BepInEx/a.dll") == "safe", "Expired marker blocked normal update.");
}
static async Task RollbackConflict()
{
    using var f = new Fixture();
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "old-a"), ("BepInEx/b.dll", "old-b")), f.Profile, f.Cache);
    await engine.ApplyAsync(f.Zip("2", ("BepInEx/a.dll", "new-a"), ("BepInEx/b.dll", "new-b")), f.Profile, f.Cache);
    File.WriteAllText(f.Path("BepInEx/a.dll"), "external edit");
    await Throws<IOException>(() => engine.RollbackAsync(f.Profile));
    Assert(f.Read("BepInEx/a.dll") == "external edit" && f.Read("BepInEx/b.dll") == "new-b", "Rollback partly modified files before detecting the conflict.");
    File.WriteAllText(f.Path("BepInEx/a.dll"), "new-a");
    await engine.RecoverAsync(f.Profile);
    Assert(f.Read("BepInEx/a.dll") == "old-a" && f.Read("BepInEx/b.dll") == "old-b", "Recovery after resolving the conflict failed.");
}
static async Task ForeignTemporary()
{
    using var f = new Fixture();
    var engine = Engine();
    await engine.ApplyAsync(f.Zip("1", ("BepInEx/a.dll", "old")), f.Profile, f.Cache);
    File.WriteAllText(f.Path("BepInEx/a.dll.arpg-new"), "foreign sidecar");
    await engine.ApplyAsync(f.Zip("2", ("BepInEx/a.dll", "new")), f.Profile, f.Cache);
    Assert(f.Read("BepInEx/a.dll.arpg-new") == "foreign sidecar", "Replacement temporary clobbered a foreign sidecar.");
}
static async Task DriveConfirmation()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/a.dll", "drive download"));
    var bytes = File.ReadAllBytes(new Uri(manifest.Artifacts[0].Urls[0]).LocalPath);
    manifest.Artifacts[0].Urls = ["https://drive.google.com/uc?id=test-file-123456789&export=download"];
    var handler = new DriveHandler(bytes, false, false);
    using var http = new HttpClient(handler);
    await Engine(http: http).ApplyAsync(manifest, f.Profile, f.Cache);
    Assert(handler.Confirmed && handler.Calls == 2 && f.Read("BepInEx/a.dll") == "drive download", "Drive confirmation failed.");
}
static async Task UnsafeDriveConfirmation()
{
    foreach (var otherFile in new[] { true, false })
    {
        using var f = new Fixture();
        var manifest = f.Zip("1", ("BepInEx/a.dll", "protected"));
        var bytes = File.ReadAllBytes(new Uri(manifest.Artifacts[0].Urls[0]).LocalPath);
        manifest.Artifacts[0].Urls = ["https://drive.google.com/uc?id=test-file-123456789&export=download"];
        var handler = new DriveHandler(bytes, otherFile, !otherFile);
        using var http = new HttpClient(handler);
        await Throws<IOException>(() => Engine(http: http).ApplyAsync(manifest, f.Profile, f.Cache));
        Assert(!handler.Confirmed && !File.Exists(f.Path("BepInEx/a.dll")), "Unsafe confirmation was followed.");
    }
}
static async Task UnsafeManualLink()
{
    using var f = new Fixture();
    foreach (var url in new[] { "file:///C:/evil.exe", "javascript:alert(1)", "https://user:password@example.com/file" })
    {
        var manifest = f.Zip("1", ("BepInEx/a.dll", "safe"));
        manifest.Artifacts[0].ManualUrl = url;
        await Throws<InvalidDataException>(() => Task.Run(() => ManifestValidation.Validate(manifest)));
    }
}
static async Task EarlyManualPreflight()
{
    using var f = new Fixture();
    var remote = f.Zip("1", ("BepInEx/remote.dll", "catalogue"));
    var remoteBytes = File.ReadAllBytes(new Uri(remote.Artifacts[0].Urls[0]).LocalPath);
    remote.Artifacts[0].Urls = ["https://catalogue.example.test/package.zip"];
    var first = f.Zip("1", ("BepInEx/first.dll", "manual-first"));
    var second = f.Zip("1", ("BepInEx/second.dll", "manual-second"));
    var firstPath = new Uri(first.Artifacts[0].Urls[0]).LocalPath;
    var secondPath = new Uri(second.Artifacts[0].Urls[0]).LocalPath;
    first.Artifacts[0].Id = first.Files[0].ArtifactId = "manual-first";
    second.Artifacts[0].Id = second.Files[0].ArtifactId = "manual-second";
    first.Artifacts[0].Urls = [];
    second.Artifacts[0].Urls = [];
    remote.Artifacts.AddRange(first.Artifacts.Concat(second.Artifacts));
    remote.Files.AddRange(first.Files.Concat(second.Files));
    var handler = new CountingHandler(remoteBytes);
    using var http = new HttpClient(handler);
    var extractions = 0;
    var engine = Engine(new SyncProgress(p => { if (p.Phase == "extract") extractions++; }), http);
    var selected = new Dictionary<string,string>();
    var missing = await NeedManual(() => engine.ApplyAsync(remote, f.Profile, f.Cache, selected));
    Assert(missing.Artifact.Id == "manual-first" && handler.Calls == 0 && extractions == 0, "First manual prompt happened after catalogue work.");
    Assert(!Directory.Exists(f.Path(".arpg/staging")), "Manual preflight created staging files.");
    selected["manual-first"] = firstPath;
    missing = await NeedManual(() => engine.ApplyAsync(remote, f.Profile, f.Cache, selected));
    Assert(missing.Artifact.Id == "manual-second" && handler.Calls == 0 && extractions == 0, "Second manual prompt repeated catalogue work.");
    selected["manual-second"] = secondPath;
    await engine.ApplyAsync(remote, f.Profile, f.Cache, selected);
    Assert(handler.Calls == 1 && extractions == 3, "Completed manual preflight did not install the package once.");
    Assert(f.Read("BepInEx/first.dll") == "manual-first" && f.Read("BepInEx/second.dll") == "manual-second", "Manual files missing.");
    // An installed/current manual artifact never needs selecting again.
    await engine.ApplyAsync(remote, f.Profile, f.Cache);
    Assert(handler.Calls == 1 && extractions == 3, "No-op update asked for unused manual archives or re-extracted files.");
}
static async Task RetryManualImport()
{
    using var f = new Fixture();
    var manifest = f.Zip("1", ("BepInEx/manual.dll", "correct manual archive"));
    var artifact = manifest.Artifacts[0];
    var correctPath = new Uri(artifact.Urls[0]).LocalPath;
    artifact.Urls = [];
    var selected = new Dictionary<string,string> { [artifact.Id] = System.IO.Path.Combine(f.Root, "missing.zip") };
    var engine = Engine();
    var missing = await NeedManual(() => engine.ApplyAsync(manifest, f.Profile, f.Cache, selected));
    Assert(missing.Message.Contains("Choose the original file again"), "Missing import did not explain reselection.");
    var wrong = System.IO.Path.Combine(f.Root, "wrong.zip");
    File.WriteAllBytes(wrong, new byte[artifact.Size]);
    selected[artifact.Id] = wrong;
    var invalid = await NeedManual(() => engine.ApplyAsync(manifest, f.Profile, f.Cache, selected));
    Assert(invalid.Artifact.Id == artifact.Id && invalid.Message.Contains("does not match"), "Wrong import did not return a recoverable manual prompt.");
    selected[artifact.Id] = correctPath;
    await engine.ApplyAsync(manifest, f.Profile, f.Cache, selected);
    Assert(f.Read("BepInEx/manual.dll") == "correct manual archive", "Selecting the correct archive after an error did not work.");
}
static async Task<ManualArtifactRequiredException> NeedManual(Func<Task> action)
{
    try { await action(); }
    catch (ManualArtifactRequiredException ex) { return ex; }
    throw new Exception("Expected a manual archive selection prompt.");
}
sealed class Fixture : IDisposable
{
    public string Root { get; } = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
    public string Profile => System.IO.Path.Combine(Root, "profile");
    public string Cache => System.IO.Path.Combine(Root, "cache");
    public Fixture() => Directory.CreateDirectory(Root);
    public string Path(string path) => System.IO.Path.Combine(Profile, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
    public string Read(string path) => File.ReadAllText(Path(path));
    public PackManifest Zip(string version, params (string Path, string Content)[] files)
    {
        var name = "test-" + Guid.NewGuid().ToString("N") + ".zip";
        var archive = System.IO.Path.Combine(Root, name);
        var manifest = new PackManifest { Version = version };
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        {
            var n = 0;
            foreach (var file in files)
            {
                var bytes = Encoding.UTF8.GetBytes(file.Content);
                var entry = "payload/" + n++ + ".bin";
                using (var output = zip.CreateEntry(entry).Open()) output.Write(bytes);
                manifest.Files.Add(new PackFile { Path = file.Path, Entry = entry, ArtifactId = "payload",
                    Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
            }
        }
        var archiveBytes = File.ReadAllBytes(archive);
        manifest.Artifacts.Add(new PackArtifact { Id = "payload", FileName = name, Kind = "zip", Size = archiveBytes.Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(archiveBytes)), Urls = [new Uri(archive).AbsoluteUri] });
        return manifest;
    }
    public void Dispose()
    {
        // Do not recurse through junctions; every fixture is generated under this binary's own fixtures directory.
        var expected = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures")) + System.IO.Path.DirectorySeparatorChar;
        if (!System.IO.Path.GetFullPath(Root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture path.");
        DeleteOwned(Root);
    }
    static void DeleteOwned(string directory)
    {
        if (!Directory.Exists(directory)) return;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var flags = File.GetAttributes(path);
            if ((flags & FileAttributes.ReparsePoint) != 0)
            {
                if ((flags & FileAttributes.Directory) != 0) Directory.Delete(path);
                else File.Delete(path);
            }
            else if ((flags & FileAttributes.Directory) != 0) DeleteOwned(path);
            else File.Delete(path);
        }
        Directory.Delete(directory);
    }
}
sealed class SyncProgress(Action<PackProgress> action) : IProgress<PackProgress>
{
    public void Report(PackProgress value) => action(value);
}
sealed class ResumeHandler(byte[] bytes, bool badRange) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public bool SawRange { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        if (Calls == 1)
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new InterruptStream(bytes, 53)) });
        var start = request.Headers.Range?.Ranges.First().From ?? 0;
        SawRange |= start == 53;
        if (badRange && Calls == 2)
        {
            var content = new ByteArrayContent(bytes[53..]);
            content.Headers.ContentRange = new ContentRangeHeaderValue(1, bytes.Length - 1, bytes.Length);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content });
        }
        var correct = new ByteArrayContent(bytes[(int)start..]);
        if (start > 0) correct.Headers.ContentRange = new ContentRangeHeaderValue(start, bytes.Length - 1, bytes.Length);
        return Task.FromResult(new HttpResponseMessage(start > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = correct });
    }
}
sealed class InterruptStream(byte[] bytes, int cut) : MemoryStream(bytes)
{
    private bool _read;
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_read) throw new IOException("Synthetic network interruption.");
        _read = true;
        var count = Math.Min(cut, (int)Length);
        var copied = base.Read(buffer.Span[..count]);
        Position = count;
        return ValueTask.FromResult(count);
    }
}

sealed class DriveHandler(byte[] payload, bool otherFile, bool externalAction) : HttpMessageHandler
{
    public int Calls { get; private set; }
    public bool Confirmed { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        var uri = request.RequestUri!;
        if (uri.Host == "drive.usercontent.google.com" || uri.Host == "evil.invalid")
        {
            Confirmed = true;
            AssertId(uri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(payload) });
        }
        var id = otherFile ? "another-file-999999" : "test-file-123456789";
        var action = externalAction ? "https://evil.invalid/download" : "https://drive.usercontent.google.com/download";
        var html = $"<html><form id='download-form' action='{action}' method='get'><input type='hidden' name='id' value='{id}'><input type='hidden' name='export' value='download'><input type='hidden' name='confirm' value='t'><input type='hidden' name='uuid' value='synthetic-token'></form></html>";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new StringContent(html, Encoding.UTF8, "text/html") });
    }
    private static void AssertId(Uri uri)
    {
        if (!uri.Query.Contains("id=test-file-123456789")) throw new Exception("File identity changed during confirmation.");
    }
}
sealed class CountingHandler(byte[] bytes) : HttpMessageHandler
{
    public int Calls { get; private set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}