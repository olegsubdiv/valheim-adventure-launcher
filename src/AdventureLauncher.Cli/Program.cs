using System.Text.Json;
using AdventureLauncher.Core;

if (args.Length == 0 || args[0] is "--help" or "help")
{
    Console.WriteLine("""
    Adventure Launcher maintainer CLI
      sign     --manifest payload.json --key private.pem --out envelope.json
      verify   --manifest envelope.json --key public-key.pem
      inspect  --manifest envelope.json --key public-key.pem --profile PATH [--full] [--local]
      apply    --manifest envelope.json --key public-key.pem --profile PATH --cache PATH [--imports imports.json] [--local]
      rollback --profile PATH
      company-code --out PRIVATE_CODE_FILE
      seal-sources --manifest signed-sources.json --key public-key.pem --code-file PRIVATE_CODE_FILE --out encrypted.json
      check-sources --encrypted encrypted.json (or --url HTTPS_URL) --key public-key.pem --code-file PRIVATE_CODE_FILE [--base-manifest stable.json]
      discover
    --local allows local/file/loopback sources for isolated development fixtures.
    Signing keys and imported archives are never uploaded by these commands.
    """);
    return 0;
}
var command = args[0];
var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
for (var i = 1; i < args.Length; i++)
{
    if (!args[i].StartsWith("--")) { Console.Error.WriteLine("Expected an option, got: " + args[i]); return 2; }
    var key = args[i][2..];
    options[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[++i] : "true";
}
string Required(string key) => options.TryGetValue(key, out var value) && value != "true" ? value : throw new ArgumentException("Missing --" + key);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
try
{
    if (command == "company-code")
    {
        var path = Path.GetFullPath(Required("out"));
        using var output = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write));
        output.Write(CompanyAccess.CreateCode());
        Console.WriteLine("Company code saved locally. Do not publish the code file.");
        return 0;
    }
    if (command == "seal-sources")
    {
        var signed = File.ReadAllText(Required("manifest"));
        CompanyAccess.ValidateSources(ManifestSigning.Verify(signed, File.ReadAllText(Required("key"))));
        File.WriteAllText(Required("out"), CompanyAccess.Seal(signed, File.ReadAllText(Required("code-file"))));
        Console.WriteLine("Encrypted company source list prepared.");
        return 0;
    }
    if (command == "check-sources")
    {
        var code = File.ReadAllText(Required("code-file"));
        var publicKey = File.ReadAllText(Required("key"));
        using var sourceHttp = new HttpClient();
        var sources = options.ContainsKey("url")
            ? await CompanyAccess.FetchAsync(sourceHttp, Required("url"), code, publicKey, cancel.Token)
            : CompanyAccess.Open(File.ReadAllText(Required("encrypted")), code, publicKey);
        if (options.TryGetValue("base-manifest", out var basePath))
        {
            var basePack = ManifestSigning.Verify(File.ReadAllText(basePath), publicKey);
            CompanyAccess.Apply(basePack, sources);
        }
        Console.WriteLine(JsonSerializer.Serialize(new { valid = true, artifacts = sources.Artifacts.Count }));
        return 0;
    }
    if (command == "sign")
    {
        var manifest = JsonSerializer.Deserialize<PackManifest>(File.ReadAllText(Required("manifest")), PackJson.Options) ?? throw new InvalidDataException("Invalid payload.");
        var envelope = ManifestSigning.Sign(manifest, File.ReadAllText(Required("key")));
        var destination = Path.GetFullPath(Required("out"));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, envelope);
        Console.WriteLine(JsonSerializer.Serialize(new { signed = destination, version = manifest.Version, files = manifest.Files.Count, artifacts = manifest.Artifacts.Count }, PackJson.Options));
        return 0;
    }
    if (command == "verify")
    {
        var manifest = ManifestSigning.Verify(File.ReadAllText(Required("manifest")), File.ReadAllText(Required("key")));
        Console.WriteLine(JsonSerializer.Serialize(new { valid = true, version = manifest.Version, files = manifest.Files.Count, artifacts = manifest.Artifacts.Count }, PackJson.Options));
        return 0;
    }
    if (command == "discover")
    {
        using var client = new LauncherController();
        Console.WriteLine(JsonSerializer.Serialize(new { games = client.DiscoverGames(), profiles = client.DiscoverProfiles() }, PackJson.Options));
        return 0;
    }
    using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
    var progress = new Progress<PackProgress>(p => Console.Error.WriteLine(LauncherController.Sanitize(p.Phase + ": " + p.Message)));
    var engine = new PackEngine(http, new EngineOptions { AllowLocalSources = options.ContainsKey("local") }, progress);
    if (command == "rollback")
    {
        var result = await engine.RollbackAsync(Required("profile"), cancel.Token);
        Console.WriteLine(JsonSerializer.Serialize(result, PackJson.Options));
        return 0;
    }
    var pack = await engine.FetchAsync(Required("manifest"), File.ReadAllText(Required("key")), cancel.Token);
    switch (command)
    {
        case "inspect":
            Console.WriteLine(JsonSerializer.Serialize(await engine.InspectAsync(pack, Required("profile"), options.ContainsKey("full"), cancel.Token), PackJson.Options));
            break;
        case "apply":
            if (System.Diagnostics.Process.GetProcessesByName("valheim").Length != 0) throw new InvalidOperationException("Close Valheim before applying a release.");
            Dictionary<string, string>? imports = options.TryGetValue("imports", out var importsPath)
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(importsPath)) : null;
            Console.WriteLine(JsonSerializer.Serialize(await engine.ApplyAsync(pack, Required("profile"), Required("cache"), imports, cancel.Token), PackJson.Options));
            break;
        default:
            throw new ArgumentException("Unknown command: " + command);
    }
    return 0;
}
catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled. Staged downloads can be resumed; installed files were protected by the transaction journal."); return 130; }
catch (Exception ex)
{
    Console.Error.WriteLine(LauncherController.Sanitize(ex.Message));
    if (ex is ManualArtifactRequiredException manual)
        Console.Error.WriteLine("Manual source: " + manual.Artifact.ManualUrl + "\nImport artifact id: " + manual.Artifact.Id);
    return 1;
}
