using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventureLauncher.Core;

var root = Path.GetFullPath(args[0]);
var upstream = Path.Combine(root, "updates/20261101-mod-migration/extracted/denikson-BepInExPack_Valheim-5.4.2360/BepInExPack_Valheim");
using var oldZip = ZipFile.OpenRead(Path.Combine(root, "packages/denikson-BepInExPack_Valheim-5.4.2351.zip"));
var old = new Dictionary<string, byte[]>();
var fresh = new Dictionary<string, byte[]>();
foreach (string name in new[] { "winhttp.dll", ".doorstop_version" })
{
    using var input = oldZip.GetEntry("BepInExPack_Valheim/" + name)!.Open();
    using var buffer = new MemoryStream(); input.CopyTo(buffer); old[name] = buffer.ToArray();
    fresh[name] = File.ReadAllBytes(Path.Combine(upstream, name));
}
fresh["doorstop_config.ini"] = Encoding.UTF8.GetBytes("[General]\nenabled=false\nboot_config_override=\n[UnityMono]\ndll_search_path_override=\n");
var expectedOld = new Dictionary<string, string> { ["winhttp.dll"] = "93406d0a02e7c164b89828cbfe3b289930a112d2eca50bd4a52e72ece169e6a8", [".doorstop_version"] = "84fa42545c0993edf8a5559d2a0772260f85f4a7ed29ec75e6590be64ed98067" };
var expectedNew = new Dictionary<string, string> { ["winhttp.dll"] = "0f39399eba05dea9419781187ae2a6b0555afcdf1859f1319b6213be7729013b", [".doorstop_version"] = "aab86e1db7618ee7d82b374862d73b7494e0267fa201410012a775de66f5eaba" };
foreach (var name in old.Keys) Assert(Sha(old[name]) == expectedOld[name] && Sha(fresh[name]) == expectedNew[name], "Fixture inputs are exact official legacy/new binaries.");
var tests = new (string Name, Action Run)[]
{
    ("matching native files are a metadata-preserving no-op", Matching),
    ("known legacy loader upgrades atomically with durable original backup", Upgrade),
    ("missing bootstrap files install from verified profile source", Missing),
    ("mixed known old/new bootstrap state completes migration", Partial),
    ("unknown winhttp and marker are rejected without native/profile writes", Unknown),
    ("legacy pin cannot authorize an unapproved replacement target", UnapprovedTarget),
    ("all staged sources validate before the first native write", BadSource),
    ("matching native files still reject corrupt staged sources", BadUnusedSource),
    ("unsupported paths, duplicate files and game/profile overlap rejected", UnsafeScope),
    ("injected migration failure restores both old bytes and timestamps", RollbackMigration),
    ("injected fresh install failure removes only its own added files", RollbackMissing),
    ("rollback refuses to overwrite an external loader edit", RollbackConflict)
};
int failed = 0;
foreach (var test in tests) try { test.Run(); Console.WriteLine("PASS " + test.Name); } catch (Exception ex) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + ex); }
Console.WriteLine($"{tests.Length - failed}/{tests.Length} bootstrap scenarios passed; real game/profile untouched.");
return failed == 0 ? 0 : 1;

