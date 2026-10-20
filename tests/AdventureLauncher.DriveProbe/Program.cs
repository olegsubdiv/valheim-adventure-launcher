using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using AdventureLauncher.Core;

var argsMap = ParseArgs(args);
var url = argsMap["url"];
var size = long.Parse(argsMap["size"], System.Globalization.CultureInfo.InvariantCulture);
var sha256 = argsMap["sha256"];
var reportPath = Path.GetFullPath(argsMap["output"]);
if (size <= 0 || sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
    throw new ArgumentException("Expected a positive size and 64-character SHA-256.");
var source = new Uri(url, UriKind.Absolute);
if (source.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(source.UserInfo))
    throw new ArgumentException("The probe accepts HTTPS URLs without embedded credentials.");

var launcherRoot = FindLauncherRoot();
var fixtureRoot = Path.Combine(launcherRoot, "artifacts", "drive-probe");
var cache = Path.Combine(fixtureRoot, "cache");
var profile = Path.Combine(fixtureRoot, "profile");
Directory.CreateDirectory(fixtureRoot);
Directory.CreateDirectory(cache);
Directory.CreateDirectory(profile);

var artifact = new PackArtifact
{
    Id = "synthetic-drive-archive",
    Kind = "raw",
    FileName = "synthetic-drive-test.zip",
    Size = size,
    Sha256 = sha256,
    Urls = [url]
};
var manifest = new PackManifest
{
    PackId = "drive-resume-probe",
    Title = "Synthetic Drive resume probe",
    Version = "1",
    Artifacts = [artifact],
    Files =
    [
        new PackFile
        {
            Path = "probe.zip",
            ArtifactId = artifact.Id,
            Entry = "",
            Size = size,
            Sha256 = sha256,
            Policy = "managed"
        }
    ]
};

var exchangeLog = new SafeExchangeLog();
using var cookies = new HttpClientHandler { UseCookies = true, CookieContainer = new CookieContainer() };
using var logging = new SafeLoggingHandler(exchangeLog) { InnerHandler = cookies };
using var http = new HttpClient(logging);
var firstProgress = new CancelAtProgress(1L * 1024 * 1024);
var firstError = await ApplyUntilCancelledAsync(http, manifest, profile, cache, firstProgress);
var cachePart = Path.Combine(cache, sha256.ToLowerInvariant() + ".raw.part");
var firstPartBytes = File.Exists(cachePart) ? new FileInfo(cachePart).Length : 0;
var firstTransactionExists = File.Exists(Path.Combine(profile, ".arpg", "transaction.json"));

var secondProgress = new CancelAtProgress(2L * 1024 * 1024);
var secondError = await ApplyUntilCancelledAsync(http, manifest, profile, cache, secondProgress);
var secondPartBytes = File.Exists(cachePart) ? new FileInfo(cachePart).Length : 0;
var zipPrefix = ReadZipPrefix(cachePart);
var entries = exchangeLog.Snapshot();
var rangeRequest = entries.LastOrDefault(e => e.Range != null);
var rangeResponse = rangeRequest;
var report = new
{
    result = "completed",
    utc = DateTimeOffset.UtcNow,
    requestedSize = size,
    requestedSha256 = sha256.ToLowerInvariant(),
    sourceHost = source.Host,
    fixtureRoot,
    firstCancel = new { threshold = firstProgress.Threshold, completed = firstProgress.MaxCompleted, errorType = firstError },
    firstPartBytes,
    firstTransactionExists,
    secondCancel = new { threshold = secondProgress.Threshold, completed = secondProgress.MaxCompleted, errorType = secondError },
    secondPartBytes,
    zipPrefixHex = Convert.ToHexString(zipPrefix),
    rangeRequest = rangeRequest == null ? null : new { rangeRequest.RequestHost, rangeRequest.Range },
    rangeResponse = rangeResponse == null ? null : new { rangeResponse.ResponseStatus, rangeResponse.ResponseHost, rangeResponse.ContentRangeFrom, rangeResponse.ContentRangeTo, rangeResponse.ContentRangeLength },
    httpExchanges = entries,
    profileFiles = Directory.EnumerateFiles(profile, "*", SearchOption.AllDirectories)
        .Select(p => Path.GetRelativePath(profile, p).Replace('\\', '/')).ToArray(),
    profileTransactionExists = File.Exists(Path.Combine(profile, ".arpg", "transaction.json")),
    archiveSha256VerifiedLocally = false,
    limitations = "Only the first ~2 MiB were read by the anonymous Core download. Full-file integrity is not established by this probe; publisher separately performs full readback SHA-256."
};

Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));

