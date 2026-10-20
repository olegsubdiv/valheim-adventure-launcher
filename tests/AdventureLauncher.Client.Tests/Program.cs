using System.IO.Compression;
using System.Text.Json;
using System.Security.Cryptography;
using AdventureLauncher.Core;

var fixture = Path.Combine(Path.GetTempPath(), "arpg-client-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    var game = Path.Combine(fixture, "game");
    var profile = Path.Combine(fixture, "profile");
    var data = Path.Combine(fixture, "app-data");
    Directory.CreateDirectory(game);
    Directory.CreateDirectory(profile);
    File.WriteAllText(Path.Combine(game, "valheim.exe"), "fixture, never executed");
    using var controller = new LauncherController(dataDirectory: data);
    var settings = new LauncherSettings { GamePath = game, ProfilePath = profile, CachePath = Path.Combine(fixture, "cache"), FeedUrl = "secret-untrusted-feed", PublicKeyPem = "never-persist-this-key" };
    var code = CompanyAccess.CreateCode();
    settings.CompanyCode = code;
    using var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    var sourceArtifact = new PackArtifact { Id = "private-hd", Kind = "7z", FileName = "hd.7z", Size = 123, Sha256 = new string('a', 64), Urls = ["https://drive.google.com/uc?export=download&id=fixture123456789"] };
    var sources = new PackManifest { PackId = "adventure-rpg-company-sources", Version = "1", Artifacts = [sourceArtifact] };
    var signed = ManifestSigning.Sign(sources, signingKey.ExportECPrivateKeyPem());
    var encrypted = CompanyAccess.Seal(signed, code);
    Assert(!encrypted.Contains("drive.google") && !encrypted.Contains(code) && !encrypted.Contains("private-hd"), "Encrypted source list leaked plaintext.");
    var opened = CompanyAccess.Open(encrypted, code, signingKey.ExportSubjectPublicKeyInfoPem());
    var pack = new PackManifest { Version = "test", Artifacts = [new PackArtifact { Id = sourceArtifact.Id, Kind = "7z", FileName = "hd.7z", Size = 123, Sha256 = sourceArtifact.Sha256 }] };
    CompanyAccess.Apply(pack, opened);
    Assert(pack.Artifacts[0].Urls.Count == 1 && pack.Files.Count == 0, "Company sources did not unlock the pinned archive.");
    try { CompanyAccess.Open(encrypted, CompanyAccess.CreateCode(), signingKey.ExportSubjectPublicKeyInfoPem()); throw new Exception("Wrong code accepted."); }
    catch (InvalidDataException) { }
    using var wrongSigningKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    try { CompanyAccess.Open(encrypted, code, wrongSigningKey.ExportSubjectPublicKeyInfoPem()); throw new Exception("Wrong signature accepted."); }
    catch (InvalidDataException) { }
    var tampered = JsonSerializer.Deserialize<JsonElement>(encrypted, PackJson.Options);
    var cipher = Convert.FromBase64String(tampered.GetProperty("ciphertext").GetString()!); cipher[0] ^= 1;
    var corrupt = JsonSerializer.Serialize(new { schemaVersion = 1, nonce = tampered.GetProperty("nonce").GetString(), ciphertext = Convert.ToBase64String(cipher), tag = tampered.GetProperty("tag").GetString() });
    try { CompanyAccess.Open(corrupt, code, signingKey.ExportSubjectPublicKeyInfoPem()); throw new Exception("Tampered ciphertext accepted."); }
    catch (InvalidDataException) { }
    pack.Artifacts[0].Urls.Clear(); pack.Artifacts[0].Size++;
    try { CompanyAccess.Apply(pack, opened); throw new Exception("Changed archive pin accepted."); }
    catch (InvalidDataException) { }
    Assert(pack.Artifacts[0].Urls.Count == 0, "Mismatch modified the pack.");
    Assert(!LauncherController.Sanitize("failure " + code).Contains(code), "Company code leaked through diagnostics.");
    LauncherController.ValidatePaths(settings);
    controller.SaveSettings(settings);
    var saved = File.ReadAllText(Path.Combine(data, "settings.json"));
    Assert(!saved.Contains("secret-untrusted-feed") && !saved.Contains("never-persist-this-key"), "Trust must not be persisted in user-editable settings.");
    Assert(controller.LoadSettings().CompanyCode == code, "Company code was not remembered.");
    Assert(controller.LoadSettings().ProfilePath == profile, "Settings round trip failed.");
    settings.CachePath = Path.Combine(profile, "cache");
    try { LauncherController.ValidatePaths(settings); throw new Exception("Nested cache accepted."); }
    catch (InvalidOperationException) { }
    settings.CachePath = Path.Combine(fixture, "cache");
    File.WriteAllText(Path.Combine(profile, "foreign-personal-document.txt"), "leave it alone");
    try { LauncherController.ValidatePaths(settings); throw new Exception("Unrelated nonempty folder accepted."); }
    catch (InvalidOperationException) { }
    File.Delete(Path.Combine(profile, "foreign-personal-document.txt"));
    var redacted = LauncherController.Sanitize(@"C:\Users\Somebody\Desktop password=supersecret refresh_token=abc https://example.test/file?token=hidden");
    Assert(!redacted.Contains("Somebody") && !redacted.Contains("supersecret") && !redacted.Contains("token=hidden"), "Diagnostic redaction failed.");
    var logs = Path.Combine(profile, "BepInEx");
    Directory.CreateDirectory(logs);
    File.WriteAllText(Path.Combine(logs, "LogOutput.log"), "password=supersecret");
    Directory.CreateDirectory(Path.Combine(data, "logs"));
    File.WriteAllText(Path.Combine(data, "logs", "desktop-error.log"), "startup failure " + code);
    for (var i = 0; i < 4; i++) controller.CollectDiagnostics(settings);
    var diagnostics = Directory.GetFiles(Path.Combine(data, "diagnostics"), "*.zip");
    Assert(diagnostics.Length == 3, "Diagnostic retention failed.");
    using var archive = ZipFile.OpenRead(diagnostics[0]);
    Assert(archive.Entries.Sum(e => e.Length) < 8 * 1024 * 1024, "Diagnostics exceeded budget.");
    using (var desktopRead = new StreamReader(archive.GetEntry("desktop-error.log")!.Open()))
        Assert(!desktopRead.ReadToEnd().Contains(code), "Desktop startup diagnostics leaked the company code.");
    using var read = new StreamReader(archive.GetEntry("BepInEx_LogOutput.log")!.Open());
    Assert(!read.ReadToEnd().Contains("supersecret"), "Secret was included in diagnostics.");
    Console.WriteLine("PASS client: settings trust boundary, path guards, diagnostic redaction and retention. No game was launched.");
    return 0;
}
finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
