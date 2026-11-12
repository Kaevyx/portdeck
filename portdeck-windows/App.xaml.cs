using System.Windows;

namespace PortDeck.Windows;

public partial class App : System.Windows.Application
{
    private TrayController? _trayController;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var settings = WindowsSettingsStore.Load();
        ThemeManager.Apply(settings.Theme);
        _trayController = new TrayController(new PortdeckClient(), settings);
        if (e.Args.Contains("--show", StringComparer.OrdinalIgnoreCase))
        {
            _trayController.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayController?.Dispose();
        base.OnExit(e);
    }
}
