using System.IO.Compression;
using System.Text.Json;
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
    LauncherController.ValidatePaths(settings);
    controller.SaveSettings(settings);
    var saved = File.ReadAllText(Path.Combine(data, "settings.json"));
    Assert(!saved.Contains("secret-untrusted-feed") && !saved.Contains("never-persist-this-key"), "Trust must not be persisted in user-editable settings.");
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
    for (var i = 0; i < 4; i++) controller.CollectDiagnostics(settings);
    var diagnostics = Directory.GetFiles(Path.Combine(data, "diagnostics"), "*.zip");
    Assert(diagnostics.Length == 3, "Diagnostic retention failed.");
    using var archive = ZipFile.OpenRead(diagnostics[0]);
    Assert(archive.Entries.Sum(e => e.Length) < 8 * 1024 * 1024, "Diagnostics exceeded budget.");
    using var read = new StreamReader(archive.GetEntry("BepInEx_LogOutput.log")!.Open());
    Assert(!read.ReadToEnd().Contains("supersecret"), "Secret was included in diagnostics.");
    Console.WriteLine("PASS client: settings trust boundary, path guards, diagnostic redaction and retention. No game was launched.");
    return 0;
}
finally { if (Directory.Exists(fixture)) Directory.Delete(fixture, true); }
