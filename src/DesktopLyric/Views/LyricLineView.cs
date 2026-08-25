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
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineElapsedMsProperty = DependencyProperty.Register(
        nameof(LineElapsedMs), typeof(double), typeof(LyricLineView),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsCurrentProperty = DependencyProperty.Register(
        nameof(IsCurrent), typeof(bool), typeof(LyricLineView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

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
        // Current line: one-line height so wrapping cannot steal space from translation.
        if (IsCurrent)
            return new Size(maxW, FontSize * 1.25);
        var ft = CreateFormatted(Text ?? "", Foreground, maxW, FontSize);
        var h = Math.Max(ft.Height, FontSize * 1.2);
        if (!double.IsInfinity(availableSize.Height))
            h = Math.Min(h, Math.Max(availableSize.Height, FontSize * 1.2));
        return new Size(maxW, h);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var maxW = Math.Max(ActualWidth, 1);
        var words = Words;
        if (IsCurrent && words is { Count: > 0 })
        {
            var scale = FitScale(string.Concat(words.Select(w => w.Text ?? "")), maxW);
            var fs = FontSize * scale;
            foreach (var (text, brush, x, y) in LayoutKaraoke(maxW, fs))
            {
                var ft = CreateFormatted(text, brush, 0, fs);
                DrawVector(dc, ft, new Point(x, y), brush);
            }
            return;
        }

        var brushFill = IsCurrent ? AccentBrush : Foreground;
        var fit = IsCurrent ? FitScale(Text ?? "", maxW) : 1.0;
        var body = CreateFormatted(Text ?? "", brushFill, IsCurrent ? 0 : maxW, FontSize * fit);
        var y0 = Math.Max(0, (ActualHeight - body.Height) / 2);
        var x0 = IsCurrent ? Math.Max(0, (maxW - body.Width) / 2) : 0;
        DrawVector(dc, body, new Point(x0, y0), brushFill);
    }

    private double FitScale(string text, double maxW)
    {
        if (maxW <= 1 || string.IsNullOrEmpty(text)) return 1;
        var probe = CreateFormatted(text, Brushes.White, 0, FontSize);
        if (probe.WidthIncludingTrailingWhitespace <= maxW) return 1;
        return Math.Max(0.55, maxW / probe.WidthIncludingTrailingWhitespace);
    }

    private List<(string text, Brush brush, double x, double y)> LayoutKaraoke(double maxWidth, double fontSize)
    {
        var elapsed = LineElapsedMs;
        var unsung = new SolidColorBrush(Color.FromRgb(0x58, 0x68, 0x78));
        unsung.Freeze();
        var x = 0.0;
        var result = new List<(string, Brush, double, double)>();
        var line = new List<(string text, Brush brush, double w)>();

        foreach (var w in Words ?? Array.Empty<KaraokeWordTiming>())
        {
            var endMs = w.StartMs + w.DurationMs;
            Brush b;
            if (elapsed >= endMs) b = AccentBrush;
            else if (elapsed <= w.StartMs) b = unsung;
            else
            {
                var pct = Math.Clamp((elapsed - w.StartMs) / Math.Max(1, w.DurationMs), 0, 1);
                var ac = ((SolidColorBrush)AccentBrush).Color;
                b = new SolidColorBrush(Color.FromRgb(
                    (byte)(0x58 + (ac.R - 0x58) * pct),
                    (byte)(0x68 + (ac.G - 0x68) * pct),
                    (byte)(0x78 + (ac.B - 0x78) * pct)));
                b.Freeze();
            }

            var piece = w.Text ?? "";
            var ww = CreateFormatted(piece, b, 0, fontSize).WidthIncludingTrailingWhitespace;
            line.Add((piece, b, ww));
            x += ww;
        }

        var start = Math.Max(0, (maxWidth - x) / 2);
        var y = Math.Max(0, (ActualHeight - fontSize * 1.2) / 2);
        var cx = start;
        foreach (var (t, b, w) in line)
        {
            result.Add((t, b, cx, y));
            cx += w;
        }
        return result;
    }

    private void DrawVector(DrawingContext dc, FormattedText ft, Point origin, Brush fill)
    {
        var geo = ft.BuildGeometry(origin);
        if (geo.IsEmpty()) return;
        geo.Freeze();
        dc.DrawGeometry(fill, null, geo);
    }

    private FormattedText CreateFormatted(string text, Brush brush, double maxWidth, double fontSize)
    {
        var dip = 1.0;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        if (dip <= 0) dip = 1.0;

        var typeface = LyricFonts.TypefaceFor(text, SettingsFont);
        var culture = LyricFonts.CultureFor(text);
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
        if (maxWidth > 1)
            ft.MaxTextWidth = maxWidth;
        return ft;
    }
}
