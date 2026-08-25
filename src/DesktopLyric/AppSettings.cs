using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopLyric;

public class AppSettings
{
    public bool BoldLyrics { get; set; } = true;
    public bool HideTranslation { get; set; } = false;
    public bool ForceTraditional { get; set; } = true;
    public bool ShowRomaji { get; set; } = false;
    public int GlobalOffsetMs { get; set; } = 0; // negative = lyrics show earlier
    public double OverlayOpacity { get; set; } = 85;
    public string AccentColor { get; set; } = "#00d4ff"; // might add color picker later

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DesktopLyric", "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            }
        }
        catch { }
        return new();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(SettingsPath)!;
            Directory.CreateDirectory(dir);
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsPath, json);
        }
        catch { }
    }
}