if (firstError != nameof(OperationCanceledException) || secondError != nameof(OperationCanceledException))
    throw new InvalidOperationException("A Core apply pass did not stop at its expected cancellation threshold.");
if (firstPartBytes < 1_048_576 || firstPartBytes > 1_200_000 || secondPartBytes < 2_097_152 || secondPartBytes > 2_300_000)
    throw new InvalidOperationException("Partial cache size was outside the expected range.");
if (zipPrefix.Length < 2 || zipPrefix[0] != 0x50 || zipPrefix[1] != 0x4B)
    throw new InvalidOperationException("Partial cache does not begin with a ZIP signature.");
if (firstTransactionExists || File.Exists(Path.Combine(profile, ".arpg", "transaction.json")))
    throw new InvalidOperationException("Core created an update transaction during the canceled transfer probe.");
if (rangeRequest?.Range != $"bytes={firstPartBytes}-")
    throw new InvalidOperationException("The second Core pass did not request the expected resume offset.");
if (rangeResponse?.ResponseStatus != (int)HttpStatusCode.PartialContent || rangeResponse.ContentRangeLength != size)
    throw new InvalidOperationException("The resumed response was not a 206 with the expected total length.");

static Dictionary<string, string> ParseArgs(string[] values)
{
    var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < values.Length; i += 2)
    {
        if (i + 1 >= values.Length || !values[i].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException("Use --url URL --size BYTES --sha256 HASH --output REPORT.");
        parsed[values[i][2..]] = values[i + 1];
    }
    foreach (var required in new[] { "url", "size", "sha256", "output" })
        if (!parsed.ContainsKey(required)) throw new ArgumentException($"Missing --{required}.");
    return parsed;
}

static string FindLauncherRoot()
{
    for (var current = new DirectoryInfo(Environment.CurrentDirectory); current != null; current = current.Parent)
        if (File.Exists(Path.Combine(current.FullName, "src", "AdventureLauncher.Core", "AdventureLauncher.Core.csproj")))
            return current.FullName;
    throw new DirectoryNotFoundException("Could not locate launcher repository root from the current directory.");
}

static async Task<string?> ApplyUntilCancelledAsync(HttpClient http, PackManifest manifest, string profile, string cache,
    CancelAtProgress progress)
{
    using var cancel = new CancellationTokenSource();
    progress.OnThreshold = cancel.Cancel;
    var engine = new PackEngine(http, progress: progress);
    try
    {
        await engine.ApplyAsync(manifest, profile, cache, ct: cancel.Token);
        return null;
    }
    catch (OperationCanceledException) when (cancel.IsCancellationRequested)
    {
        return nameof(OperationCanceledException);
    }
    catch (Exception ex)
    {
        return ex.GetType().Name;
    }
}

static byte[] ReadZipPrefix(string path)
{
    if (!File.Exists(path)) return [];
    using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    var bytes = new byte[4];
    var read = input.Read(bytes, 0, bytes.Length);
    return bytes[..read];
}

sealed class CancelAtProgress(long threshold) : IProgress<PackProgress>
{
    public long Threshold { get; } = threshold;
    public long MaxCompleted { get; private set; }
    public Action? OnThreshold { get; set; }
    private bool _cancelled;

    public void Report(PackProgress value)
    {
        if (value.Phase != "download") return;
        MaxCompleted = Math.Max(MaxCompleted, value.Completed);
        if (!_cancelled && value.Completed >= Threshold)
        {
            _cancelled = true;
            OnThreshold?.Invoke();
        }
    }
}

sealed record SafeExchange(string RequestHost, string? Range, int ResponseStatus, string? ResponseHost,
    long? ContentRangeFrom, long? ContentRangeTo, long? ContentRangeLength);

sealed class SafeExchangeLog
{
    private readonly List<SafeExchange> _entries = [];
    public void Add(SafeExchange item) { lock (_entries) _entries.Add(item); }
    public SafeExchange[] Snapshot() { lock (_entries) return [.. _entries]; }
}

sealed class SafeLoggingHandler(SafeExchangeLog log) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestHost = request.RequestUri?.Host ?? "unknown";
        var range = request.Headers.Range?.ToString();
        var response = await base.SendAsync(request, cancellationToken);
        var contentRange = response.Content.Headers.ContentRange;
        var responseHost = response.RequestMessage?.RequestUri?.Host;
        log.Add(new SafeExchange(requestHost, range, (int)response.StatusCode, responseHost,
            contentRange?.From, contentRange?.To, contentRange?.Length));
        return response;
    }
}
