using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AdventureLauncher.Core;

public static class ManifestSigning
{
    public static PackManifest Verify(string envelopeJson, string publicKeyPem)
    {
        if (envelopeJson.Length > 24 * 1024 * 1024) throw new InvalidDataException("Manifest envelope is too large.");
        var envelope = JsonSerializer.Deserialize<SignedEnvelope>(envelopeJson, PackJson.Options)
            ?? throw new InvalidDataException("Missing manifest envelope.");
        if (envelope.KeyId != "release-v1") throw new InvalidDataException("Unknown manifest signing key.");
        var payload = Convert.FromBase64String(envelope.Payload);
        if (payload.Length > 16 * 1024 * 1024) throw new InvalidDataException("Manifest is too large.");
        using var key = ECDsa.Create();
        key.ImportFromPem(publicKeyPem);
        RequireP256(key);
        if (!key.VerifyData(payload, Convert.FromBase64String(envelope.Signature), HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence))
            throw new CryptographicException("The release signature is invalid.");
        var manifest = JsonSerializer.Deserialize<PackManifest>(payload, PackJson.Options)
            ?? throw new InvalidDataException("Missing manifest.");
        ManifestValidation.Validate(manifest);
        return manifest;
    }

    public static string Sign(PackManifest manifest, string privateKeyPem)
    {
        ManifestValidation.Validate(manifest);
        var payload = JsonSerializer.SerializeToUtf8Bytes(manifest, PackJson.Options);
        using var key = ECDsa.Create();
        key.ImportFromPem(privateKeyPem);
        RequireP256(key);
        return JsonSerializer.Serialize(new SignedEnvelope
        {
            Payload = Convert.ToBase64String(payload),
            Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256,
                DSASignatureFormat.Rfc3279DerSequence))
        }, PackJson.Options);
    }

    private static void RequireP256(ECDsa key)
    {
        if (key.KeySize != 256 || key.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new CryptographicException("Only ECDSA P-256 signing keys are supported.");
    }
}

public static class ManifestValidation
{
    private static readonly Regex Hash = new("^[a-fA-F0-9]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex Id = new("^[a-zA-Z0-9][a-zA-Z0-9._-]{0,159}$", RegexOptions.CultureInvariant);
    private static readonly Regex Device = new("^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])($|\\.)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static void Validate(PackManifest manifest)
    {
        if (manifest.SchemaVersion != 1) throw new InvalidDataException("Unsupported pack schema.");
        if (!Id.IsMatch(manifest.PackId) || string.IsNullOrWhiteSpace(manifest.Version))
            throw new InvalidDataException("Invalid pack identity.");
        if (manifest.Artifacts is null || manifest.Files is null ||
            manifest.Artifacts.Count > 10000 || manifest.Files.Count > 100000)
            throw new InvalidDataException("Invalid manifest inventory.");
        var artifacts = new Dictionary<string, PackArtifact>(StringComparer.OrdinalIgnoreCase);
        foreach (var artifact in manifest.Artifacts)
        {
            if (artifact is null || !Id.IsMatch(artifact.Id) || !artifacts.TryAdd(artifact.Id, artifact))
                throw new InvalidDataException("Duplicate or invalid artifact id.");
            if (artifact.Kind is not ("zip" or "7z" or "raw") || artifact.Size < 0 || !Hash.IsMatch(artifact.Sha256))
                throw new InvalidDataException($"Invalid artifact: {artifact.Id}");
            ValidateRelativePath(artifact.FileName);
            if (artifact.FileName.Contains('/') || artifact.FileName.Contains('\\'))
                throw new InvalidDataException("Artifact filename must be a filename only.");
            if (artifact.Urls is null) throw new InvalidDataException("Missing artifact URLs.");
            if (!string.IsNullOrWhiteSpace(artifact.ManualUrl)) ArtifactTransfer.ValidateUri(artifact.ManualUrl, false);
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file is null) throw new InvalidDataException("Missing manifest file.");
            var path = Normalize(file.Path);
            ValidateRelativePath(path);
            if (path.Equals(".arpg", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(".arpg/", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Pack cannot own launcher metadata.");
            if (!paths.Add(path)) throw new InvalidDataException($"Duplicate destination: {file.Path}");
            if (!artifacts.TryGetValue(file.ArtifactId, out var artifact))
                throw new InvalidDataException($"Missing artifact for {file.Path}");
            if (file.Size < 0 || !Hash.IsMatch(file.Sha256) || file.Policy is not ("managed" or "config" or "preserve"))
                throw new InvalidDataException($"Invalid file: {file.Path}");
            if (artifact.Kind != "raw") ValidateRelativePath(file.Entry);
            if (artifact.Kind == "raw" && (file.Size != artifact.Size ||
                !file.Sha256.Equals(artifact.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Raw artifact metadata must match the file.");
            if (file.PreserveKeys is null) throw new InvalidDataException("Missing configuration key policy.");
        }
        foreach (var path in paths)
        {
            var parent = path;
            while (parent.LastIndexOf('/') is var separator && separator >= 0)
            {
                parent = parent[..separator];
                if (paths.Contains(parent)) throw new InvalidDataException($"File/directory collision: {path}");
            }
        }
    }

    public static string Normalize(string path) => path.Replace('\\', '/');

    public static void ValidateRelativePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 1024 || path.Contains('\0') ||
            path.StartsWith('/') || path.StartsWith('\\') || Path.IsPathRooted(path))
            throw new InvalidDataException($"Unsafe relative path: {path}");
        foreach (var part in Normalize(path).Split('/'))
        {
            if (part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ') ||
                part.Any(c => c < 32 || "<>:\"|?*".Contains(c)) || Device.IsMatch(part))
                throw new InvalidDataException($"Unsafe relative path: {path}");
        }
    }
}
