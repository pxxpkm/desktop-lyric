using System.Threading;
using System.Windows;

namespace DesktopLyric;

public partial class App : Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "DesktopLyric_SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("already running", "Desktop Lyric");
            Shutdown();
            return;
        }
        FontLoader.Load();
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