void Assert(bool value, string reason) { if (!value) throw new Exception(reason); }
void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
Fixture NewFixture() => new(fresh, Path.Combine(root, "launcher/artifacts/bootstrap-test-fixtures"));
void Preserved(Fixture f, Dictionary<string, (string Hash, long Ticks)> profile, byte[]? ini = null)
{
    Assert(f.SnapshotProfile().OrderBy(x => x.Key).SequenceEqual(profile.OrderBy(x => x.Key)), "Bootstrap installer changed the profile.");
    if (ini != null) Assert(File.ReadAllBytes(f.GameFile("doorstop_config.ini")).SequenceEqual(ini), "Existing native INI changed.");
}
void Matching()
{
    using var f = NewFixture(); f.SetNative(fresh); var profile = f.SnapshotProfile(); var game = f.SnapshotGame(); var ini = File.ReadAllBytes(f.GameFile("doorstop_config.ini"));
    BootstrapInstaller.Validate(f.Manifest, f.Game); var result = BootstrapInstaller.Ensure(f.Manifest, f.Settings);
    Assert(result.WrittenFiles == 0 && result.MigratedFiles == 0 && result.BackupPath == null, "No-op created a transaction.");
    Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Matching native files touched."); Preserved(f, profile, ini);
}
void Upgrade()
{
    using var f = NewFixture(); f.SetNative(old); var profile = f.SnapshotProfile(); var ini = File.ReadAllBytes(f.GameFile("doorstop_config.ini"));
    BootstrapInstaller.Validate(f.Manifest, f.Game); var result = BootstrapInstaller.Ensure(f.Manifest, f.Settings);
    Assert(result.WrittenFiles == 2 && result.MigratedFiles == 2 && result.BackupPath != null, "Expected two known replacements.");
    foreach (var name in old.Keys) { Assert(Sha(File.ReadAllBytes(f.GameFile(name))) == expectedNew[name], "Upgrade did not install signed target."); Assert(Sha(File.ReadAllBytes(Path.Combine(result.BackupPath!, "before", name))) == expectedOld[name], "Original backup differs."); }
    Assert(JsonDocument.Parse(File.ReadAllText(Path.Combine(result.BackupPath!, "transaction.json"))).RootElement.GetProperty("status").GetString() == "committed", "Missing committed journal.");
    Preserved(f, profile, ini); Assert(BootstrapInstaller.Ensure(f.Manifest, f.Settings).WrittenFiles == 0, "Completed migration was repeated.");
}
void Missing()
{
    using var f = NewFixture(); var profile = f.SnapshotProfile(); var result = BootstrapInstaller.Ensure(f.Manifest, f.Settings);
    Assert(result.WrittenFiles == 3 && result.MigratedFiles == 0, "Fresh install must add all three files.");
    foreach (var (name, bytes) in fresh) Assert(File.ReadAllBytes(f.GameFile(name)).SequenceEqual(bytes), "Missing source differs."); Preserved(f, profile);
}
void Partial()
{
    using var f = NewFixture(); f.SetNative(old); File.WriteAllBytes(f.GameFile("winhttp.dll"), fresh["winhttp.dll"]); var profile = f.SnapshotProfile(); var ini = File.ReadAllBytes(f.GameFile("doorstop_config.ini"));
    var result = BootstrapInstaller.Ensure(f.Manifest, f.Settings); Assert(result.WrittenFiles == 1 && result.MigratedFiles == 1, "Mixed state should update only old marker."); Preserved(f, profile, ini);
}
void Unknown()
{
    foreach (string name in old.Keys)
    {
        using var f = NewFixture(); f.SetNative(old); File.WriteAllText(f.GameFile(name), "unrelated/custom mod loader"); var game = f.SnapshotGame(); var profile = f.SnapshotProfile();
        Throws<InvalidOperationException>(() => BootstrapInstaller.Validate(f.Manifest, f.Game)); Throws<InvalidOperationException>(() => BootstrapInstaller.Ensure(f.Manifest, f.Settings));
        Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Unknown loader was modified."); Preserved(f, profile);
    }
}
void UnapprovedTarget()
{
    using var f = NewFixture(); f.SetNative(old); var game = f.SnapshotGame(); var file = f.Manifest.Files.Single(x => x.Path.EndsWith("winhttp.dll")); var other = Encoding.UTF8.GetBytes("not the reviewed official replacement");
    File.WriteAllBytes(Path.Combine(f.Profile, file.Path), other); file.Size = other.Length; file.Sha256 = Sha(other);
    Throws<InvalidOperationException>(() => BootstrapInstaller.Ensure(f.Manifest, f.Settings)); Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Legacy allowlist authorized another target.");
}
void BadSource()
{
    foreach (string name in fresh.Keys)
    {
        using var f = NewFixture(); f.SetNative(old); var game = f.SnapshotGame(); var bytes = File.ReadAllBytes(Path.Combine(f.Profile, "bootstrap", name)); bytes[0] ^= 1; File.WriteAllBytes(Path.Combine(f.Profile, "bootstrap", name), bytes); var profile = f.SnapshotProfile();
        Throws<IOException>(() => BootstrapInstaller.Ensure(f.Manifest, f.Settings)); Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Bad source caused a native write before complete validation."); Preserved(f, profile);
    }
}
void BadUnusedSource()
{
    using var f = NewFixture(); f.SetNative(fresh); var game = f.SnapshotGame(); File.WriteAllText(Path.Combine(f.Profile, "bootstrap", "doorstop_config.ini"), "corrupt source"); var profile = f.SnapshotProfile();
    Throws<IOException>(() => BootstrapInstaller.Ensure(f.Manifest, f.Settings)); Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Bad unused source touched game."); Preserved(f, profile);
}
void UnsafeScope()
{
    using var f = NewFixture(); f.SetNative(old);
    f.Manifest.Files.Add(new PackFile { Path = "bootstrap/../escape", Sha256 = new string('a', 64) }); Throws<InvalidOperationException>(() => BootstrapInstaller.Validate(f.Manifest, f.Game));
    f.Manifest.Files.RemoveAt(f.Manifest.Files.Count - 1); f.Manifest.Files.Add(f.Manifest.Files[0]); Throws<InvalidOperationException>(() => BootstrapInstaller.Validate(f.Manifest, f.Game));
    f.Manifest.Files.RemoveAt(f.Manifest.Files.Count - 1); var overlap = new LauncherSettings { GamePath = f.Game, ProfilePath = f.Game }; Throws<InvalidOperationException>(() => BootstrapInstaller.Ensure(f.Manifest, overlap));
}
void RollbackMigration()
{
    using var f = NewFixture(); f.SetNative(old); var game = f.SnapshotGame(); var profile = f.SnapshotProfile(); var ini = File.ReadAllBytes(f.GameFile("doorstop_config.ini"));
    Throws<IOException>(() => BootstrapInstaller.EnsureCore(f.Manifest, f.Settings, n => { if (n == 1) throw new IOException("Fixture write failure"); }));
    Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Rollback changed original native bytes/timestamps."); Preserved(f, profile, ini);
    Assert(BootstrapInstaller.Ensure(f.Manifest, f.Settings).WrittenFiles == 2, "Retry after rollback failed.");
}
void RollbackMissing()
{
    using var f = NewFixture(); var game = f.SnapshotGame(); var profile = f.SnapshotProfile();
    Throws<IOException>(() => BootstrapInstaller.EnsureCore(f.Manifest, f.Settings, n => { if (n == 2) throw new IOException("Fixture write failure"); }));
    Assert(f.SnapshotGame().OrderBy(x => x.Key).SequenceEqual(game.OrderBy(x => x.Key)), "Fresh-install rollback retained new files."); Preserved(f, profile);
}
void RollbackConflict()
{
    using var f = NewFixture(); f.SetNative(old); var profile = f.SnapshotProfile();
    Throws<AggregateException>(() => BootstrapInstaller.EnsureCore(f.Manifest, f.Settings, n => { if (n == 1) { File.WriteAllText(f.GameFile("winhttp.dll"), "external changed during operation"); throw new IOException("Fixture conflict"); } }));
    Assert(File.ReadAllText(f.GameFile("winhttp.dll")) == "external changed during operation", "Rollback overwrote foreign edit."); Assert(Sha(File.ReadAllBytes(f.GameFile(".doorstop_version"))) == expectedOld[".doorstop_version"], "Unapplied marker changed."); Preserved(f, profile);
}

