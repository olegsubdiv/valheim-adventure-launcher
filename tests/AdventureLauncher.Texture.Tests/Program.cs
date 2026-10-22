using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AdventureLauncher.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("signed scope and exact baseline pin", Scope),
    ("Higher/Medium transitions, idempotence and rollback", Transitions),
    ("cancelled texture switch restores previous variant", Cancellation),
    ("legacy Higher-only cleanup is pinned and transactional", Legacy),
    ("new profile default, existing quality and settings trust", Defaults)
};
var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} texture checks passed; no game was launched.");
return failures == 0 ? 0 : 1;

static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
static async Task Throws<T>(Func<Task> action) where T : Exception
{
    try { await action(); } catch (T) { return; }
    throw new Exception("Expected " + typeof(T).Name);
}
static PackEngine Engine(IProgress<PackProgress>? progress = null) => new(new HttpClient(), new EngineOptions { AllowLocalSources = true }, progress);
static PackManifest Clone(PackManifest value) => JsonSerializer.Deserialize<PackManifest>(JsonSerializer.Serialize(value, PackJson.Options), PackJson.Options)!;

static async Task Scope()
{
    using var f = new Fixture();
    using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var signedBase = ManifestSigning.Sign(f.Higher, key.ExportECPrivateKeyPem());
    var signedVariant = ManifestSigning.Sign(f.Medium, key.ExportECPrivateKeyPem());
    var baseline = ManifestSigning.Verify(signedBase, key.ExportSubjectPublicKeyInfoPem());
    var variant = ManifestSigning.Verify(signedVariant, key.ExportSubjectPublicKeyInfoPem());
    var composed = TextureVariants.Compose(baseline, "medium", variant);
    Assert(composed.TextureQuality == "medium" && composed.Version != baseline.Version, "Effective variant identity missing.");
    Assert(composed.Artifacts.All(a => a.Id != TextureVariants.HigherArtifactId), "Higher download remained in Medium manifest.");
    Assert(baseline.Files.Any(p => p.Path.EndsWith("higher-only.bin")) && baseline.TextureQuality == null, "Composition mutated verified baseline.");
    Assert(composed.Files.Single(p => p.Path == Fixture.Balance).Sha256 == baseline.Files.Single(p => p.Path == Fixture.Balance).Sha256, "Balance pin changed.");
    foreach (var forbidden in new[] { Fixture.Balance, "BepInEx/plugins/Evil.dll", "bootstrap/winhttp.dll", "BepInEx/config/HDValheimTextures.cfg" })
    {
        var bad = Clone(variant); bad.Files[0].Path = forbidden;
        await Throws<InvalidDataException>(() => Task.FromResult(TextureVariants.Compose(baseline, "medium", bad)));
    }
    var mismatch = Clone(variant); mismatch.TextureVariant!.BaseArtifactSha256 = new string('0', 64);
    await Throws<InvalidDataException>(() => Task.FromResult(TextureVariants.Compose(baseline, "medium", mismatch)));
    var config = Clone(variant); config.Files[0].Policy = "config";
    await Throws<InvalidDataException>(() => Task.FromResult(TextureVariants.Compose(baseline, "medium", config)));
    var envelope = JsonSerializer.Deserialize<SignedEnvelope>(signedVariant, PackJson.Options)!;
    variant.TextureVariant!.BaseArtifactSha256 = new string('1', 64);
    envelope.Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(variant, PackJson.Options));
    await Throws<CryptographicException>(() => Task.FromResult(ManifestSigning.Verify(JsonSerializer.Serialize(envelope, PackJson.Options), key.ExportSubjectPublicKeyInfoPem())));
    var unchanged = TextureVariants.Compose(baseline, "higher");
    Assert(unchanged.Version == baseline.Version && unchanged.Files.Count == baseline.Files.Count, "Higher baseline compatibility changed.");
}

