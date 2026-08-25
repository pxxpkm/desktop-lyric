using System.IO;
using System.Text;
using System.Windows;

namespace DesktopLyric;

internal static class ErrorLog
{
    private static readonly object Gate = new();
    private static readonly string PathFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "error.log");

    public static void Attach(Application app)
    {
        app.DispatcherUnhandledException += (_, e) =>
        {
            Write(e.Exception);
            e.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Write(ex);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Write(e.Exception);
            e.SetObserved();
        };
    }

    public static void Write(Exception ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathFile)!);
                var sb = new StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("s") + " ====");
                sb.AppendLine(ex.GetType().FullName);
                sb.AppendLine(ex.Message);
                sb.AppendLine(ex.StackTrace);
                if (ex.InnerException != null)
                {
                    sb.AppendLine("inner: " + ex.InnerException.GetType().FullName);
                    sb.AppendLine(ex.InnerException.Message);
                }
                sb.AppendLine();
                File.AppendAllText(PathFile, sb.ToString());
            }
        }
        catch { }
    }
}
