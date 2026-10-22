using Velopack;
using Velopack.Sources;

namespace AdventureLauncher.Desktop;

internal sealed class LauncherUpdates(string repository)
{
    private readonly UpdateManager _manager = new(new GithubSource(repository, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;
    public bool IsInstalled => _manager.IsInstalled;
    public string? AvailableVersion => _pending?.TargetFullRelease.Version.ToString();
    public async Task<bool> CheckAsync()
    {
        if (!_manager.IsInstalled) return false;
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending is not null;
    }
    public async Task ApplyAsync(Action<int>? progress = null)
    {
        if (_pending is null) throw new InvalidOperationException("Check for a launcher update first.");
        await _manager.DownloadUpdatesAsync(_pending, progress);
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
