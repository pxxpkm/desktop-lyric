using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopLyric;

/// <summary>
/// Transparent WPF disables ClearType. Hinted UI fonts (JhengHei) then look
/// like old Windows. Lyrics are drawn as vectors; Chinese default is Chiron
/// GoRound TC. Japanese kana uses Yu Gothic UI.
/// </summary>
public static class LyricFonts
{
    public const string JapaneseStack =
        "Yu Gothic UI, Yu Gothic Medium, Yu Gothic, Meiryo UI, Segoe UI";

    public static readonly (string Family, string Label)[] ChineseChoices =
    [
        ("Chiron GoRound TC", "昭源圓體"),
        ("Chiron GoRound TC Medium", "昭源圓體 M"),
        ("Noto Sans HK Medium", "Noto HK"),
        ("Noto Sans TC Medium", "Noto TC"),
        ("Noto Sans HK", "Noto HK 常規"),
        ("Microsoft JhengHei UI", "正黑體"),
        ("Microsoft YaHei UI", "雅黑"),
    ];

    public static FontFamily Japanese { get; } = new(JapaneseStack);

    public static FontFamily ChineseDefault =>
        FontLoader.BundledChiron ?? new FontFamily("Chiron GoRound TC");

    public static FontFamily FromSettings(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Equals("Chiron GoRound TC", StringComparison.OrdinalIgnoreCase))
            return ChineseDefault;
        if (name.Equals("Chiron GoRound TC Medium", StringComparison.OrdinalIgnoreCase) &&
            FontLoader.FontsUri != null)
            return new FontFamily(FontLoader.FontsUri, "./#Chiron GoRound TC Medium");
        return new FontFamily(name);
    }

    public static CultureInfo CultureFor(string? text)
        => HasKana(text)
            ? CultureInfo.GetCultureInfo("ja-JP")
            : CultureInfo.GetCultureInfo("zh-HK");

    public static Typeface TypefaceFor(string? text, string? settingsFont)
    {
        var family = HasKana(text) ? Japanese : FromSettings(settingsFont);
        // Weight lives in the family name for Noto ("... Medium"). Faux-bold looks XP-era.
        return new Typeface(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    }

    public static string CurrentLabel(string? family)
    {
        if (string.IsNullOrWhiteSpace(family)) return ChineseChoices[0].Label;
        foreach (var (fam, label) in ChineseChoices)
            if (fam.Equals(family, StringComparison.OrdinalIgnoreCase)) return label;
        return family;
    }

    public static string CycleChinese(string? current)
    {
        int i = 0;
        if (!string.IsNullOrWhiteSpace(current))
        {
            for (int n = 0; n < ChineseChoices.Length; n++)
                if (ChineseChoices[n].Family.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    i = n;
                    break;
                }
        }
        return ChineseChoices[(i + 1) % ChineseChoices.Length].Family;
    }

    public static double LineSize(string? text, bool current, double delta = 0)
    {
        var jp = HasKana(text);
        var n = current ? (jp ? 24.0 : 28.0) : (jp ? 16.0 : 22.0);
        return Math.Clamp(n + delta, 10, 48);
    }

    public static bool HasKana(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var c in text)
        {
            if (c is (>= '\u3040' and <= '\u309F')
                or (>= '\u30A0' and <= '\u30FF')
                or (>= '\u31F0' and <= '\u31FF')
                or (>= '\uFF66' and <= '\uFF9D'))
                return true;
        }
        return false;
    }
}
