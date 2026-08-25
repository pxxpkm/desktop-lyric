using System.Threading;
using System.Windows;
using DesktopLyric.Services;

namespace DesktopLyric;

public partial class App : Application
{
    private static Mutex? _mutex;
    internal TrayIconService? Tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DesktopLyric_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("already running", "Desktop Lyric");
            Shutdown();
            return;
        }
        try { System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2); }
        catch { }
        FontLoader.Load();
        ErrorLog.Attach(this);
        Tray = new TrayIconService();
        Tray.Start();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Tray?.Dispose();
        Tray = null;
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
