using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms = System.Windows.Forms;
using Microsoft.Win32;

namespace PortDeck.Windows;

internal sealed class TrayController : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly DashboardWindow _window;
    private readonly Icon _trayIcon;
    private readonly WindowsSettings _settings;

    public TrayController(PortdeckClient client, WindowsSettings settings)
    {
        _settings = settings;
        _window = new DashboardWindow(client, settings, SaveSettings);
        _trayIcon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _trayIcon,
            Text = "PortDeck",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _notifyIcon.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ToggleWindow();
        };
        SystemEvents.DisplaySettingsChanged += SystemDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged += SystemUserPreferenceChanged;
    }

    private Forms.ContextMenuStrip BuildMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open PortDeck", null, (_, _) => Show());
        menu.Items.Add("Refresh", null, async (_, _) =>
        {
            Show();
            await _window.RefreshAsync();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => System.Windows.Application.Current.Shutdown());
        return menu;
    }

    private void ToggleWindow()
    {
        if (_window.IsVisible) _window.Hide();
        else Show();
    }

    internal void Show()
    {
        _window.Show();
        PositionWindow();
        _window.Activate();
        _ = _window.RefreshAsync();
    }

    private void PositionWindow()
    {
        var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        var area = screen.WorkingArea;
        var bounds = screen.Bounds;
        var handle = new WindowInteropHelper(_window).Handle;
        var dpi = handle == IntPtr.Zero ? 96u : GetDpiForWindow(handle);
        var scale = Math.Max(1d, dpi / 96d);
        const double gap = 8;

        var areaLeft = area.Left / scale;
        var areaTop = area.Top / scale;
        var areaRight = area.Right / scale;
        var areaBottom = area.Bottom / scale;
        var taskbarAtTop = area.Top > bounds.Top;
        var taskbarAtLeft = area.Left > bounds.Left;
        var taskbarAtRight = area.Right < bounds.Right;

        if (taskbarAtTop)
        {
            _window.Left = Math.Max(areaLeft + gap, areaRight - _window.Width - gap);
            _window.Top = areaTop + gap;
        }
        else if (taskbarAtLeft)
        {
            _window.Left = areaLeft + gap;
            _window.Top = Math.Max(areaTop + gap, areaBottom - _window.Height - gap);
        }
        else if (taskbarAtRight)
        {
            _window.Left = areaRight - _window.Width - gap;
            _window.Top = Math.Max(areaTop + gap, areaBottom - _window.Height - gap);
        }
        else
        {
            _window.Left = Math.Max(areaLeft + gap, areaRight - _window.Width - gap);
            _window.Top = areaBottom - _window.Height - gap;
        }
    }

    private void SaveSettings()
    {
        ThemeManager.Apply(_settings.Theme);
        WindowsSettingsStore.Save(_settings);
    }

    private void SystemDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_window.IsVisible) PositionWindow();
        });
    }

    private void SystemUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        _window.Dispatcher.BeginInvoke(() =>
        {
            if (_settings.Theme == PortDeckTheme.System) ThemeManager.Apply(_settings.Theme);
            if (_window.IsVisible) PositionWindow();
        });
    }

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);

        using var shape = RoundedRectangle(new RectangleF(2, 2, 28, 28), 8);
        using var gradient = new LinearGradientBrush(
            new PointF(3, 2),
            new PointF(29, 30),
            Color.FromArgb(255, 168, 175, 255),
            Color.FromArgb(255, 105, 120, 247));
        graphics.FillPath(gradient, shape);

        using var font = new Font("Segoe UI", 17, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.FromArgb(255, 11, 14, 20));
        var text = "P";
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, textBrush, (32 - size.Width) / 2 + 0.5f, (32 - size.Height) / 2 - 0.5f);

        using var statusBrush = new SolidBrush(Color.FromArgb(255, 88, 214, 168));
        graphics.FillEllipse(statusBrush, 23, 23, 6, 6);

        var handle = bitmap.GetHicon();
        try
        {
            using var borrowed = Icon.FromHandle(handle);
            return (Icon)borrowed.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr windowHandle);

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= SystemDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= SystemUserPreferenceChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _trayIcon.Dispose();
        _window.AllowClose = true;
        _window.Close();
    }
}
