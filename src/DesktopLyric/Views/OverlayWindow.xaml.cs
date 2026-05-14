using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopLyric.Views;

public partial class OverlayWindow : Window
{
    private AppSettings _settings;
    private double _mainFontSize = 24;
    private double _transFontSize = 14;

    public OverlayWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        ApplyAccentColor();
    }

    private void ApplyAccentColor()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
            OvCurrent.Foreground = new SolidColorBrush(c);
        }
        catch { }
    }

    public void SetTrackInfo(string title, string artist)
    {
        OvTrackTitle.Text = title ?? "";
        OvTrackArtist.Text = artist ?? "";
    }

    public void UpdateLyrics(string current, string? translated, string? next = null,
        List<KaraokeWordTiming>? wordTimings = null, double lineElapsedMs = 0)
    {
        // karaoke mode
        if (wordTimings != null && wordTimings.Count > 0)
        {
            OvCurrent.Inlines.Clear();
            foreach (var w in wordTimings)
            {
                var endMs = w.StartMs + w.DurationMs;
                Color c;
                if (lineElapsedMs >= endMs)
                    c = Color.FromRgb(0x00, 0xd4, 0xff);
                else if (lineElapsedMs <= w.StartMs)
                    c = Color.FromRgb(0x50, 0x60, 0x70);
                else
                {
                    // blend between unsung and sung
                    var pct = (lineElapsedMs - w.StartMs) / Math.Max(1, w.DurationMs);
                    pct = Math.Clamp(pct, 0, 1);
                    var r = (byte)(0x50 + (0x00 - 0x50) * pct);
                    var g = (byte)(0x60 + (0xd4 - 0x60) * pct);
                    var b = (byte)(0x70 + (0xff - 0x70) * pct);
                    c = Color.FromRgb(r, g, b);
                }
                OvCurrent.Inlines.Add(new Run(w.Text) { Foreground = new SolidColorBrush(c) });
            }
        }
        else
        {
            OvCurrent.Inlines.Clear();
            OvCurrent.Inlines.Add(new Run(current ?? ""));
        }

        OvTrans.Text = translated ?? "";
        OvNext.Text = next ?? "";
    }

    // --- controls ---

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnMouseEnter(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(1, TimeSpan.FromMilliseconds(150));
        ControlBar.BeginAnimation(OpacityProperty, anim);
    }

    private void OnMouseLeave(object sender, MouseEventArgs e)
    {
        var anim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(150));
        ControlBar.BeginAnimation(OpacityProperty, anim);
    }

    private void OnFontLarger(object sender, RoutedEventArgs e)
    {
        _mainFontSize = Math.Min(48, _mainFontSize + 2);
        _transFontSize = Math.Min(28, _transFontSize + 1);
        OvCurrent.FontSize = _mainFontSize;
        OvTrans.FontSize = _transFontSize;
    }

    private void OnFontSmaller(object sender, RoutedEventArgs e)
    {
        _mainFontSize = Math.Max(14, _mainFontSize - 2);
        _transFontSize = Math.Max(10, _transFontSize - 1);
        OvCurrent.FontSize = _mainFontSize;
        OvTrans.FontSize = _transFontSize;
    }

    private void OnToggleTrad(object sender, RoutedEventArgs e)
    {
        _settings.ForceTraditional = !_settings.ForceTraditional;
        BtnTrad.Foreground = _settings.ForceTraditional
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff))
            : new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
        _settings.Save();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
