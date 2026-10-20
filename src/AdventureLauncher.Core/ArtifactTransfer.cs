using System.Net;
using System.Net.Http.Headers;

namespace AdventureLauncher.Core;

internal sealed class ArtifactTransfer(HttpClient http, EngineOptions options, IProgress<PackProgress>? progress)
{
    internal async Task PreflightManualAsync(IEnumerable<PackArtifact> requiredArtifacts, string cache,
        IReadOnlyDictionary<string, string>? localArtifacts, CancellationToken ct)
    {
        foreach (var artifact in requiredArtifacts.Where(a => a.Urls.Count == 0))
        {
            ct.ThrowIfCancellationRequested();
            var cached = EngineStorage.InProfile(cache, artifact.Sha256.ToLowerInvariant() + "." + artifact.Kind);
            progress?.Report(new("verify", $"Checking availability of {artifact.FileName}", 0, artifact.Size));
            if (await EngineStorage.MatchesAsync(cached, artifact.Size, artifact.Sha256, ct)) continue;
            if (localArtifacts != null && localArtifacts.TryGetValue(artifact.Id, out var local))
            {
                await VerifyImportAsync(artifact, local, ct);
                continue;
            }
            throw new ManualArtifactRequiredException(artifact);
        }
    }

    private static async Task VerifyImportAsync(PackArtifact artifact, string local, CancellationToken ct)
    {
        try
        {
            if (await EngineStorage.MatchesAsync(local, artifact.Size, artifact.Sha256, ct)) return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ManualArtifactRequiredException(artifact, "The selected archive cannot be read. Choose the original file again.");
        }
        throw new ManualArtifactRequiredException(artifact, "The selected archive is missing or does not match this release. Choose the original file again.");
    }
    internal async Task<string> GetAsync(PackArtifact artifact, string cache,
        IReadOnlyDictionary<string, string>? localArtifacts, CancellationToken ct)
    {
        Directory.CreateDirectory(cache);
        var cacheName = artifact.Sha256.ToLowerInvariant() + "." + artifact.Kind;
        var target = EngineStorage.InProfile(cache, cacheName);
        EngineStorage.InProfile(cache, cacheName + ".lock");
        EngineStorage.InProfile(cache, cacheName + ".part");
        await using var cacheLock = new FileStream(target + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        if (await EngineStorage.MatchesAsync(target, artifact.Size, artifact.Sha256, ct)) return target;
        if (File.Exists(target)) File.Delete(target);
        var cacheDirectory = new DirectoryInfo(Path.GetFullPath(cache));
        var physical = cacheDirectory.ResolveLinkTarget(true)?.FullName ?? cacheDirectory.FullName;
        var root = Path.GetPathRoot(physical);
        var partialBytes = File.Exists(target + ".part") ? Math.Min(artifact.Size, new FileInfo(target + ".part").Length) : 0;
        if (root != null && !root.StartsWith("\\\\") && new DriveInfo(root).AvailableFreeSpace < artifact.Size - partialBytes + 16 * 1024 * 1024)
            throw new IOException($"Not enough cache space for {artifact.FileName}.");
        if (localArtifacts != null && localArtifacts.TryGetValue(artifact.Id, out var local))
        {
            progress?.Report(new("verify", $"Checking imported {artifact.FileName}", 0, artifact.Size));
            await VerifyImportAsync(artifact, local, ct);
            await CopyAsync(local, target + ".part", ct);
            File.Move(target + ".part", target, true);
            return target;
        }
        if (artifact.Urls.Count == 0) throw new ManualArtifactRequiredException(artifact);
        Exception? last = null;
        foreach (var source in artifact.Urls)
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (IsLocal(source))
                    {
                        if (!options.AllowLocalSources) throw new InvalidDataException("Local release sources are disabled.");
                        var path = Uri.TryCreate(source, UriKind.Absolute, out var localUri) && localUri.IsFile
                            ? localUri.LocalPath : source;
                        await CopyAsync(path, target + ".part", ct);
                    }
                    else
                    {
                        var uri = ValidateUri(source, options.AllowLocalSources);
                        await DownloadAsync(uri, target + ".part", artifact, ct);
                    }
                    if (!await EngineStorage.MatchesAsync(target + ".part", artifact.Size, artifact.Sha256, ct))
                    {
                        File.Delete(target + ".part");
                        throw new InvalidDataException($"Downloaded archive checksum mismatch: {artifact.FileName}");
                    }
                    File.Move(target + ".part", target, true);
                    return target;
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException ||
                    ex is TaskCanceledException && !ct.IsCancellationRequested)
                {
                    last = ex;
                    if (attempt < 2) await Task.Delay(TimeSpan.FromMilliseconds(300 * (attempt + 1)), ct);
                }
            }
            // Do not resume a failed mirror's bytes against a different URL.
            if (File.Exists(target + ".part")) File.Delete(target + ".part");
        }
        throw new IOException($"Could not download {artifact.FileName}. {last?.Message}", last);
    }

    internal static bool IsLocal(string source) =>
        Path.IsPathFullyQualified(source) || Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile;

    internal static Uri ValidateUri(string source, bool allowLocal)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !(uri.Scheme == Uri.UriSchemeHttps ||
              allowLocal && uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
            throw new InvalidDataException("Release downloads require HTTPS.");
        return uri;
    }

    private async Task DownloadAsync(Uri uri, string part, PackArtifact artifact, CancellationToken ct)
    {
        var existing = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (existing > artifact.Size) { File.Delete(part); existing = 0; }
        if (existing == artifact.Size) return;
        using var response = await DownloadResponseAsync(uri, existing, ct);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentType?.MediaType is "text/html" or "application/xhtml+xml")
            throw new IOException("The file host returned a web page instead of an archive. Check the file's download access.");
        var append = response.StatusCode == HttpStatusCode.PartialContent;
        if (append)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != existing || range.Length != artifact.Size)
            {
                if (File.Exists(part)) File.Delete(part);
                throw new IOException("The download server returned an invalid resume range.");
            }
        }
        else if (response.StatusCode != HttpStatusCode.OK)
            throw new IOException($"Unexpected download response: {response.StatusCode}");
        if (!append) existing = 0;
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = new FileStream(part, append ? FileMode.Append : FileMode.Create,
            FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[1024 * 1024];
        var written = existing;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read == 0) break;
            if (read > artifact.Size - written) throw new InvalidDataException("Archive is larger than the signed release size.");
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;
            progress?.Report(new("download", artifact.FileName, written, artifact.Size));
        }
        await output.FlushAsync(ct);
        output.Flush(true);
        if (written != artifact.Size) throw new IOException("The archive download was interrupted.");
    }

    private async Task<HttpResponseMessage> DownloadResponseAsync(Uri original, long offset, CancellationToken ct)
    {
        var next = original;
        for (var confirmation = 0; confirmation <= 2; confirmation++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
            var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType is not ("text/html" or "application/xhtml+xml"))
                return response;
            try
            {
                var page = response.RequestMessage?.RequestUri ?? next;
                if (!DriveDownloads.IsDrive(original) || !DriveDownloads.IsDrive(page) || confirmation == 2)
                    throw new IOException("The file host returned a web page instead of an archive. Check the file's download access.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                using var output = new MemoryStream();
                var buffer = new byte[16384];
                while (true)
                {
                    var read = await stream.ReadAsync(buffer, ct);
                    if (read == 0) break;
                    if (output.Length + read > 1024 * 1024) throw new IOException("Google Drive returned an unsupported download page.");
                    output.Write(buffer, 0, read);
                }
                next = DriveDownloads.Confirmation(original, page, System.Text.Encoding.UTF8.GetString(output.ToArray()))
                    ?? throw new IOException("Google Drive requires sign-in or did not provide a matching public-file download confirmation.");
            }
            finally { response.Dispose(); }
        }
        throw new IOException("Too many Google Drive download confirmations.");
    }
    private static async Task CopyAsync(string source, string target, CancellationToken ct)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None,
            1024 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, ct);
        await output.FlushAsync(ct);
        output.Flush(true);
    }
}
