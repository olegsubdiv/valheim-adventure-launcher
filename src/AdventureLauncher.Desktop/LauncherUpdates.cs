using Velopack;
using Velopack.Sources;

namespace AdventureLauncher.Desktop;

internal sealed class LauncherUpdates(string repository)
{
    private readonly UpdateManager _manager = new(new GithubSource(repository, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;
    public async Task<bool> CheckAsync()
    {
        if (!_manager.IsInstalled) return false;
        _pending = await _manager.CheckForUpdatesAsync();
        return _pending is not null;
    }
    public async Task ApplyAsync()
    {
        if (_pending is null) return;
        await _manager.DownloadUpdatesAsync(_pending);
        _manager.ApplyUpdatesAndRestart(_pending);
    }
}
