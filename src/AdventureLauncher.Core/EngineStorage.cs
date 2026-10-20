using System.Security.Cryptography;
using System.Text.Json;

namespace AdventureLauncher.Core;

internal static class EngineStorage
{
    internal static string InProfile(string profile, string relative)
    {
        ManifestValidation.ValidateRelativePath(relative);
        var root = Path.GetFullPath(profile).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, ManifestValidation.Normalize(relative).Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path leaves the profile.");
        var current = root;
        foreach (var part in ManifestValidation.Normalize(relative).Split('/'))
        {
            current = Path.Combine(current, part);
            // The logical profile root may itself be the supported r2modman junction.
            try
            {
                // GetAttributes also sees a dangling link, unlike File.Exists/Directory.Exists.
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException($"Nested links are not supported: {current}");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    internal static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct));
    }

    internal static async Task<bool> MatchesAsync(string path, long size, string sha, CancellationToken ct)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != size) return false;
        return (await HashAsync(path, ct)).Equals(sha, StringComparison.OrdinalIgnoreCase);
    }

    internal static void AtomicJson<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, PackJson.Options);
        using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            output.Write(bytes);
            output.Flush(true);
        }
        File.Move(tmp, path, true);
    }

    internal static T? ReadJson<T>(string path) => !File.Exists(path) ? default :
        JsonSerializer.Deserialize<T>(File.ReadAllText(path), PackJson.Options);

    internal static void CopyDurable(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = Path.Combine(Path.GetDirectoryName(target)!, ".arpg-write-" + Guid.NewGuid().ToString("N"));
        var created = false;
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                input.CopyTo(output);
                output.Flush(true);
            }
            File.Move(temporary, target, true);
        }
        finally
        {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
    internal static void SafeDeleteTree(string metadataRoot, string target)
    {
        var root = Path.GetFullPath(metadataRoot).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(target);
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Refusing to clean outside launcher metadata.");
        if (!Directory.Exists(full)) return;
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new IOException("Refusing to clean linked metadata.");
        foreach (var path in Directory.EnumerateFileSystemEntries(full, "*", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Refusing to clean metadata containing a link.");
            if (Directory.Exists(path)) SafeDeleteTree(root, path);
            else File.Delete(path);
        }
        Directory.Delete(full);
    }
}
internal sealed class InstalledState
{
    public int SchemaVersion { get; set; } = 1;
    public string PackId { get; set; } = "";
    public string Version { get; set; } = "";
    public Dictionary<string, InstalledFile> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
internal sealed class InstalledFile
{
    public string SourceHash { get; set; } = "";
    public string ActualHash { get; set; } = "";
    public long Size { get; set; }
    public long LastWriteTicks { get; set; }
    public string Policy { get; set; } = "managed";
}
internal sealed class Transaction
{
    public string Status { get; set; } = "preparing";
    public string BackupId { get; set; } = "";
    public string Version { get; set; } = "";
    public List<TransactionEntry> Entries { get; set; } = [];
    public bool HadState { get; set; }
}
internal sealed class TransactionEntry
{
    public string Path { get; set; } = "";
    public bool Existed { get; set; }
    public bool Applied { get; set; }
    public string? BeforeHash { get; set; }
    public string? AfterHash { get; set; }
}
