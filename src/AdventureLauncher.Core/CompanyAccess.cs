using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdventureLauncher.Core;

// The code is a randomly generated 256-bit key, not a user-chosen password.
// Public ciphertext contains an independently signed, narrowly scoped source list.
public static class CompanyAccess
{
    private static readonly byte[] Context = Encoding.UTF8.GetBytes("adventure-rpg-company-sources-v1");
    public static string CreateCode() => "ARPG1-" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Key(string code)
    {
        code = code.Trim();
        if (!Regex.IsMatch(code, @"^ARPG1-[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant))
            throw new InvalidDataException("Enter the company code supplied by the pack maintainer.");
        return Convert.FromBase64String(code[6..].Replace('-', '+').Replace('_', '/') + "=");
    }
    public static string Seal(string signedSources, string code)
    {
        var key = Key(code);
        try
        {
            var plain = Encoding.UTF8.GetBytes(signedSources);
            if (plain.Length > 256 * 1024) throw new InvalidDataException("Company source list is too large.");
            var nonce = RandomNumberGenerator.GetBytes(12);
            var encrypted = new byte[plain.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(key, 16);
            aes.Encrypt(nonce, plain, encrypted, tag, Context);
            return JsonSerializer.Serialize(new Envelope(1, Convert.ToBase64String(nonce), Convert.ToBase64String(encrypted), Convert.ToBase64String(tag)), PackJson.Options);
        }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static PackManifest Open(string encryptedJson, string code, string publicKeyPem)
    {
        if (encryptedJson.Length > 512 * 1024) throw new InvalidDataException("Company source list is too large.");
        var key = Key(code);
        try
        {
            var envelope = JsonSerializer.Deserialize<Envelope>(encryptedJson, PackJson.Options) ?? throw new InvalidDataException("Missing company source list.");
            if (envelope.SchemaVersion != 1) throw new InvalidDataException("Unsupported company source list.");
            var nonce = Convert.FromBase64String(envelope.Nonce);
            var cipher = Convert.FromBase64String(envelope.Ciphertext);
            var tag = Convert.FromBase64String(envelope.Tag);
            if (nonce.Length != 12 || tag.Length != 16 || cipher.Length > 256 * 1024) throw new InvalidDataException("Invalid company source list.");
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, cipher, tag, plain, Context);
            var sources = ManifestSigning.Verify(Encoding.UTF8.GetString(plain), publicKeyPem);
            ValidateSources(sources);
            return sources;
        }
        catch (CryptographicException) { throw new InvalidDataException("The company code is incorrect, or the source list could not be verified."); }
        finally { CryptographicOperations.ZeroMemory(key); }
    }
    public static void ValidateSources(PackManifest sources)
    {
        ManifestValidation.Validate(sources);
        if (sources.PackId != "adventure-rpg-company-sources" || sources.Files.Count != 0 || sources.Artifacts.Count is < 1 or > 16)
            throw new InvalidDataException("Invalid company source list scope.");
        foreach (var artifact in sources.Artifacts)
            if (artifact.Urls.Count != 1 || !Uri.TryCreate(artifact.Urls[0], UriKind.Absolute, out var uri) || !DriveDownloads.IsDrive(uri))
                throw new InvalidDataException("Company downloads must use Google Drive HTTPS sources.");
    }
    public static void Apply(PackManifest manifest, PackManifest sources)
    {
        ValidateSources(sources);
        var changes = new List<(PackArtifact Target, PackArtifact Source)>();
        foreach (var source in sources.Artifacts)
        {
            var target = manifest.Artifacts.SingleOrDefault(a => a.Id == source.Id);
            if (target == null) continue; // An old grant cannot add a removed mod.
            if (target.Urls.Count != 0) continue; // Catalogue sources remain unchanged.
            if (target.Size != source.Size || target.Kind != source.Kind || !target.Sha256.Equals(source.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The company source list does not match this collection. Ask the maintainer to update it.");
            changes.Add((target, source));
        }
        foreach (var (target, source) in changes) target.Urls = new(source.Urls);
    }
    public static async Task<PackManifest> FetchAsync(HttpClient http, string url, string code, string publicKeyPem, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidDataException("Company source channel must use HTTPS.");
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var count = await stream.ReadAsync(buffer, ct);
            if (count == 0) break;
            if (output.Length + count > 512 * 1024) throw new InvalidDataException("Company source list is too large.");
            output.Write(buffer, 0, count);
        }
        return Open(Encoding.UTF8.GetString(output.ToArray()), code, publicKeyPem);
    }
    private sealed record Envelope(int SchemaVersion, string Nonce, string Ciphertext, string Tag);
}
