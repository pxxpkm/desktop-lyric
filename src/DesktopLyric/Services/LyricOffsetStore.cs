using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopLyric.Services;

/// <summary>
/// remembers per-track lyric offset (ms) so you don't have to adjust every time
/// </summary>
public static class LyricOffsetStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "offsets.json");

    // some songs are consistently off by a few hundred ms, this remembers it
    private static Dictionary<string, int>? _cache;
    private static readonly object _lock = new();

    private static string MakeKey(string? title, string? artist)
    {
        static string Norm(string? s) =>
            Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        return $"{Norm(artist)}|{Norm(title)}";
    }

    public static int GetMs(string? title, string? artist)
    {
        var dict = LoadDict();
        var key = MakeKey(title, artist);
        return dict.TryGetValue(key, out var v) ? v : 0;
    }

    public static void SetMs(string? title, string? artist, int ms)
    {
        lock (_lock)
        {
            var dict = LoadDict();
            var key = MakeKey(title, artist);
            if (ms == 0) dict.Remove(key);
            else dict[key] = ms;
            _cache = dict;
        }
        SaveDict();
    }

    private static Dictionary<string, int> LoadDict()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(StorePath))
                {
                    var json = File.ReadAllText(StorePath);
                    _cache = JsonSerializer.Deserialize<Dictionary<string, int>>(json) ?? new();
                    return _cache;
                }
            }
            catch { }
            _cache = new();
            return _cache;
        }
    }

    private static void SaveDict()
    {
        try
        {
            var dir = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(dir);
            Dictionary<string, int> snap;
            lock (_lock) { snap = new(_cache ?? new()); }
            File.WriteAllText(StorePath, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
