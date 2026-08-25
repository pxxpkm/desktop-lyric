using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DesktopLyric.Services;

/// <summary>Remembers which lyrics candidate the user picked for a playing track.</summary>
public static class LyricChoiceStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "choices.json");

    private static Dictionary<string, string>? _cache;
    private static readonly object _lock = new();

    private static string MakeKey(string? title, string? artist)
    {
        static string Norm(string? s) =>
            Regex.Replace((s ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        return $"{Norm(artist)}|{Norm(title)}";
    }

    public static string? Get(string? title, string? artist)
    {
        var dict = Load();
        return dict.TryGetValue(MakeKey(title, artist), out var v) ? v : null;
    }

    public static void Set(string? title, string? artist, string candidateKey)
    {
        lock (_lock)
        {
            var dict = Load();
            dict[MakeKey(title, artist)] = candidateKey;
            _cache = dict;
        }
        Save();
    }

    public static void Clear(string? title, string? artist)
    {
        lock (_lock)
        {
            var dict = Load();
            dict.Remove(MakeKey(title, artist));
            _cache = dict;
        }
        Save();
    }

    private static Dictionary<string, string> Load()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(StorePath))
                {
                    _cache = JsonSerializer.Deserialize<Dictionary<string, string>>(
                        File.ReadAllText(StorePath)) ?? new();
                    return _cache;
                }
            }
            catch { }
            _cache = new();
            return _cache;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            Dictionary<string, string> snap;
            lock (_lock) { snap = new(_cache ?? new()); }
            File.WriteAllText(StorePath, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
