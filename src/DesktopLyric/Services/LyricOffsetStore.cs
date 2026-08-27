using System.IO;
using System.Text.Json;

namespace DesktopLyric.Services;

/// <summary>
/// Per-track lyric timing offset in milliseconds, saved to offsets.json.
/// Positive = show lyrics earlier; negative = later.
/// </summary>
public static class LyricOffsetStore
{
    private static string _storePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "offsets.json");

    private static Dictionary<string, int>? _cache;
    private static readonly object _lock = new();

    public const int StepMs = 50;
    public const int MediumStepMs = 250;
    public const int FastStepMs = 1_000;
    public const int MinMs = -300_000;
    public const int MaxMs = 300_000;
    public const int HoldDelayMs = 400;
    public const int HoldAccelMs = 1_200;
    public const int HoldFastMs = 2_500;

    /// <summary>
    /// Extra step while a ＋/− button is held. 0 until HoldDelayMs so the
    /// initial MouseDown 50ms step is not immediately doubled.
    /// </summary>
    public static int StepForHoldMs(double heldMs)
    {
        if (heldMs < HoldDelayMs) return 0;
        if (heldMs < HoldAccelMs) return StepMs;
        if (heldMs < HoldFastMs) return MediumStepMs;
        return FastStepMs;
    }

    internal static void ResetForTests(string storePath)
    {
        lock (_lock)
        {
            _storePath = storePath;
            _cache = null;
        }
    }

    public static int GetMs(string? title, string? artist)
    {
        var dict = LoadDict();
        if (dict.Count == 0) return 0;

        foreach (var key in LyricChoiceStore.FingerprintKeys(title, artist))
        {
            if (dict.TryGetValue(key, out var v)) return v;
        }

        var wantTitles = LyricChoiceStore.TitleKeys(title);
        if (wantTitles.Count == 0) return 0;
        int? unique = null;
        var uniqueCount = 0;
        foreach (var kv in dict)
        {
            var i = kv.Key.IndexOf('|');
            var storedTitle = i < 0 ? kv.Key : kv.Key[(i + 1)..];
            var storedTitles = LyricChoiceStore.TitleKeys(storedTitle);
            if (!wantTitles.Any(w => storedTitles.Contains(w))) continue;
            uniqueCount++;
            unique = kv.Value;
        }
        return uniqueCount == 1 ? unique!.Value : 0;
    }

    public static void SetMs(string? title, string? artist, int ms)
    {
        ms = Math.Clamp(ms, MinMs, MaxMs);
        var keys = LyricChoiceStore.FingerprintKeys(title, artist);
        if (keys.Count == 0) return;
        lock (_lock)
        {
            var dict = LoadDict();
            var titles = LyricChoiceStore.TitleKeys(title);
            foreach (var existing in dict.Keys.ToList())
            {
                if (keys.Contains(existing)) continue;
                var i = existing.IndexOf('|');
                var storedTitle = i < 0 ? existing : existing[(i + 1)..];
                if (titles.Any(t => LyricChoiceStore.TitleKeys(storedTitle).Contains(t)))
                    dict.Remove(existing);
            }
            foreach (var key in keys)
            {
                if (ms == 0) dict.Remove(key);
                else dict[key] = ms;
            }
            _cache = dict;
        }
        SaveDict();
    }

    public static int Nudge(string? title, string? artist, int deltaMs)
    {
        var next = Math.Clamp(GetMs(title, artist) + deltaMs, MinMs, MaxMs);
        SetMs(title, artist, next);
        return next;
    }

    public static string Format(int ms)
    {
        var sign = ms > 0 ? "+" : ms < 0 ? "−" : "±";
        return $"{sign}{Math.Abs(ms) / 1000.0:0.00}s";
    }

    private static Dictionary<string, int> LoadDict()
    {
        lock (_lock)
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(_storePath))
                {
                    _cache = JsonSerializer.Deserialize<Dictionary<string, int>>(
                        File.ReadAllText(_storePath)) ?? new();
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
            Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
            Dictionary<string, int> snap;
            lock (_lock) { snap = new(_cache ?? new()); }
            File.WriteAllText(_storePath, JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
