using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Media = System.Windows.Media;

namespace PortDeck.Windows;

public partial class DashboardWindow : Window
{
    private readonly PortdeckClient _client;
    private readonly ObservableCollection<ServiceRow> _rows = [];
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly DispatcherTimer _timer;
    private readonly WindowsSettings _settings;
    private readonly Action _saveSettings;
    private readonly WindowsUpdateChecker _updateChecker = new();
    private bool _updatingSettingsControls;
    private bool _hasCheckedForUpdates;
    private bool _updateCheckInProgress;
    private string? _buildCommit;
    private Uri _latestSourceUri = new("https://github.com/Kaevyx/portdeck");

    internal bool AllowClose { get; set; }

    internal DashboardWindow(PortdeckClient client, WindowsSettings settings, Action saveSettings)
    {
        _settings = settings;
        _saveSettings = saveSettings;
        InitializeComponent();
        _client = client;
        ServiceList.ItemsSource = _rows;
        UpdateBuildInformation();
        UpdateSettingsControls();
        Deactivated += (_, _) =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (IsVisible && !IsActive && !_settings.KeepFlyoutOpen) Hide();
            });
        };
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _timer.Tick += async (_, _) =>
        {
            if (IsVisible) await RefreshAsync();
        };
        _timer.Start();
    }

    internal async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0)) return;
        RefreshButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        LastUpdatedText.Text = "Refreshing local services…";
        try
        {
            var status = await _client.GetStatusAsync();
            var nextRows = Flatten(status).ToList();
            _rows.Clear();
            foreach (var row in nextRows) _rows.Add(row);
            EmptyState.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ServiceScrollViewer.Visibility = _rows.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
            var activeCount = _rows.Count(row => row.Service.Status == "running");
            ServiceCountText.Text = activeCount == 1 ? "1 active local service" : $"{activeCount} active local services";
            ProjectCountText.Text = FormatScopeSummary(status);
            LastUpdatedText.Text = $"Updated {status.GeneratedAt.ToLocalTime():t}";
            FooterStatusDot.Fill = (Media.Brush)FindResource(status.Warnings.Count == 0 ? "Healthy" : "Warning");
            ShowMessage(status.Warnings.Count > 0 ? string.Join(Environment.NewLine, status.Warnings) : null, false);
        }
        catch (Exception error)
        {
            ServiceCountText.Text = "Discovery unavailable";
            ProjectCountText.Text = "The last successful service list is still shown";
            LastUpdatedText.Text = "Refresh failed";
            FooterStatusDot.Fill = (Media.Brush)FindResource("Danger");
            ShowMessage(error.Message, false);
        }
        finally
        {
            LoadingBar.Visibility = Visibility.Collapsed;
            RefreshButton.IsEnabled = true;
            _refreshGate.Release();
        }
    }

    private static string FormatScopeSummary(PortdeckStatus status)
    {
        var projectCount = status.Groups.Count;
        var projectText = projectCount switch
        {
            0 => "No attributed projects",
            1 => "1 attributed project",
            _ => $"{projectCount} attributed projects"
        };
        return status.Unknown.Count == 0
            ? projectText
            : $"{projectText}  •  {status.Unknown.Count} unattributed";
    }

    private static IEnumerable<ServiceRow> Flatten(PortdeckStatus status)
    {
        foreach (var project in status.Groups)
        {
            foreach (var worktree in project.Worktrees)
            {
                foreach (var service in worktree.Services)
                {
                    yield return new ServiceRow(
                        project.ProjectName,
                        worktree.Name,
                        service,
                        service.Cwd ?? worktree.Path ?? project.RepoRoot ?? "");
                }
            }
        }

        foreach (var service in status.Unknown)
        {
            yield return new ServiceRow("Unknown services", "", service, service.Cwd ?? "");
        }
    }

    private void ShowMessage(string? message, bool success)
    {
        MessageText.Text = message ?? "";
        MessageBorder.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrWhiteSpace(message)) return;

        MessageGlyph.Text = success ? "✓" : "!";
        MessageGlyph.Foreground = (Media.Brush)FindResource(success ? "Healthy" : "Warning");
        MessageText.Foreground = (Media.Brush)FindResource("PrimaryText");
        MessageBorder.Background = (Media.Brush)FindResource("SurfaceBackground");
        MessageBorder.BorderBrush = (Media.Brush)FindResource(success ? "Healthy" : "Warning");
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void CloseClick(object sender, RoutedEventArgs e) => Hide();

    private void PinClick(object sender, RoutedEventArgs e)
    {
        _settings.KeepFlyoutOpen = !_settings.KeepFlyoutOpen;
        SaveAndRefreshSettings();
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        UpdateSettingsControls();
        SettingsPanel.Visibility = Visibility.Visible;
        if (!_hasCheckedForUpdates) _ = CheckForUpdatesAsync();
    }

    private void CloseSettingsClick(object sender, RoutedEventArgs e) => SettingsPanel.Visibility = Visibility.Collapsed;

    private void DarkThemeClick(object sender, RoutedEventArgs e) => SelectTheme(PortDeckTheme.Dark);

    private void LightThemeClick(object sender, RoutedEventArgs e) => SelectTheme(PortDeckTheme.Light);

    private void SystemThemeClick(object sender, RoutedEventArgs e) => SelectTheme(PortDeckTheme.System);

    private void SelectTheme(PortDeckTheme theme)
    {
        _settings.Theme = theme;
        SaveAndRefreshSettings();
    }

    private void KeepOpenToggleChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingSettingsControls) return;
        _settings.KeepFlyoutOpen = KeepOpenToggle.IsChecked == true;
        SaveAndRefreshSettings();
    }

    private void SaveAndRefreshSettings()
    {
        _saveSettings();
        UpdateSettingsControls();
    }

    private void UpdateSettingsControls()
    {
        _updatingSettingsControls = true;
        try
        {
            KeepOpenToggle.IsChecked = _settings.KeepFlyoutOpen;
            SetThemeButtonState(DarkThemeButton, _settings.Theme == PortDeckTheme.Dark);
            SetThemeButtonState(LightThemeButton, _settings.Theme == PortDeckTheme.Light);
            SetThemeButtonState(SystemThemeButton, _settings.Theme == PortDeckTheme.System);
            PinButton.Foreground = (Media.Brush)FindResource(_settings.KeepFlyoutOpen ? "AccentBright" : "PrimaryText");
            PinButton.BorderBrush = (Media.Brush)FindResource(_settings.KeepFlyoutOpen ? "Accent" : "HairlineBorder");
            PinButton.Content = _settings.KeepFlyoutOpen ? "Pinned" : "Pin";
            PinButton.ToolTip = _settings.KeepFlyoutOpen ? "Unpin flyout" : "Keep flyout open";
        }
        finally
        {
            _updatingSettingsControls = false;
        }
    }

    private void SetThemeButtonState(System.Windows.Controls.Button button, bool selected)
    {
        button.BorderBrush = (Media.Brush)FindResource(selected ? "Accent" : "HairlineBorder");
        button.BorderThickness = selected ? new Thickness(2) : new Thickness(1);
        button.Background = (Media.Brush)FindResource(selected ? "ActionBackground" : "SurfaceBackground");
    }

    private void UpdateBuildInformation()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString()
                      ?? "unknown";
        VersionText.Text = $"PortDeck for Windows {version}";

        _buildCommit = Environment.GetEnvironmentVariable("PORTDECK_BUILD_COMMIT")?.Trim();
        BuildCommitText.Text = string.IsNullOrWhiteSpace(_buildCommit)
            ? "Source commit unavailable. Use git fetch origin and git rev-parse HEAD to compare this checkout."
            : $"Source commit {ShortCommit(_buildCommit)}";
    }

    private async void CheckForUpdatesClick(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync();

    private async Task CheckForUpdatesAsync()
    {
        if (_updateCheckInProgress) return;
        _updateCheckInProgress = true;
        CheckForUpdatesButton.IsEnabled = false;
        CheckForUpdatesButton.Content = "Checking…";
        UpdateStatusHeadline.Foreground = (Media.Brush)FindResource("PrimaryText");
        UpdateStatusHeadline.Text = "Checking GitHub";
        UpdateStatusText.Text = "Checking the latest source commit on GitHub…";
        UpdateCheckedText.Text = "";

        try
        {
            var result = await _updateChecker.CheckAsync(_buildCommit);
            _latestSourceUri = result.LatestSourceUri;

            switch (result.State)
            {
                case WindowsUpdateState.Current:
                    SetUpdateStatus(
                        "Up to date",
                        $"This build matches GitHub main ({ShortCommit(result.LatestCommit)}).",
                        "Healthy");
                    break;
                case WindowsUpdateState.UpdateAvailable:
                    SetUpdateStatus(
                        "Update available",
                        $"GitHub main is {FormatCommitCount(result.MainAheadBy)} ahead. Pull and rebuild to update this preview.",
                        "Warning");
                    break;
                case WindowsUpdateState.LocalAhead:
                    SetUpdateStatus(
                        "Local build is ahead",
                        $"This build is {FormatCommitCount(result.LocalAheadBy)} ahead of GitHub main. Nothing needs downloading.",
                        "AccentBright");
                    break;
                case WindowsUpdateState.Diverged:
                    SetUpdateStatus(
                        "Source has diverged",
                        $"GitHub main is {FormatCommitCount(result.MainAheadBy)} ahead and this build has {FormatCommitCount(result.LocalAheadBy)} not on main.",
                        "Warning");
                    break;
                case WindowsUpdateState.Different:
                    SetUpdateStatus(
                        "Different source build",
                        $"GitHub main is {ShortCommit(result.LatestCommit)}, but its relationship to this build could not be confirmed.",
                        "Warning");
                    break;
                default:
                    SetUpdateStatus(
                        "Comparison unavailable",
                        $"Latest source is {ShortCommit(result.LatestCommit)}, but this build has no source commit to compare.",
                        "SecondaryText");
                    break;
            }
        }
        catch (Exception)
        {
            SetUpdateStatus(
                "Unable to check",
                "GitHub could not be reached. Check your connection and try again.",
                "Warning");
        }
        finally
        {
            _hasCheckedForUpdates = true;
            _updateCheckInProgress = false;
            CheckForUpdatesButton.Content = "Check for updates";
            CheckForUpdatesButton.IsEnabled = true;
            UpdateCheckedText.Text = $"Checked {DateTime.Now:t}";
        }
    }

    private void SetUpdateStatus(string headline, string detail, string colorResource)
    {
        UpdateStatusHeadline.Text = headline;
        UpdateStatusHeadline.Foreground = (Media.Brush)FindResource(colorResource);
        UpdateStatusText.Text = detail;
    }

    private static string FormatCommitCount(int count) =>
        count == 1 ? "1 commit" : $"{Math.Max(count, 0)} commits";

    private void ViewLatestSourceClick(object sender, RoutedEventArgs e) => OpenShell(_latestSourceUri.AbsoluteUri);

    private static string ShortCommit(string commit) => commit[..Math.Min(12, commit.Length)];

    private void WindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        if (SettingsPanel.Visibility == Visibility.Visible) SettingsPanel.Visibility = Visibility.Collapsed;
        else Hide();
    }

    private void OpenClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is ServiceRow { Service.Url: { } url }) OpenShell(url);
    }

    private void FolderClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is ServiceRow { Folder.Length: > 0 } row) OpenShell(row.Folder);
    }

    private async void StopClick(object sender, RoutedEventArgs e)
    {
        if ((sender as System.Windows.Controls.Button)?.Tag is not ServiceRow row) return;
        var answer = System.Windows.MessageBox.Show(
            this,
            $"Stop {row.Name}{(row.Service.Port is int port ? $" on :{port}" : "")}?",
            "Confirm stop",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        var result = await _client.StopAsync(row.Service.Id);
        await RefreshAsync();
        ShowMessage(result.Message, result.Ok);
    }

    private static void OpenShell(string target)
    {
        Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }
        base.OnClosing(e);
    }
}
