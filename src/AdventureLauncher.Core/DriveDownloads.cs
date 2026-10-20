using System.Net;
using System.Text.RegularExpressions;

namespace AdventureLauncher.Core;

// This parses only Google's raw-file download confirmation form, never arbitrary page actions.
internal static class DriveDownloads
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    { "id", "export", "confirm", "uuid", "at", "authuser", "resourcekey" };
    private static readonly Regex Form = new(@"<form\b(?<attributes>[^>]*)>(?<body>.*?)</form\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Input = new(@"<input\b(?<attributes>[^>]*)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline, TimeSpan.FromMilliseconds(200));
    private static readonly Regex Attribute = new("""(?<key>[\w-]+)\s*=\s*(?:"(?<value>[^"]*)"|'(?<value>[^']*)'|(?<value>[^\s>]+))""",
        RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(200));
    private static readonly Regex FileId = new("^[A-Za-z0-9_-]{10,200}$", RegexOptions.CultureInvariant);

    internal static bool IsDrive(Uri uri) => uri.Scheme == Uri.UriSchemeHttps && uri.IsDefaultPort &&
        string.IsNullOrEmpty(uri.UserInfo) &&
        (uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase));

    internal static Uri? Confirmation(Uri original, Uri page, string html)
    {
        if (!IsDrive(original) || !IsDrive(page) || html.Length > 1024 * 1024) return null;
        var expectedId = GetId(original);
        if (expectedId == null) return null;
        try
        {
            foreach (Match form in Form.Matches(html))
            {
                var attributes = Attributes(form.Groups["attributes"].Value);
                if (!attributes.TryGetValue("id", out var formId) || formId != "download-form") continue;
                if (attributes.TryGetValue("method", out var method) && !method.Equals("get", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!attributes.TryGetValue("action", out var action) ||
                    !Uri.TryCreate(page, action, out var target) || !IsDrive(target)) continue;
                if (!(target.Host.Equals("drive.usercontent.google.com", StringComparison.OrdinalIgnoreCase) && target.AbsolutePath == "/download") &&
                    !(target.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase) && target.AbsolutePath == "/uc"))
                    continue;
                var values = Query(target);
                foreach (Match input in Input.Matches(form.Groups["body"].Value))
                {
                    var tags = Attributes(input.Groups["attributes"].Value);
                    if (!tags.TryGetValue("type", out var type) || !type.Equals("hidden", StringComparison.OrdinalIgnoreCase) ||
                        !tags.TryGetValue("name", out var name) || !Fields.Contains(name) ||
                        !tags.TryGetValue("value", out var value) || value.Length > 4096) continue;
                    if (values.TryGetValue(name, out var existing) && existing != value) return null;
                    values[name] = value;
                }
                if (!values.TryGetValue("id", out var id) || id != expectedId ||
                    !values.TryGetValue("confirm", out var confirm) || string.IsNullOrEmpty(confirm)) continue;
                var builder = new UriBuilder(target)
                {
                    Query = string.Join("&", values.Where(x => Fields.Contains(x.Key))
                        .Select(x => Uri.EscapeDataString(x.Key) + "=" + Uri.EscapeDataString(x.Value))),
                    Fragment = ""
                };
                return builder.Uri.AbsoluteUri.Length <= 16384 ? builder.Uri : null;
            }
        }
        catch (RegexMatchTimeoutException) { return null; }
        return null;
    }

    private static string? GetId(Uri uri)
    {
        var query = Query(uri);
        if (query.TryGetValue("id", out var id) && FileId.IsMatch(id)) return id;
        var match = Regex.Match(uri.AbsolutePath, "^/file/d/([A-Za-z0-9_-]{10,200})(?:/|$)", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Dictionary<string,string> Query(Uri uri)
    {
        var values = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.IndexOf('=');
            if (split < 0) continue;
            var key = WebUtility.UrlDecode(part[..split]);
            var value = WebUtility.UrlDecode(part[(split + 1)..]);
            if (Fields.Contains(key)) values[key] = value;
        }
        return values;
    }

    private static Dictionary<string,string> Attributes(string text)
    {
        var result = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Attribute.Matches(text))
            result[match.Groups["key"].Value] = WebUtility.HtmlDecode(match.Groups["value"].Value);
        return result;
    }
}