sealed class Fixture : IDisposable
{
    public string Root { get; }
    private readonly string fixtureParent;
    public string Game => Path.Combine(Root, "game");
    public string Profile => Path.Combine(Root, "profile");
    public PackManifest Manifest { get; } = new() { Version = "fixture-new-loader" };
    public LauncherSettings Settings => new() { GamePath = Game, ProfilePath = Profile, CachePath = Path.Combine(Root, "cache") };
    public Fixture(Dictionary<string, byte[]> fresh, string fixtureParent)
    {
        this.fixtureParent = Path.GetFullPath(fixtureParent);
        Root = Path.Combine(this.fixtureParent, "arpg-bootstrap-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Game); Directory.CreateDirectory(Path.Combine(Profile, "bootstrap")); Directory.CreateDirectory(Path.Combine(Profile, ".arpg"));
        File.WriteAllText(GameFile("valheim.exe"), "fixture executable; never launched"); File.WriteAllText(Path.Combine(Profile, ".arpg", "state.json"), "fixture state must remain byte-identical");
        foreach (var (name, bytes) in fresh)
        {
            File.WriteAllBytes(Path.Combine(Profile, "bootstrap", name), bytes);
            Manifest.Files.Add(new() { Path = "bootstrap/" + name, ArtifactId = name == "doorstop_config.ini" ? "fixture-config" : "denikson-BepInExPack_Valheim-5.4.2360", Size = bytes.Length, Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)) });
        }
    }
    public string GameFile(string name) => Path.Combine(Game, name);
    public void SetNative(Dictionary<string, byte[]> files)
    {
        foreach (var (name, bytes) in files) File.WriteAllBytes(GameFile(name), bytes);
        File.WriteAllText(GameFile("doorstop_config.ini"), "[General]\nenabled=false\n# personal native loader config, preserve exactly\n");
        foreach (var name in new[] { "winhttp.dll", ".doorstop_version", "doorstop_config.ini" }) if (File.Exists(GameFile(name))) File.SetLastWriteTimeUtc(GameFile(name), new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc));
    }
    public Dictionary<string, (string Hash, long Ticks)> SnapshotProfile() => Snapshot(Profile, false);
    public Dictionary<string, (string Hash, long Ticks)> SnapshotGame() => Snapshot(Game, true);
    private static Dictionary<string, (string, long)> Snapshot(string root, bool skipBackups) => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !skipBackups || !Path.GetRelativePath(root, p).Replace('\\', '/').StartsWith(".arpg-bootstrap-backups/")).ToDictionary(p => Path.GetRelativePath(root, p), p => (Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p))), File.GetLastWriteTimeUtc(p).Ticks));
    public void Dispose()
    {
        var temp = fixtureParent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Root);
        if (!full.StartsWith(temp + "arpg-bootstrap-test-", StringComparison.OrdinalIgnoreCase)) throw new IOException("Unsafe fixture cleanup path.");
        EngineStorage.SafeDeleteTree(temp, full);
    }
}
