using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace DesktopLyric.Views;

public partial class OverlayWindow : Window
{
    private AppSettings _settings;

    public event Action? TraditionalToggled;

    public OverlayWindow() : this(AppSettings.Load()) { }

    public OverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ApplyAccentColor();
        ApplyTradButton();
        ApplyFont();
    }

    private void ApplyFont()
    {
        FontFamily = LyricFonts.FromSettings(_settings.FontFamily);
        OvTrackTitle.FontFamily = FontFamily;
        OvTrackArtist.FontFamily = FontFamily;
    }

    private void ApplyAccentColor()
    {
        try
        {
            var c = (Color)ColorConverter.ConvertFromString(_settings.AccentColor);
            var b = new SolidColorBrush(c);
            b.Freeze();
            OvCurrent.AccentBrush = b;
            OvCurrent.Foreground = b;
        }
        catch { }
    }

    public void SetTrackInfo(string title, string artist)
    {
        OvTrackTitle.Text = title ?? "";
        OvTrackArtist.Text = artist ?? "";
    }

    public void RefreshTradButton() => ApplyTradButton();

    public void RefreshFonts()
    {
        var custom = _settings.FontFamily ?? "";
        OvCurrent.SettingsFont = custom;
        OvTrans.SettingsFont = custom;
        OvNext.SettingsFont = custom;
    }

    private void ApplyTradButton()
    {
        BtnTrad.Foreground = _settings.ForceTraditional
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff))
            : new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
    }

    public void UpdateLyrics(string current, string? translated, string? next = null,
        List<KaraokeWordTiming>? wordTimings = null, double lineElapsedMs = 0)
    {
        var custom = _settings.FontFamily ?? "";
        OvCurrent.SettingsFont = custom;
        OvCurrent.Text = current ?? "";
        OvCurrent.Words = wordTimings;
        OvCurrent.LineElapsedMs = lineElapsedMs;
        OvTrans.SettingsFont = custom;
        OvTrans.Text = translated ?? "";
        OvNext.SettingsFont = custom;
        OvNext.Text = next ?? "";
        ApplyLineSizes();
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLineSizes();

    private void ApplyLineSizes()
    {
        var area = LyricsHost?.ActualHeight > 1
            ? LyricsHost.ActualHeight
            : Math.Max(50, ActualHeight - 96);
        var jpCur = LyricFonts.HasKana(OvCurrent.Text);
        var jpTrans = LyricFonts.HasKana(OvTrans.Text);
        OvCurrent.FontSize = Math.Clamp(area * (jpCur ? 0.24 : 0.28), 14, 48);
        OvTrans.FontSize = Math.Clamp(area * (jpTrans ? 0.18 : 0.30), 14, 44);
        OvNext.FontSize = Math.Clamp(area * 0.12, 10, 20);
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
        var anim = new DoubleAnimation(0.55, TimeSpan.FromMilliseconds(150));
        ControlBar.BeginAnimation(OpacityProperty, anim);
    }

    private void OnFontLarger(object sender, RoutedEventArgs e)
    {
        GrowWindow(1.12);
    }

    private void OnFontSmaller(object sender, RoutedEventArgs e)
    {
        GrowWindow(1 / 1.12);
    }

    private void GrowWindow(double factor)
    {
        Height = Math.Clamp(Height * factor, MinHeight, MaxHeight);
        ApplyLineSizes();
    }

    private void OnToggleTrad(object sender, RoutedEventArgs e)
    {
        _settings.ForceTraditional = !_settings.ForceTraditional;
        ApplyTradButton();
        _settings.Save();
        TraditionalToggled?.Invoke();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