static async Task Transitions()
{
    using var f = new Fixture(); var engine = Engine();
    var higher = TextureVariants.Compose(f.Higher, "higher");
    var medium = TextureVariants.Compose(f.Higher, "medium", f.Medium);
    await engine.ApplyAsync(higher, f.Profile, f.Cache);
    var balance = File.ReadAllBytes(f.PathOf(Fixture.Balance));
    var balanceTime = File.GetLastWriteTimeUtc(f.PathOf(Fixture.Balance));
    var plan = await engine.InspectAsync(medium, f.Profile);
    Assert(plan.RequiredArtifactIds.SequenceEqual(new[] { TextureVariants.MediumArtifactId }) && plan.RemovedFiles.Single().EndsWith("higher-only.bin"), "Switch planned non-texture work.");
    await engine.ApplyAsync(medium, f.Profile, f.Cache);
    Assert(!File.Exists(f.PathOf(Fixture.HigherOnly)) && File.Exists(f.PathOf(Fixture.MediumOnly)), "Higher-only data was not replaced.");
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "medium", "Installed Medium quality not recorded.");
    var again = await engine.ApplyAsync(medium, f.Profile, f.Cache);
    Assert(again.WrittenFiles == 0 && again.RemovedFiles == 0, "Identical Medium install rewrote files.");
    await engine.RollbackAsync(f.Profile);
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "higher" && File.Exists(f.PathOf(Fixture.HigherOnly)) && !File.Exists(f.PathOf(Fixture.MediumOnly)), "Rollback did not restore Higher inventory/state.");
    await engine.ApplyAsync(medium, f.Profile, f.Cache);
    await engine.ApplyAsync(higher, f.Profile, f.Cache);
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "higher" && !File.Exists(f.PathOf(Fixture.MediumOnly)), "Medium to Higher switch left data behind.");
    await engine.RollbackAsync(f.Profile);
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "medium" && !File.Exists(f.PathOf(Fixture.HigherOnly)), "Rollback did not restore Medium inventory/state.");
    Assert(balance.SequenceEqual(File.ReadAllBytes(f.PathOf(Fixture.Balance))) && balanceTime == File.GetLastWriteTimeUtc(f.PathOf(Fixture.Balance)), "Texture switches wrote gameplay config.");
    f.Write(Fixture.MediumOnly, "personal modification");
    await Throws<IOException>(() => engine.ApplyAsync(higher, f.Profile, f.Cache));
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "medium" && File.ReadAllText(f.PathOf(Fixture.MediumOnly)) == "personal modification", "Switch to Higher hid a modified Medium-only file.");
}

static async Task Cancellation()
{
    using var f = new Fixture();
    await Engine().ApplyAsync(TextureVariants.Compose(f.Higher, "higher"), f.Profile, f.Cache);
    var before = File.ReadAllText(f.PathOf(Fixture.Plugin));
    using var cancellation = new CancellationTokenSource();
    var engine = Engine(new SyncProgress(p => { if (p.Phase == "apply") cancellation.Cancel(); }));
    await Throws<OperationCanceledException>(() => engine.ApplyAsync(TextureVariants.Compose(f.Higher, "medium", f.Medium), f.Profile, f.Cache, ct: cancellation.Token));
    Assert(File.ReadAllText(f.PathOf(Fixture.Plugin)) == before && TextureVariants.ResolveTextureQuality(f.Profile) == "higher", "Cancellation left mixed texture data/state.");
    Assert(File.Exists(f.PathOf(Fixture.HigherOnly)) && !File.Exists(f.PathOf(Fixture.MediumOnly)), "Cancellation lost variant-only data.");
}

static async Task Legacy()
{
    using var f = new Fixture();
    // A legacy r2modman profile has no launcher state. Seed its exact signed Higher bytes.
    foreach (var (path, content) in f.HigherContents) f.Write(path, content);
    var engine = Engine(); var medium = TextureVariants.Compose(f.Higher, "medium", f.Medium);
    var plan = await engine.InspectAsync(medium, f.Profile);
    Assert(plan.RemovedFiles.Contains(Fixture.HigherOnly), "Untracked Higher-only pinned file was not adopted.");
    await engine.ApplyAsync(medium, f.Profile, f.Cache);
    Assert(!File.Exists(f.PathOf(Fixture.HigherOnly)), "Legacy Higher-only file remained installed.");
    await engine.RollbackAsync(f.Profile);
    Assert(File.ReadAllText(f.PathOf(Fixture.HigherOnly)) == "higher-only" && !File.Exists(f.PathOf(".arpg/state.json")), "Legacy rollback did not restore original profile/state absence.");
    f.Write(Fixture.HigherOnly, "personal modification");
    await Throws<IOException>(() => engine.ApplyAsync(medium, f.Profile, f.Cache));
    Assert(File.ReadAllText(f.PathOf(Fixture.HigherOnly)) == "personal modification" && File.ReadAllText(f.PathOf(Fixture.Plugin)) == "higher-plugin", "Modified legacy data was deleted or other files changed.");
}

