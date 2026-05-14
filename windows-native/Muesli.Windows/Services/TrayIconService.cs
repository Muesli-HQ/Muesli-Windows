using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Forms = System.Windows.Forms;

namespace Muesli.Windows.Services;

public sealed class TrayIconService : IDisposable
{
    private Forms.NotifyIcon? _notifyIcon;
    private Window? _window;

    public void Initialize(Window window)
    {
        _window = window;
        _notifyIcon = new Forms.NotifyIcon
        {
            Text = "Muesli",
            Icon = LoadTrayIcon(),
            Visible = true,
            ContextMenuStrip = BuildContextMenu()
        };
        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    public void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        if (_window is MainWindow mainWindow)
        {
            mainWindow.ShowDashboardFromBackground();
            return;
        }

        _window.ShowInTaskbar = true;
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    private static Icon LoadTrayIcon()
    {
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "muesli_app_icon.png");
        if (!File.Exists(iconPath))
        {
            return SystemIcons.Application;
        }

        try
        {
            using var source = new Bitmap(iconPath);
            using var resized = new Bitmap(source, new System.Drawing.Size(32, 32));
            var handle = resized.GetHicon();
            try
            {
                return (Icon)Icon.FromHandle(handle).Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private Forms.ContextMenuStrip BuildContextMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Muesli", null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(ShowWindow));
        menu.Items.Add("Quit", null, (_, _) => System.Windows.Application.Current.Dispatcher.Invoke(System.Windows.Application.Current.Shutdown));
        return menu;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
