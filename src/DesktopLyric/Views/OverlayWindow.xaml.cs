using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DesktopLyric.Services;

namespace DesktopLyric.Views;

public partial class OverlayWindow : Window
{
    private AppSettings _settings;
    private bool _applyingSize;
    private bool _closed;
    private HoldRepeat? _offsetHold;

    public event Action? TraditionalToggled;
    public event Action<int>? OffsetNudged;
    public event Action? PickSongRequested;
    public event Action? FullscreenRequested;

    public OverlayWindow() : this(AppSettings.Load()) { }

    public OverlayWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ApplyAccentColor();
        ApplyTradButton();
        ApplyTopmost();
        ApplyFont();
        _offsetHold = new HoldRepeat(delta => OffsetNudged?.Invoke(delta));
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

    public void SetOffsetLabel(int ms)
    {
        if (!WindowGuard.CanTouch(this)) return;
        BtnOffset.Content = LyricOffsetStore.Format(ms);
        BtnOffset.Foreground = ms == 0
            ? new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90))
            : new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff));
    }

    public void RefreshFonts()
    {
        if (!WindowGuard.CanTouch(this) || _closed) return;
        var custom = _settings.FontFamily ?? "";
        OvCurrent.SettingsFont = custom;
        OvTrans.SettingsFont = custom;
        OvNext.SettingsFont = custom;
        ApplyLineSizes();
    }

    public void RefreshAppearance()
    {
        if (!WindowGuard.CanTouch(this) || _closed) return;
        ApplyAccentColor();
        ApplyTradButton();
        ApplyTopmost();
        ApplyFont();
        Opacity = Math.Clamp(_settings.OverlayOpacity / 100.0, 0.35, 1);
        RefreshFonts();
    }

    private void ApplyTradButton()
    {
        BtnTrad.Foreground = _settings.ForceTraditional
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff))
            : new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
    }

    private void ApplyTopmost()
    {
        Topmost = _settings.OverlayTopmost;
        BtnTopmost.Foreground = _settings.OverlayTopmost
            ? new SolidColorBrush(Color.FromRgb(0x00, 0xd4, 0xff))
            : new SolidColorBrush(Color.FromRgb(0x90, 0x90, 0x90));
    }

    public void UpdateLyrics(string current, string? translated, string? next = null,
        List<KaraokeWordTiming>? wordTimings = null, double lineElapsedMs = 0)
    {
        if (!WindowGuard.CanTouch(this) || _closed) return;
        try
        {
            var custom = _settings.FontFamily ?? "";
            var needLayout = OvCurrent.Text != (current ?? "")
                || OvTrans.Text != (translated ?? "")
                || OvNext.Text != (next ?? "");
            OvCurrent.SettingsFont = custom;
            OvCurrent.Text = current ?? "";
            OvCurrent.Words = wordTimings;
            OvCurrent.LineElapsedMs = lineElapsedMs;
            OvTrans.SettingsFont = custom;
            OvTrans.Text = translated ?? "";
            OvNext.SettingsFont = custom;
            OvNext.Text = next ?? "";
            if (needLayout) ApplyLineSizes();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_applyingSize) ApplyLineSizes();
    }

    private void ApplyLineSizes()
    {
        if (_applyingSize || _closed || !IsLoaded) return;
        _applyingSize = true;
        try
        {
            var area = LyricsHost?.ActualHeight > 1
                ? LyricsHost.ActualHeight
                : Math.Max(50, ActualHeight - 96);
            if (double.IsNaN(area) || double.IsInfinity(area)) return;
            var sizes = LyricFonts.FitOverlaySizes(
                area,
                hasTrans: !string.IsNullOrWhiteSpace(OvTrans.Text),
                hasNext: !string.IsNullOrWhiteSpace(OvNext.Text),
                originalIsJapanese: LyricFonts.HasKana(OvCurrent.Text),
                originalScale: _settings.OverlayOriginalScale,
                translationScale: _settings.OverlayTranslationScale);
            WindowGuard.SetFontSize(OvCurrent, sizes.CurrentFont);
            WindowGuard.SetFontSize(OvTrans, sizes.TransFont);
            WindowGuard.SetMaxHeight(OvTrans, sizes.TransMaxHeight);
            WindowGuard.SetFontSize(OvNext, sizes.NextFont);
            WindowGuard.SetMaxHeight(OvNext, sizes.NextMaxHeight);
            OvCurrent.InvalidateVisual();
            OvTrans.InvalidateVisual();
            OvNext.InvalidateVisual();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally { _applyingSize = false; }
    }

    // --- controls ---

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is System.Windows.Controls.Button) return;
        try { DragMove(); } catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        _offsetHold?.Dispose();
        _offsetHold = null;
        base.OnClosed(e);
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

    private void NudgeOriginal(double delta)
    {
        _settings.OverlayOriginalScale = Math.Clamp(
            _settings.OverlayOriginalScale + delta, LyricFonts.ScaleMin, LyricFonts.ScaleMax);
        _settings.Save();
        ApplyLineSizes();
    }

    private void NudgeTranslation(double delta)
    {
        _settings.OverlayTranslationScale = Math.Clamp(
            _settings.OverlayTranslationScale + delta, LyricFonts.ScaleMin, LyricFonts.ScaleMax);
        _settings.Save();
        ApplyLineSizes();
    }

    private void OnOriginalSmaller(object sender, RoutedEventArgs e) => NudgeOriginal(-LyricFonts.ScaleStep);
    private void OnOriginalLarger(object sender, RoutedEventArgs e) => NudgeOriginal(LyricFonts.ScaleStep);
    private void OnTransSmaller(object sender, RoutedEventArgs e) => NudgeTranslation(-LyricFonts.ScaleStep);
    private void OnTransLarger(object sender, RoutedEventArgs e) => NudgeTranslation(LyricFonts.ScaleStep);

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
        _applyingSize = true;
        try
        {
            var h = Height * factor;
            if (double.IsNaN(h) || double.IsInfinity(h)) return;
            Height = Math.Clamp(h, MinHeight, MaxHeight);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
        finally { _applyingSize = false; }
        ApplyLineSizes();
    }

    private void OnOffsetEarlierDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _offsetHold?.Down(1, sender as IInputElement);
    }

    private void OnOffsetLaterDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _offsetHold?.Down(-1, sender as IInputElement);
    }

    private void OnOffsetHoldUp(object sender, MouseEventArgs e)
        => _offsetHold?.Up();

    private void OnOffsetReset(object sender, RoutedEventArgs e)
        => OffsetNudged?.Invoke(int.MinValue);

    private void OnToggleTrad(object sender, RoutedEventArgs e)
    {
        _settings.ForceTraditional = !_settings.ForceTraditional;
        ApplyTradButton();
        _settings.Save();
        TraditionalToggled?.Invoke();
    }

    private void OnToggleTopmost(object sender, RoutedEventArgs e)
    {
        _settings.OverlayTopmost = !_settings.OverlayTopmost;
        _settings.Save();
        ApplyTopmost();
    }

    private void OnPickSong(object sender, RoutedEventArgs e) => PickSongRequested?.Invoke();

    private void OnFullscreen(object sender, RoutedEventArgs e) => FullscreenRequested?.Invoke();

    private void OnShowMain(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
            main.RestoreFromOverlay();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        // Main window hides instead of quitting. Closing overlay with main hidden
        // is the actual exit, otherwise lyrics would freeze with no UI left.
        if (Application.Current.MainWindow is MainWindow { IsVisible: false } main)
            main.QuitApp();
        else
            Close();
    }
}
