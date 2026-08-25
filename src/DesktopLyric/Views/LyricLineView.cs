using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace DesktopLyric.Views;

/// <summary>
/// Draws lyrics as vector geometry instead of hinted TextBlock glyphs.
/// Transparent WPF disables ClearType; GDI-style hinting then looks like
/// old Windows UI fonts. Geometry stays smooth at overlay sizes.
/// </summary>
public class LyricLineView : FrameworkElement
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
        nameof(Text), typeof(string), typeof(LyricLineView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FontSizeProperty = DependencyProperty.Register(
        nameof(FontSize), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(28.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(
        nameof(Foreground), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(
        nameof(AccentBrush), typeof(Brush), typeof(LyricLineView),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff)), FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SettingsFontProperty = DependencyProperty.Register(
        nameof(SettingsFont), typeof(string), typeof(LyricLineView),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty WordsProperty = DependencyProperty.Register(
        nameof(Words), typeof(IList<KaraokeWordTiming>), typeof(LyricLineView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineElapsedMsProperty = DependencyProperty.Register(
        nameof(LineElapsedMs), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(LyricLineView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    private string? _karaokeKey;
    private List<(string text, double x, double y, int start, int dur)>? _karaokeLayout;

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public string SettingsFont
    {
        get => (string)GetValue(SettingsFontProperty);
        set => SetValue(SettingsFontProperty, value);
    }

    public IList<KaraokeWordTiming>? Words
    {
        get => (IList<KaraokeWordTiming>?)GetValue(WordsProperty);
        set => SetValue(WordsProperty, value);
    }

    public double LineElapsedMs
    {
        get => (double)GetValue(LineElapsedMsProperty);
        set => SetValue(LineElapsedMsProperty, value);
    }

    public bool IsCurrent
    {
        get => (bool)GetValue(IsCurrentProperty);
        set => SetValue(IsCurrentProperty, value);
    }

    public LyricLineView()
    {
        SnapsToDevicePixels = false;
        UseLayoutRounding = false;
        ClipToBounds = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Grayscale);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Animated);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double maxW = double.IsInfinity(availableSize.Width) ? 4096 : Math.Max(availableSize.Width, 1);
        var fs = SafeFontSize();
        // Current line: one-line height so wrapping cannot steal space from translation.
        if (IsCurrent)
            return new Size(maxW, fs * 1.25);
        var ft = CreateFormatted(Text ?? "", Foreground, maxW, fs);
        var h = Math.Max(ft.Height, fs * 1.2);
        if (!double.IsInfinity(availableSize.Height))
            h = Math.Min(h, Math.Max(availableSize.Height, fs * 1.2));
        return new Size(maxW, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        try
        {
            RenderCore(dc);
        }
        catch
        {
            try
            {
                var fs = SafeFontSize();
                var body = CreateFormatted(Text ?? "", AccentColorBrush(), 0, fs);
                DrawVector(dc, body, new Point(0, 0), AccentColorBrush());
            }
            catch { }
        }
    }

    private void RenderCore(DrawingContext dc)
    {
        var maxW = Math.Max(ActualWidth, 1);
        var fs = SafeFontSize();
        var words = Words;
        if (IsCurrent && words is { Count: > 0 } && words.Count < 400)
        {
            var layout = EnsureKaraokeLayout(words, maxW, fs);
            var elapsed = double.IsNaN(LineElapsedMs) ? 0 : LineElapsedMs;
            var drawFs = fs * FitScale(string.Concat(words.Take(80).Select(w => w.Text ?? "")), maxW);
            foreach (var (text, x, y, start, dur) in layout)
            {
                var brush = KaraokeBrush(elapsed, start, dur);
                var ft = CreateFormatted(text, brush, 0, drawFs);
                DrawVector(dc, ft, new Point(x, y), brush);
            }
            return;
        }

        var brushFill = IsCurrent ? AccentColorBrush() : Foreground;
        var fit = IsCurrent ? FitScale(Text ?? "", maxW) : 1.0;
        var body = CreateFormatted(Text ?? "", brushFill, IsCurrent ? 0 : maxW, fs * fit);
        var y0 = Math.Max(0, (ActualHeight - body.Height) / 2);
        var x0 = IsCurrent ? Math.Max(0, (maxW - body.Width) / 2) : 0;
        DrawVector(dc, body, new Point(x0, y0), brushFill);
    }

    private List<(string text, double x, double y, int start, int dur)> EnsureKaraokeLayout(
        IList<KaraokeWordTiming> words, double maxWidth, double fontSize)
    {
        var scale = FitScale(string.Concat(words.Select(w => w.Text ?? "")), maxWidth);
        var fs = fontSize * scale;
        var key = $"{maxWidth:0.#}|{ActualHeight:0.#}|{fs:0.##}|{SettingsFont}|{FontHint()}|{words.Count}|{Text}";
        if (_karaokeLayout != null && _karaokeKey == key)
            return _karaokeLayout;

        var unsung = Brushes.White;
        var x = 0.0;
        var pieces = new List<(string text, double w, int start, int dur)>();
        var n = Math.Min(words.Count, 80);
        for (int i = 0; i < n; i++)
        {
            var w = words[i];
            var piece = w.Text ?? "";
            if (piece.Length == 0) continue;
            var ww = CreateFormatted(piece, unsung, 0, fs).WidthIncludingTrailingWhitespace;
            if (double.IsNaN(ww) || double.IsInfinity(ww)) ww = fs;
            pieces.Add((piece, ww, w.StartMs, Math.Max(0, w.DurationMs)));
            x += ww;
        }

        var startX = Math.Max(0, (maxWidth - x) / 2);
        var y = Math.Max(0, (ActualHeight - fs * 1.2) / 2);
        if (double.IsNaN(y) || double.IsInfinity(y)) y = 0;
        var layout = new List<(string, double, double, int, int)>(pieces.Count);
        var cx = startX;
        foreach (var (t, w, start, dur) in pieces)
        {
            layout.Add((t, cx, y, start, dur));
            cx += w;
        }
        _karaokeKey = key;
        _karaokeLayout = layout;
        return layout;
    }

    private Brush KaraokeBrush(double elapsed, int startMs, int durMs)
    {
        var endMs = startMs + durMs;
        if (elapsed >= endMs) return AccentColorBrush();
        if (elapsed <= startMs || durMs <= 0) return UnsungBrush();
        var pct = Math.Clamp((elapsed - startMs) / durMs, 0, 1);
        var ac = AccentColor();
        var b = new SolidColorBrush(Color.FromRgb(
            (byte)(0x58 + (ac.R - 0x58) * pct),
            (byte)(0x68 + (ac.G - 0x68) * pct),
            (byte)(0x78 + (ac.B - 0x78) * pct)));
        b.Freeze();
        return b;
    }

    private static readonly SolidColorBrush UnsungFrozen = CreateUnsung();
    private static SolidColorBrush CreateUnsung()
    {
        var b = new SolidColorBrush(Color.FromRgb(0x58, 0x68, 0x78));
        b.Freeze();
        return b;
    }
    private static Brush UnsungBrush() => UnsungFrozen;

    private Color AccentColor()
        => AccentBrush is SolidColorBrush sb ? sb.Color : Color.FromRgb(0x00, 0xd4, 0xff);

    private Brush AccentColorBrush()
        => AccentBrush as SolidColorBrush ?? Brushes.Cyan;

    private string FontHint()
    {
        if (LyricFonts.HasKana(Text)) return Text ?? "";
        var words = Words;
        if (words is { Count: > 0 })
        {
            var all = string.Concat(words.Take(80).Select(w => w.Text ?? ""));
            if (LyricFonts.HasKana(all)) return all;
        }
        return Text ?? "";
    }

    private double SafeFontSize()
    {
        var fs = FontSize;
        if (double.IsNaN(fs) || double.IsInfinity(fs) || fs < 1) return 14;
        return Math.Clamp(fs, 8, 160);
    }

    private double FitScale(string text, double maxW)
    {
        if (maxW <= 1 || string.IsNullOrEmpty(text)) return 1;
        var fs = SafeFontSize();
        var probe = CreateFormatted(text, Brushes.White, 0, fs);
        if (probe.WidthIncludingTrailingWhitespace <= maxW) return 1;
        var scale = maxW / probe.WidthIncludingTrailingWhitespace;
        if (double.IsNaN(scale) || double.IsInfinity(scale)) return 1;
        return Math.Max(0.55, scale);
    }

    private void DrawVector(DrawingContext dc, FormattedText ft, Point origin, Brush fill)
    {
        if (double.IsNaN(origin.X) || double.IsNaN(origin.Y)
            || double.IsInfinity(origin.X) || double.IsInfinity(origin.Y))
            origin = new Point(0, 0);
        var geo = ft.BuildGeometry(origin);
        if (geo.IsEmpty()) return;
        geo.Freeze();
        dc.DrawGeometry(fill, null, geo);
    }

    private FormattedText CreateFormatted(string text, Brush brush, double maxWidth, double fontSize)
    {
        if (double.IsNaN(fontSize) || fontSize < 1) fontSize = SafeFontSize();
        var dip = 1.0;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        if (dip <= 0 || double.IsNaN(dip)) dip = 1.0;

        var hint = FontHint();
        var typeface = LyricFonts.TypefaceFor(text, SettingsFont, hint);
        var culture = LyricFonts.CultureFor(text, hint);
        if (text.Length > 400) text = text[..400];
        var ft = new FormattedText(
            string.IsNullOrEmpty(text) ? " " : text,
            culture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            brush,
            dip)
        {
            Trimming = TextTrimming.None,
            TextAlignment = maxWidth > 1 ? TextAlignment.Center : TextAlignment.Left,
        };
        if (maxWidth > 1 && !double.IsInfinity(maxWidth) && !double.IsNaN(maxWidth))
            ft.MaxTextWidth = maxWidth;
        return ft;
    }
}