static Task Defaults()
{
    using var f = new Fixture();
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "medium", "Fresh profile did not default to Medium.");
    Assert(TextureVariants.ResolveTextureQuality("") == "medium" && TextureVariants.ResolveTextureQuality("C:\\invalid\0") == "medium", "Partial editable path threw an error.");
    f.Write(Fixture.Plugin, "existing plugin");
    Assert(TextureVariants.ResolveTextureQuality(f.Profile) == "higher", "Existing Higher installation was downgraded by default.");
    Assert(TextureVariants.ResolveTextureQuality(f.Profile, "Medium") == "medium", "Explicit player choice ignored.");
    using var controller = new LauncherController(dataDirectory: Path.Combine(f.Root, "app-data"));
    controller.SaveSettings(new LauncherSettings { ProfilePath = f.Profile, TextureQuality = "medium", TextureVariantUrl = "untrusted-texture-channel" });
    Assert(controller.LoadSettings().TextureQuality == "medium" && !File.ReadAllText(Path.Combine(f.Root, "app-data/settings.json")).Contains("untrusted-texture-channel"), "Texture preference or trust persistence incorrect.");
    return Task.CompletedTask;
}

sealed class SyncProgress(Action<PackProgress> action) : IProgress<PackProgress> { public void Report(PackProgress value) => action(value); }
sealed class Fixture : IDisposable
{
    public const string Plugin = "BepInEx/plugins/HDValheimTextures.dll";
    public const string Textures = "BepInEx/plugins/HDValheimTexturesData/Textures.dat";
    public const string HigherOnly = "BepInEx/plugins/HDValheimTexturesData/higher-only.bin";
    public const string MediumOnly = "BepInEx/plugins/HDValheimTexturesData/medium-only.bin";
    public const string Balance = "BepInEx/config/gameplay.cfg";
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "arpg-textures-" + Guid.NewGuid().ToString("N"));
    public string Profile => Path.Combine(Root, "profile");
    public string Cache => Path.Combine(Root, "cache");
    public PackManifest Higher { get; }
    public PackManifest Medium { get; }
    public Dictionary<string, string> HigherContents { get; } = new() { [Plugin] = "higher-plugin", [Textures] = "higher-textures", [HigherOnly] = "higher-only" };
    public Fixture()
    {
        Directory.CreateDirectory(Root);
        Higher = Archive(TextureVariants.HigherArtifactId, HigherContents);
        var gameplay = Archive("baseline-config", new() { [Balance] = "[Balance]\nHealth = 120\nDamage = 120\n" });
        Higher.Artifacts.AddRange(gameplay.Artifacts); Higher.Files.AddRange(gameplay.Files);
        Medium = Archive(TextureVariants.MediumArtifactId, new() { [Plugin] = "medium-plugin", [Textures] = "medium-textures", [MediumOnly] = "medium-only" });
        Medium.PackId = "adventure-rpg-textures-medium";
        Medium.TextureVariant = new() { Id = "medium", BaseArtifactId = TextureVariants.HigherArtifactId, BaseArtifactSha256 = Higher.Artifacts[0].Sha256 };
    }
    private PackManifest Archive(string id, Dictionary<string, string> contents)
    {
        // Windows tar detects this tiny synthetic ZIP by magic, exercising the 7z extraction route without real HD data.
        var archive = Path.Combine(Root, id + ".7z");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
            foreach (var (path, text) in contents) { using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false)); writer.Write(text); }
        var artifact = new PackArtifact { Id = id, Kind = "7z", FileName = Path.GetFileName(archive), Size = new FileInfo(archive).Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(archive))), Urls = [archive] };
        return new PackManifest { Version = "test-1", Artifacts = [artifact], Files = contents.Select(p => new PackFile { Path = p.Key, Entry = p.Key, ArtifactId = id,
            Size = Encoding.UTF8.GetByteCount(p.Value), Sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(p.Value))) }).ToList() };
    }
    public string PathOf(string relative) => Path.Combine(Profile, relative.Replace('/', Path.DirectorySeparatorChar));
    public void Write(string relative, string text) { var path = PathOf(relative); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, text, new UTF8Encoding(false)); }
    public void Dispose()
    {
        var full = Path.GetFullPath(Root);
        if (Path.GetDirectoryName(full) != Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) || !Path.GetFileName(full).StartsWith("arpg-textures-")) throw new IOException("Unexpected fixture path.");
        if (Directory.Exists(full)) Directory.Delete(full, true);
    }
}
