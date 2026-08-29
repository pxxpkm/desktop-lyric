using System.IO;
using System.Text;

namespace DesktopLyric;

/// <summary>
/// One-line lifecycle log. Native heap/Finalize crashes never reach error.log;
/// if the last line is start/hide with no quit, the process was killed or AVed.
/// </summary>
internal static class RunLog
{
    private static readonly object Gate = new();
    private static readonly string PathFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "run.log");

    public static string FilePath => PathFile;

    public static void Write(string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathFile)!);
                var line = DateTime.Now.ToString("s")
                    + " pid=" + Environment.ProcessId
                    + " " + msg
                    + Environment.NewLine;
                File.AppendAllText(PathFile, line, Encoding.UTF8);
                TrimIfHuge();
            }
        }
        catch { }
    }

    private static void TrimIfHuge()
    {
        try
        {
            var info = new FileInfo(PathFile);
            if (!info.Exists || info.Length < 256_000) return;
            var text = File.ReadAllText(PathFile, Encoding.UTF8);
            var keep = text.Length > 80_000 ? text[^80_000..] : text;
            File.WriteAllText(PathFile, keep, Encoding.UTF8);
        }
        catch { }
    }
}
