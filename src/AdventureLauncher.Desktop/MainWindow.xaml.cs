using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using AdventureLauncher.Core;
using Microsoft.Win32;

namespace AdventureLauncher.Desktop;

public partial class MainWindow : Window
{
    private readonly LauncherController _controller;
    private readonly ReleaseSettings _release;
    private readonly LauncherSettings _settings;
    private readonly LauncherUpdates _updates;
    private CancellationTokenSource? _operation;
    private bool _closing;
    private bool _closeAfterOperation;
    private readonly string? _selfTestData;
    private bool _cacheExplicitlyChosen;

    public MainWindow(bool selfTest = false)
    {
        InitializeComponent();
        _release = ReleaseSettings.Load();
        _updates = new LauncherUpdates(_release.RepositoryUrl);
        _selfTestData = selfTest ? Path.Combine(Path.GetTempPath(), "AdventureLauncher-DesktopSelfTest-" + Guid.NewGuid().ToString("N")) : null;
        _controller = new LauncherController(new Progress<PackProgress>(progress => Dispatcher.InvokeAsync(() => ShowProgress(progress))), _selfTestData);
        _settings = selfTest ? new LauncherSettings
        {
            GamePath = @"D:\SteamLibrary\steamapps\common\Valheim",
            ProfilePath = @"F:\ValheimMods\Adventure RPG",
            CachePath = @"F:\ValheimMods\.arpg-cache"
        } : _controller.LoadSettings();
        // Trust and the release channel always come from the installed application, never user settings or a remote manifest.
        _settings.FeedUrl = _release.FeedUrl;
        _settings.PublicKeyPem = _release.PublicKeyPem;
        if (!selfTest)
        {
            GamePathBox.ItemsSource = _controller.DiscoverGames();
            ProfilePathBox.ItemsSource = _controller.DiscoverProfiles();
            if (string.IsNullOrWhiteSpace(_settings.GamePath)) _settings.GamePath = GamePathBox.Items.Cast<string>().FirstOrDefault() ?? "";
            if (string.IsNullOrWhiteSpace(_settings.ProfilePath)) _settings.ProfilePath = ProfilePathBox.Items.Cast<string>().FirstOrDefault() ?? "";
        }
        GamePathBox.Text = _settings.GamePath;
        ProfilePathBox.Text = _settings.ProfilePath;
        CachePathBox.Text = _settings.CachePath;
        AppVersionText.Text = "Launcher " + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "development");
        if (!selfTest) Loaded += async (_, _) => await CheckLauncherUpdateAsync();
    }

    private LauncherSettings ReadSettings()
    {
        _settings.GamePath = GamePathBox.Text.Trim();
        _settings.ProfilePath = ProfilePathBox.Text.Trim();
        _settings.CachePath = CachePathBox.Text.Trim();
        _settings.FeedUrl = _release.FeedUrl;
        _settings.PublicKeyPem = _release.PublicKeyPem;
        if (string.IsNullOrWhiteSpace(_settings.ProfilePath)) throw new InvalidOperationException("Choose a modpack storage folder first.");
        if (string.IsNullOrWhiteSpace(_settings.CachePath)) throw new InvalidOperationException("Choose a download storage folder first.");
        _controller.SaveSettings(_settings);
        return _settings;
    }

    private async Task RunAsync(string title, Func<LauncherSettings, CancellationToken, Task> action)
    {
        if (_operation is not null) return;
        _operation = new CancellationTokenSource();
        SettingsPanel.IsEnabled = ActionPanel.IsEnabled = PlayInstalledButton.IsEnabled = DiagnosticsButton.IsEnabled = LauncherUpdateButton.IsEnabled = false;
        CancelButton.Visibility = OperationProgress.Visibility = Visibility.Visible;
        OperationProgress.IsIndeterminate = true;
        StatusTitle.Text = title;
        try { await action(ReadSettings(), _operation.Token); }
        catch (OperationCanceledException)
        {
            StatusTitle.Text = "Operation cancelled";
            StatusDetail.Text = "The next update will check for any interrupted changes before continuing.";
            AppendLog("Cancelled.");
        }
        catch (Exception ex)
        {
            StatusTitle.Text = "Could not complete the operation";
            StatusDetail.Text = ex.Message;
            AppendLog(ex.Message);
            if (!_closing) MessageBox.Show(this, ex.Message + "\n\nUse Collect diagnostics if you need help.", "Adventure RPG", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SettingsPanel.IsEnabled = ActionPanel.IsEnabled = PlayInstalledButton.IsEnabled = DiagnosticsButton.IsEnabled = LauncherUpdateButton.IsEnabled = true;
            CancelButton.Visibility = OperationProgress.Visibility = Visibility.Collapsed;
            if (_closeAfterOperation) Close();
        }
    }

    private async Task UpdateAsync(LauncherSettings settings, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                var result = await _controller.UpdateAsync(settings, ct);
                StatusTitle.Text = "Collection is ready";
                StatusDetail.Text = $"Version {result.Version}. {result.WrittenFiles} files installed or updated.";
                return;
            }
            catch (ManualArtifactRequiredException ex)
            {
                AppendLog(ex.Message);
                var selected = ChooseManualArchive(ex.Artifact, ex.Message);
                if (selected is null) throw new OperationCanceledException(ct);
                settings.ImportedArtifacts[ex.Artifact.Id] = selected;
                _controller.SaveSettings(settings);
            }
        }
    }

    private async void Update_Click(object sender, RoutedEventArgs e) => await RunAsync("Updating collection", UpdateAsync);
    private async void UpdatePlay_Click(object sender, RoutedEventArgs e) => await RunAsync("Preparing your collection", async (settings, ct) =>
    {
        await UpdateAsync(settings, ct);
        ct.ThrowIfCancellationRequested();
        _controller.Launch(settings);
        StatusTitle.Text = "Valheim is starting";
        StatusDetail.Text = "The first modded start may take several minutes. Steam must be signed in.";
    });
    private async void Verify_Click(object sender, RoutedEventArgs e) => await RunAsync("Verifying collection", async (settings, ct) =>
    {
        var plan = await _controller.CheckAsync(settings, true, ct);
        if (plan.IsCurrent)
        {
            StatusTitle.Text = "All managed files verified";
            StatusDetail.Text = $"Version {plan.Version} is ready.";
            return;
        }
        AppendLog($"Repair needed: {plan.ChangedFiles.Count} files; {plan.RemovedFiles.Count} managed files removed by this release.");
        await UpdateAsync(settings, ct);
    });
    private async void Rollback_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Restore the files saved before the last modpack update?", "Restore previous version", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunAsync("Restoring previous version", async (settings, ct) =>
        {
            var result = await _controller.RollbackAsync(settings, ct);
            StatusTitle.Text = "Previous version restored";
            StatusDetail.Text = $"Restored version {result.Version}.";
        });
    }
    private async void Play_Click(object sender, RoutedEventArgs e) => await RunAsync("Starting installed collection", (settings, ct) =>
    {
        ct.ThrowIfCancellationRequested();
        _controller.Launch(settings);
        StatusTitle.Text = "Valheim is starting";
        StatusDetail.Text = "The installed files were used without checking for a new release.";
        return Task.CompletedTask;
    });
    private async void Diagnostics_Click(object sender, RoutedEventArgs e) => await RunAsync("Collecting diagnostics", async (settings, ct) =>
    {
        var archive = await Task.Run(() => _controller.CollectDiagnostics(settings), ct);
        StatusTitle.Text = "Diagnostics are ready";
        StatusDetail.Text = archive;
        Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = "/select,\"" + archive + "\"", UseShellExecute = true });
    });
    private void Cancel_Click(object sender, RoutedEventArgs e) => _operation?.Cancel();

    private void ShowProgress(PackProgress progress)
    {
        if (_closing) return;
        StatusDetail.Text = progress.Message;
        OperationProgress.IsIndeterminate = progress.Total <= 0;
        if (progress.Total > 0) OperationProgress.Value = Math.Clamp((double)progress.Completed * 100 / progress.Total, 0, 100);
        AppendLog(progress.Message);
    }
    private void AppendLog(string text)
    {
        ActivityLog.AppendText(text + Environment.NewLine);
        if (ActivityLog.Text.Length > 20_000) ActivityLog.Text = ActivityLog.Text[^16_000..];
        ActivityLog.ScrollToEnd();
    }

    private void BrowseGame_Click(object sender, RoutedEventArgs e) { var path = PickFolder("Select the folder containing valheim.exe", GamePathBox.Text); if (path is not null) GamePathBox.Text = path; }
    private void BrowseProfile_Click(object sender, RoutedEventArgs e)
    {
        var path = PickFolder("Select an existing modpack folder, or choose a folder for a new installation", ProfilePathBox.Text);
        if (path is null) return;
        if (string.Equals(Path.GetPathRoot(path)?.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            path = Path.Combine(path, "ValheimMods", "Adventure RPG");
        ProfilePathBox.Text = path;
        if (!_cacheExplicitlyChosen) CachePathBox.Text = Path.Combine(Path.GetDirectoryName(path) ?? path, ".arpg-cache");
    }
    private void BrowseCache_Click(object sender, RoutedEventArgs e) { var path = PickFolder("Select download storage", CachePathBox.Text); if (path is not null) { CachePathBox.Text = path; _cacheExplicitlyChosen = true; } }
    private static string? PickFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (Directory.Exists(current)) dialog.InitialDirectory = current;
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
    private void OpenProfile_Click(object sender, RoutedEventArgs e)
    {
        var path = ProfilePathBox.Text.Trim();
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
        else MessageBox.Show(this, "The modpack folder has not been created yet.", "Adventure RPG");
    }

    private string? ChooseManualArchive(PackArtifact artifact, string reason)
    {
        var dialog = new Window { Owner = this, Title = "Import original archive", Width = 570, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var stack = new StackPanel { Margin = new Thickness(24) };
        stack.Children.Add(new TextBlock { Text = artifact.FileName, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(new TextBlock { Text = reason, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
        stack.Children.Add(new TextBlock { Text = artifact.Instructions ?? "Choose the original archive downloaded from the mod author's page. Its checksum will be checked before installation.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 18) });
        var buttons = new WrapPanel();
        string? selected = null;
        if (Uri.TryCreate(artifact.ManualUrl, UriKind.Absolute, out var url) && url.Scheme == Uri.UriSchemeHttps)
        {
            var website = new Button { Content = "Open download page", Margin = new Thickness(0, 0, 8, 8) };
            website.Click += (_, _) => Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            buttons.Children.Add(website);
        }
        var import = new Button { Content = "Choose archive…", Margin = new Thickness(0, 0, 8, 8) };
        import.Click += (_, _) =>
        {
            var picker = new OpenFileDialog { Title = "Choose " + artifact.FileName, Filter = "Mod archives|*.zip;*.7z|All files|*.*", CheckFileExists = true };
            if (picker.ShowDialog(dialog) == true) { selected = picker.FileName; dialog.DialogResult = true; }
        };
        buttons.Children.Add(import);
        var cancel = new Button { Content = "Cancel", Margin = new Thickness(0, 0, 0, 8), IsCancel = true };
        buttons.Children.Add(cancel);
        stack.Children.Add(buttons);
        dialog.Content = stack;
        dialog.ShowDialog();
        return selected;
    }

    private async Task CheckLauncherUpdateAsync()
    {
        try { if (await _updates.CheckAsync()) LauncherUpdateButton.Visibility = Visibility.Visible; }
        catch (Exception ex) { AppendLog("Launcher update check unavailable: " + ex.Message); }
    }
    private async void LauncherUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_operation is not null) return;
        LauncherUpdateButton.IsEnabled = false;
        try { await _updates.ApplyAsync(); }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Launcher update", MessageBoxButton.OK, MessageBoxImage.Warning); LauncherUpdateButton.IsEnabled = true; }
    }
    protected override void OnClosed(EventArgs e)
    {
        _controller.Dispose();
        if (_selfTestData is not null && Path.GetFileName(_selfTestData).StartsWith("AdventureLauncher-DesktopSelfTest-", StringComparison.Ordinal) && Path.GetDirectoryName(_selfTestData) == Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar))
        {
            try { if (Directory.Exists(_selfTestData)) Directory.Delete(_selfTestData, true); } catch { }
        }
        base.OnClosed(e);
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        if (_operation is not null)
        {
            e.Cancel = true;
            _closeAfterOperation = true;
            StatusTitle.Text = "Cancelling before closing";
            StatusDetail.Text = "Finishing any interrupted changes safely…";
            _operation.Cancel();
        }
        base.OnClosing(e);
    }
}
