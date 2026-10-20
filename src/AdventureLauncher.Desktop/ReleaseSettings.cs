using System.IO;
using System.Text.Json;

namespace AdventureLauncher.Desktop;

internal sealed class ReleaseSettings
{
    public string FeedUrl { get; set; } = "";
    public string CompanySourcesUrl { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
    public string RepositoryUrl { get; set; } = "";

    public static ReleaseSettings Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "release-settings.json");
        var settings = JsonSerializer.Deserialize<ReleaseSettings>(File.ReadAllText(path), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Bundled release settings are missing.");
        if (!Uri.TryCreate(settings.FeedUrl, UriKind.Absolute, out var feed) || feed.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The bundled release feed must use HTTPS.");
        if (!Uri.TryCreate(settings.RepositoryUrl, UriKind.Absolute, out var repo) || repo.Host != "github.com" || repo.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The bundled launcher repository must be a public GitHub HTTPS URL.");
        if (!Uri.TryCreate(settings.CompanySourcesUrl, UriKind.Absolute, out var company) || company.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The bundled company source channel must use HTTPS.");
        using var key = System.Security.Cryptography.ECDsa.Create();
        key.ImportFromPem(settings.PublicKeyPem);
        if (key.KeySize != 256) throw new InvalidDataException("The bundled release key must be P-256.");
        return settings;
    }
}
