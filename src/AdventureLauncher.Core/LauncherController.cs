using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace AdventureLauncher.Core;

public sealed class LauncherSettings
{
    public string GamePath { get; set; } = "";
    public string ProfilePath { get; set; } = "";
    public string CachePath { get; set; } = "";
    public string FeedUrl { get; set; } = "";
    public string CompanySourcesUrl { get; set; } = "";
    public string CompanyCode { get; set; } = "";
    public string TextureQuality { get; set; } = "";
    public string TextureVariantUrl { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public Dictionary<string, string> ImportedArtifacts { get; set; } = [];
}

public sealed class LauncherController : IDisposable
{
    private readonly string dataDirectory;
    private readonly IProgress<PackProgress>? progress;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(30) };
    private readonly object logGate = new();
    public LauncherController(IProgress<PackProgress>? progress = null, string? dataDirectory = null)
    {
        this.progress = progress;
        this.dataDirectory = dataDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValheimAdventureLauncher");
        Directory.CreateDirectory(this.dataDirectory);
    }
    public LauncherSettings LoadSettings()
    {
        try
        {
            var path = Path.Combine(dataDirectory, "settings.json");
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(path), PackJson.Options);
                if (settings is not null) return settings;
            }
        }
        catch (Exception ex) { Report(new("warning", "Settings could not be loaded: " + ex.Message)); }
        var profile = DiscoverProfiles().FirstOrDefault() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ValheimMods", "Adventure RPG");
        return new()
        {
            GamePath = DiscoverGames().FirstOrDefault() ?? "",
            ProfilePath = profile,
            CachePath = DefaultCachePath(profile)
        };
    }
    public static string DefaultCachePath(string profile)
    {
        var full = Path.GetFullPath(profile);
        var physical = Directory.Exists(full) ? new DirectoryInfo(full).ResolveLinkTarget(true)?.FullName ?? full : full;
        return Path.Combine(Path.GetDirectoryName(physical)!, ".arpg-cache");
    }
    public void SaveSettings(LauncherSettings settings)
    {
        // Trust is supplied from the application bundle, never from user settings or a remote manifest.
        var safe = new LauncherSettings
        {
            GamePath = settings.GamePath, ProfilePath = settings.ProfilePath, CachePath = settings.CachePath,
            CompanyCode = settings.CompanyCode, TextureQuality = settings.TextureQuality, ImportedArtifacts = settings.ImportedArtifacts
        };
        var destination = Path.Combine(dataDirectory, "settings.json");
        var temporary = destination + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(safe, PackJson.Options), Encoding.UTF8);
        File.Move(temporary, destination, true);
    }
    public IReadOnlyList<string> DiscoverGames()
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows()) return result;
        var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        if (string.IsNullOrEmpty(steam)) return result;
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
            foreach (Match match in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
        foreach (var library in libraries)
        {
            var game = Path.Combine(library, "steamapps", "common", "Valheim");
            if (File.Exists(Path.Combine(game, "valheim.exe"))) result.Add(game);
        }
        return result;
    }
    public IReadOnlyList<string> DiscoverProfiles()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "r2modmanPlus-local", "Valheim", "profiles");
        if (!Directory.Exists(root)) return [];
        return Directory.GetDirectories(root).Where(p => File.Exists(Path.Combine(p, "BepInEx", "core", "BepInEx.Preloader.dll")))
            .OrderBy(p => !Path.GetFileName(p).Equals("Adventure RPG", StringComparison.OrdinalIgnoreCase)).ThenBy(p => p).ToArray();
    }
    public async Task<PackPlan> CheckAsync(LauncherSettings settings, bool fullHash = false, CancellationToken ct = default)
    {
        ValidatePaths(settings);
        var engine = Engine();
        var manifest = await FetchCollectionAsync(settings, engine, ct);
        return await engine.InspectAsync(manifest, settings.ProfilePath, fullHash, ct);
    }
    public async Task<EngineResult> UpdateAsync(LauncherSettings settings, CancellationToken ct = default)
    {
        ValidatePaths(settings);
        RequireGameClosed();
        var engine = Engine();
        var manifest = await FetchCollectionAsync(settings, engine, ct);
        ValidateBootstrap(manifest, settings.GamePath);
        var result = await engine.ApplyAsync(manifest, settings.ProfilePath, settings.CachePath, settings.ImportedArtifacts, ct);
        EnsureBootstrap(manifest, settings);
        settings.TextureQuality = manifest.TextureQuality ?? TextureVariants.ResolveTextureQuality(settings.ProfilePath);
        SaveSettings(settings);
        InstallShortcuts();
        return result;
    }
    public async Task<EngineResult> RollbackAsync(LauncherSettings settings, CancellationToken ct = default)
    {
        ValidatePaths(settings); RequireGameClosed();
        var result = await Engine().RollbackAsync(settings.ProfilePath, ct);
        settings.TextureQuality = TextureVariants.ResolveTextureQuality(settings.ProfilePath);
        SaveSettings(settings);
        return result;
    }
    private async Task<PackManifest> FetchCollectionAsync(LauncherSettings settings, PackEngine engine, CancellationToken ct)
    {
        var manifest = await engine.FetchAsync(settings.FeedUrl, settings.PublicKeyPem, ct);
        var quality = TextureVariants.ResolveTextureQuality(settings.ProfilePath, settings.TextureQuality);
        PackManifest? variant = null;
        if (quality == TextureVariants.Medium)
        {
            if (string.IsNullOrWhiteSpace(settings.TextureVariantUrl))
                throw new InvalidDataException("The bundled Medium texture channel is missing. Update the launcher.");
            variant = await engine.FetchAsync(settings.TextureVariantUrl, settings.PublicKeyPem, ct);
        }
        manifest = TextureVariants.Compose(manifest, quality, variant);
        if (!string.IsNullOrWhiteSpace(settings.CompanyCode))
        {
            var sources = await CompanyAccess.FetchAsync(http, settings.CompanySourcesUrl, settings.CompanyCode, settings.PublicKeyPem, ct);
            CompanyAccess.Apply(manifest, sources);
            Report(new("access", "Company downloads unlocked."));
        }
        return manifest;
    }
    private PackEngine Engine() => new(http, new EngineOptions(), new InlineProgress(Report));
    public void Launch(LauncherSettings settings)
    {
        ValidatePaths(settings); RequireGameClosed();
        var preloader = Path.Combine(settings.ProfilePath, "BepInEx", "core", "BepInEx.Preloader.dll");
        if (!File.Exists(preloader)) throw new InvalidOperationException("This profile is not installed. Use Update and play first.");
        if (!File.Exists(Path.Combine(settings.GamePath, "winhttp.dll")))
            throw new InvalidOperationException("The game loader is missing. Use Update and play to repair it.");
        if (Process.GetProcessesByName("steam").Length == 0)
            throw new InvalidOperationException("Start Steam and sign in, then press Play again.");
        var steam = OperatingSystem.IsWindows() ? Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamExe", null) as string : null;
        var metadata = EngineStorage.InProfile(settings.ProfilePath, ".arpg");
        Directory.CreateDirectory(metadata);
        using var operation = new FileStream(EngineStorage.InProfile(settings.ProfilePath, ".arpg/operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (File.Exists(EngineStorage.InProfile(settings.ProfilePath, ".arpg/transaction.json")))
            throw new InvalidOperationException("An update was interrupted. Use Install / update to restore a complete profile before playing.");
        var marker = EngineStorage.InProfile(settings.ProfilePath, ".arpg/launch-pending.json");
        if (File.Exists(marker))
        {
            using var prior = JsonDocument.Parse(File.ReadAllText(marker));
            if (prior.RootElement.TryGetProperty("startedAt", out var timestamp) &&
                DateTimeOffset.UtcNow - timestamp.GetDateTimeOffset() < TimeSpan.FromMinutes(3))
                throw new InvalidOperationException("Steam is already starting Valheim. Wait for the game to open.");
        }
        var launchId = Guid.NewGuid().ToString("N");
        EngineStorage.AtomicJson(marker, new { startedAt = DateTimeOffset.UtcNow, launchId });
        ProcessStartInfo start;
        if (!string.IsNullOrWhiteSpace(steam) && File.Exists(steam))
        {
            start = new(steam) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(steam)! };
            start.ArgumentList.Add("-applaunch"); start.ArgumentList.Add("892970");
        }
        else start = new(Path.Combine(settings.GamePath, "valheim.exe")) { UseShellExecute = true, WorkingDirectory = settings.GamePath };
        start.ArgumentList.Add("--doorstop-enabled"); start.ArgumentList.Add("true");
        start.ArgumentList.Add("--doorstop-target-assembly"); start.ArgumentList.Add(preloader);
        try { Process.Start(start); }
        catch { File.Delete(marker); throw; }
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < 180; i++)
                {
                    await Task.Delay(1000);
                    if (Process.GetProcessesByName("valheim").Length > 0) break;
                }
                using var document = JsonDocument.Parse(File.ReadAllText(marker));
                if (document.RootElement.TryGetProperty("launchId", out var current) && current.GetString() == launchId)
                    File.Delete(marker);
            }
            catch (Exception) { /* A stale launch intent expires after three minutes. */ }
        });
        Report(new("launch", "Steam is starting Valheim. First modded startup can take several minutes."));
    }
    public string CollectDiagnostics(LauncherSettings settings)
    {
        var folder = Path.Combine(dataDirectory, "diagnostics");
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, "diagnostics-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            WriteZip(archive, "summary.txt", "Valheim Adventure Launcher diagnostics\nUTC: " + DateTimeOffset.UtcNow + "\nOS: " + Environment.OSVersion +
                "\nGame present: " + File.Exists(Path.Combine(settings.GamePath, "valheim.exe")) +
                "\nProfile preloader present: " + File.Exists(Path.Combine(settings.ProfilePath, "BepInEx", "core", "BepInEx.Preloader.dll")));
            foreach (var file in Directory.GetFiles(dataDirectory, "launcher*.log").Take(3))
                WriteZip(archive, Path.GetFileName(file), ReadTail(file, 256 * 1024));
            var desktopError = Path.Combine(dataDirectory, "logs", "desktop-error.log");
            if (File.Exists(desktopError)) WriteZip(archive, "desktop-error.log", ReadTail(desktopError, 64 * 1024));
            foreach (var relative in new[] { "BepInEx/LogOutput.log", "BepInEx/LogOutput.log.1" })
            {
                var file = Path.Combine(settings.ProfilePath, relative);
                if (File.Exists(file)) WriteZip(archive, relative.Replace("/", "_"), ReadTail(file, 512 * 1024));
            }
            var playerLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow", "IronGate", "Valheim", "Player.log");
            if (File.Exists(playerLog)) WriteZip(archive, "Player.log.tail.txt", ReadTail(playerLog, 512 * 1024));
        }
        foreach (var old in new DirectoryInfo(folder).GetFiles("diagnostics-*.zip").OrderByDescending(f => f.LastWriteTimeUtc).Skip(3)) old.Delete();
        Report(new("diagnostics", "Diagnostics saved: " + zipPath));
        return zipPath;
    }
    public static void ValidatePaths(LauncherSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ProfilePath) || string.IsNullOrWhiteSpace(settings.CachePath))
            throw new InvalidOperationException("Choose a profile folder and cache location.");
        if (string.IsNullOrWhiteSpace(settings.GamePath) || !File.Exists(Path.Combine(settings.GamePath, "valheim.exe")))
            throw new InvalidOperationException("Choose the Steam Valheim folder containing valheim.exe.");
        var game = Path.GetFullPath(settings.GamePath).TrimEnd(Path.DirectorySeparatorChar);
        var profile = Path.GetFullPath(settings.ProfilePath).TrimEnd(Path.DirectorySeparatorChar);
        var cache = Path.GetFullPath(settings.CachePath).TrimEnd(Path.DirectorySeparatorChar);
        if (profile.Equals(Path.GetPathRoot(profile)?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a dedicated profile folder, not a drive root.");
        static bool Contains(string parent, string child) => child.Equals(parent, StringComparison.OrdinalIgnoreCase) || child.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        static string Physical(string path) => Directory.Exists(path) ? new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName.TrimEnd(Path.DirectorySeparatorChar) ?? path : path;
        var physicalGame = Physical(game);
        var physicalProfile = Physical(profile);
        var physicalCache = Physical(cache);
        if (Contains(game, profile) || Contains(profile, game) || Contains(profile, cache) || Contains(cache, profile) ||
            Contains(game, cache) || Contains(cache, game) ||
            Contains(physicalGame, physicalProfile) || Contains(physicalProfile, physicalGame) ||
            Contains(physicalProfile, physicalCache) || Contains(physicalCache, physicalProfile) ||
            Contains(physicalGame, physicalCache) || Contains(physicalCache, physicalGame))
            throw new InvalidOperationException("Game, profile and cache must use separate folders.");
        if (Directory.Exists(profile) && Directory.EnumerateFileSystemEntries(profile).Any() &&
            !Directory.Exists(Path.Combine(profile, "BepInEx")) && !Directory.Exists(Path.Combine(profile, ".arpg")))
            throw new InvalidOperationException("The selected folder contains unrelated files. Choose an empty folder or an existing BepInEx profile.");
    }
    private static void RequireGameClosed()
    {
        if (Process.GetProcessesByName("valheim").Length != 0)
            throw new InvalidOperationException("Close Valheim before updating, repairing or starting another copy.");
    }
    private static void ValidateBootstrap(PackManifest manifest, string game)
    {
        foreach (var file in manifest.Files.Where(f => f.Path.StartsWith("bootstrap/", StringComparison.OrdinalIgnoreCase)))
        {
            var name = file.Path["bootstrap/".Length..];
            if (name != Path.GetFileName(name) || name is not ("winhttp.dll" or "doorstop_config.ini" or ".doorstop_version"))
                throw new InvalidOperationException("Unsupported loader bootstrap file.");
            var target = Path.Combine(game, name);
            if (name == "doorstop_config.ini" || !File.Exists(target)) continue;
            using var stream = File.OpenRead(target);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            if (!hash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("A different mod loader exists in the game folder (" + name + "). Nothing was changed.");
        }
    }
    private static void EnsureBootstrap(PackManifest manifest, LauncherSettings settings)
    {
        ValidateBootstrap(manifest, settings.GamePath);
        foreach (var file in manifest.Files.Where(f => f.Path.StartsWith("bootstrap/", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(file.Path);
            var target = Path.Combine(settings.GamePath, name);
            if (!File.Exists(target)) File.Copy(Path.Combine(settings.ProfilePath, file.Path), target, false);
        }
    }
    public void InstallShortcuts()
    {
        if (!OperatingSystem.IsWindows()) return;
        var executable = Environment.ProcessPath;
        if (executable is null || !Path.GetFileName(executable).Equals("AdventureLauncher.exe", StringComparison.OrdinalIgnoreCase)) return;
        foreach (var folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
        {
            try
            {
                if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) continue;
                var shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType is null) continue;
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic shortcut = shell.CreateShortcut(Path.Combine(folder, "Valheim Adventure RPG.lnk"));
                shortcut.TargetPath = executable;
                shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
                shortcut.Description = "Update and play Valheim Adventure RPG";
                shortcut.Save();
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex) { Report(new("warning", "The collection is installed. Shortcut could not be saved: " + ex.Message + ". Launch the app from its installed folder.")); }
        }
    }
    private void Report(PackProgress value)
    {
        lock (logGate)
        {
            var log = Path.Combine(dataDirectory, "launcher.log");
            try
            {
                if (File.Exists(log) && new FileInfo(log).Length >= 256 * 1024)
                {
                    var oldest = Path.Combine(dataDirectory, "launcher.2.log");
                    if (File.Exists(oldest)) File.Delete(oldest);
                    var prior = Path.Combine(dataDirectory, "launcher.1.log");
                    if (File.Exists(prior)) File.Move(prior, oldest);
                    File.Move(log, prior, true);
                }
                var message = Sanitize(value.Message);
                if (message.Length > 4096) message = message[..4096];
                File.AppendAllText(log, DateTime.UtcNow.ToString("O") + " [" + value.Phase + "] " + message + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
        }
        progress?.Report(value);
    }
    public static string Sanitize(string text)
    {
        text = Regex.Replace(text, @"ARPG1-[A-Za-z0-9_-]{43}", "<company-code-redacted>");
        text = Regex.Replace(text, @"(?i)([A-Z]:\\Users\\)[^\\\s]+", "$1<user>");
        text = Regex.Replace(text, @"(?i)(password|passwd|access_token|refresh_token|authorization|client_secret)(\s*[:=]\s*|\s+)[^\s,;]+", "$1=<redacted>");
        text = Regex.Replace(text, @"(https?://[^\s?]+)\?[^\s]+", "$1?<redacted>");
        return text;
    }
    private static string ReadTail(string path, int bytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > bytes) stream.Seek(-bytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd();
    }
    private static void WriteZip(ZipArchive archive, string name, string text)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Fastest).Open(), new UTF8Encoding(false));
        writer.Write(Sanitize(text));
    }
    public void Dispose() => http.Dispose();
    private sealed class InlineProgress(Action<PackProgress> action) : IProgress<PackProgress>
    {
        public void Report(PackProgress value) => action(value);
    }
}
