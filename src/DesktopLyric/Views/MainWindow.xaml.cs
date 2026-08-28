using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using DesktopLyric.Services;

namespace DesktopLyric.Views;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DispatcherTimer _pollTimer;
    private DispatcherTimer _syncTimer;

    private readonly LyricsService _lyrics = new();
    private static readonly System.Net.Http.HttpClient _romajiHttp = new() { Timeout = TimeSpan.FromSeconds(3) };

    private string _lastTitle = "";
    private string _lastArtist = "";
    private int _trackOffsetMs;
    private double _trackRate = 1.0;
    private Dictionary<string, int> _lineShifts = new();
    private Dictionary<string, int> _lineHolds = new();
    private Dictionary<string, string> _lineTexts = new();
    private Dictionary<string, string> _lineTrans = new();
    private List<AddedLyric> _addedLines = new();
    private HoldRepeat? _offsetHold;
    private TimingEditorWindow? _timingEditor;
    private DispatcherTimer? _offsetSave;
    private List<LrcLine> _lines = new();
    private string _lastRomajiInput = "";
    private string _lastRomajiOutput = ""; // cache so we don't hit google every 100ms

    // Many players freeze SMTC Position while playing and only update it on pause/seek.
    // Polling that stale value used to rewind the interpolator every 2s.
    private readonly PlaybackClock _clock = new();
    private OverlayWindow? _overlay;
    private FullscreenWindow? _fullscreen;
    private ImageSource? _albumArt;
    private string _lastArtKey = "";
    private bool _overlayHiddenForFullscreen;
    private AppSettings _settings;
    private bool _forceClose;
    private int _karaokeLineIdx = -1;
    private List<KaraokeWordTiming>? _karaokeWords;
    private List<KaraokeWordTiming>? _karaokeSrc;
    private List<LrcLine>? _shown;
    private string _romajiInFlight = "";
    private int _pollGen;
    private int _artGen;
    private bool _clockQueued;
    private bool _pollQueued;
    private TimeSpan? _trackDuration;
    private GlobalSystemMediaTransportControlsSessionPlaybackInfo? _heldPlayback;
    private GlobalSystemMediaTransportControlsSessionTimelineProperties? _heldTimeline;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        Loaded += OnLoaded;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) =>
        {
            try { PollNowPlaying(); }
            catch (Exception ex) { ErrorLog.Write(ex); }
        };

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _syncTimer.Tick += (_, _) =>
        {
            try { SyncLyrics(); }
            catch (Exception ex) { ErrorLog.Write(ex); }
        };

        ApplySettings();
        ApplyTradButton();
        ApplyFontButton();
        WireTray();
        _offsetHold = new HoldRepeat(NudgeOffset);
    }

    private void WireTray()
    {
        if (Application.Current is not App { Tray: { } tray }) return;
        tray.ShowMainRequested += RestoreFromOverlay;
        tray.ShowOverlayRequested += ShowOverlay;
        tray.SettingsRequested += OpenSettings;
        tray.ExitRequested += QuitApp;
    }

    private void ApplySettings()
    {
        FontFamily = LyricFonts.FromSettings(_settings.FontFamily);
        try
        {
            var b = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(_settings.AccentColor));
            b.Freeze();
            TxtCurrent.AccentBrush = b;
            TxtCurrent.Foreground = b;
            TxtCurrent.SettingsFont = _settings.FontFamily ?? "";
            TxtTrans.SettingsFont = _settings.FontFamily ?? "";
        }
        catch { }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_forceClose)
            {
                WinRtLifetime.Suppress(_mgr);
                _mgr = null;
                return;
            }
            BindSession(_mgr.GetCurrentSession());
            ApplySessionUi();
            _mgr.CurrentSessionChanged += OnCurrentSessionChanged;
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "smtc error: " + ex.Message;
        }

        ShowOverlay();
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, object args)
    {
        WinRtLifetime.Suppress(args);
        Dispatcher.BeginInvoke(() =>
        {
            if (_forceClose || _mgr == null) return;
            try
            {
                BindSession(_mgr.GetCurrentSession());
                ApplySessionUi();
            }
            catch (Exception ex) { ErrorLog.Write(ex); }
        });
    }

    private void ApplySessionUi()
    {
        if (_session != null)
        {
            TxtStatus.Text = "connected";
            StatusDot.Fill = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x00, 0xd4, 0xff));
            PollNowPlaying();
            _pollTimer.Start();
            _syncTimer.Start();
        }
        else
        {
            TxtStatus.Text = "no media session — play something";
        }
    }

    private async void PollNowPlaying()
    {
        if (_session == null || _forceClose) return;
        var gen = ++_pollGen;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (gen != _pollGen || _forceClose)
            {
                WinRtLifetime.Suppress(props);
                return;
            }
            if (props == null) return;
            try
            {
                var title = props.Title ?? "";
                var artist = props.Artist ?? "";

                if (!string.IsNullOrEmpty(title))
                {
                    TxtTitle.Text = ToDisplay(title);
                    TxtArtist.Text = ToDisplay(artist);

                    var artKey = title + "\n" + artist;
                    if (artKey != _lastArtKey || _albumArt == null)
                    {
                        await LoadAlbumArt(props);
                        if (_forceClose) return;
                        _lastArtKey = artKey;
                    }
                    _overlay?.SetTrackInfo(ToDisplay(title), ToDisplay(artist));
                    _fullscreen?.SetTrackInfo(ToDisplay(title), ToDisplay(artist));

                    var titleChanged = title != _lastTitle;
                    var artistFilled = !titleChanged
                        && string.IsNullOrEmpty(_lastArtist)
                        && !string.IsNullOrEmpty(artist);
                    if (titleChanged) _trackDuration = null;
                    RefreshClock();
                    if (titleChanged || artistFilled)
                    {
                        _lastTitle = title;
                        _lastArtist = artist;
                        LoadTrackOffset();
                        _lyrics.Cancel();
                        if (titleChanged)
                        {
                            TxtCurrent.Text = "searching...";
                            TxtTrans.Text = "";
                            TxtPrev.Text = "";
                            TxtNext.Text = "";
                        }

                        var requestedTitle = title;
                        var result = await _lyrics.SearchAsync(title, artist, GetTrackDuration());
                        if (_forceClose || _lastTitle != requestedTitle) return;
                        if (result == null) return; // cancelled (user picked another candidate, or a newer search)
                        if (result.Count > 0)
                        {
                            SetLines(result);
                            TxtCurrent.Text = "♪";
                        }
                        else
                        {
                            SetLines([]);
                            TxtCurrent.Text = "no lyrics found";
                        }
                    }
                }
                else
                    RefreshClock();
            }
            finally { WinRtLifetime.Suppress(props); }
        }
        catch { }
    }

    private void BindSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session != null && session != null)
        {
            try
            {
                if (_session.SourceAppUserModelId == session.SourceAppUserModelId)
                {
                    WinRtLifetime.Suppress(session);
                    return;
                }
            }
            catch { }
        }

        if (_session != null)
        {
            try
            {
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            }
            catch { }
            WinRtLifetime.Suppress(_session);
        }

        DropHeldSmtc();
        _session = session;
        if (_session == null) return;

        _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        _session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
        _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        WinRtLifetime.Suppress(args);
        QueueRefreshClock();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        WinRtLifetime.Suppress(args);
        QueueRefreshClock();
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, object args)
    {
        WinRtLifetime.Suppress(args);
        QueuePollNowPlaying();
    }

    private void QueueRefreshClock()
    {
        if (_clockQueued || _forceClose) return;
        _clockQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _clockQueued = false;
            if (_forceClose || _session == null) return;
            RefreshClock();
        });
    }

    private void QueuePollNowPlaying()
    {
        if (_pollQueued || _forceClose) return;
        _pollQueued = true;
        Dispatcher.BeginInvoke(() =>
        {
            _pollQueued = false;
            if (_forceClose || _session == null) return;
            PollNowPlaying();
        });
    }

    private void RefreshClock()
    {
        if (_session == null || _forceClose) return;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo? info = null;
        GlobalSystemMediaTransportControlsSessionTimelineProperties? tl = null;
        try
        {
            info = _session.GetPlaybackInfo();
            tl = _session.GetTimelineProperties();
            var playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var rate = info.PlaybackRate is > 0 and var r ? r : 1.0;
            _clock.Apply(tl.Position, playing, rate);
            var dur = tl.EndTime - tl.StartTime;
            _trackDuration = dur >= TimeSpan.FromSeconds(12) ? dur : null;
            if (!playing)
                SyncLyrics();
        }
        catch { }
        finally
        {
            HoldSmtc(ref _heldPlayback, info);
            HoldSmtc(ref _heldTimeline, tl);
            GC.KeepAlive(_session);
        }
    }

    private static void HoldSmtc<T>(ref T? slot, T? next) where T : class
    {
        var prev = slot;
        slot = next;
        if (!ReferenceEquals(prev, next))
            WinRtLifetime.Suppress(prev);
    }

    private void DropHeldSmtc()
    {
        WinRtLifetime.Suppress(_heldPlayback);
        WinRtLifetime.Suppress(_heldTimeline);
        _heldPlayback = null;
        _heldTimeline = null;
        _trackDuration = null;
    }

    private async Task LoadAlbumArt(GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        IRandomAccessStreamReference? thumb = null;
        IRandomAccessStream? stream = null;
        try
        {
            thumb = props.Thumbnail;
            if (thumb == null)
            {
                _albumArt = null;
                AlbumArt.Source = null;
                _fullscreen?.SetAlbumArt(null);
                return;
            }
            var artGen = ++_artGen;
            stream = await thumb.OpenReadAsync();
            using var ms = new MemoryStream();
            var transferred = false;
            try
            {
                // AsStreamForRead takes ownership and Closes the WinRT stream.
                // A second Dispose heap-corrupts ntdll (0xc0000374).
                using (var inp = stream.AsStreamForRead())
                {
                    transferred = true;
                    await inp.CopyToAsync(ms);
                }
            }
            finally
            {
                if (transferred)
                {
                    WinRtLifetime.Suppress(stream);
                    stream = null;
                }
            }
            ms.Position = 0;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            _ = Dispatcher.BeginInvoke(() =>
            {
                if (_forceClose || !IsLoaded || artGen != _artGen) return;
                _albumArt = bmp;
                AlbumArt.Source = bmp;
                if (WindowGuard.CanTouch(_fullscreen))
                    _fullscreen!.SetAlbumArt(bmp);
            });
        }
        catch { }
        finally
        {
            if (stream != null)
                WinRtLifetime.Release(stream);
            WinRtLifetime.Suppress(thumb);
        }
    }

    private void SafeUpdateLyrics(string current, string? translated, string? next,
        List<KaraokeWordTiming>? words, double elapsed)
    {
        try
        {
            if (WindowGuard.CanTouch(_overlay))
                _overlay!.UpdateLyrics(current, translated, next, words, elapsed);
            if (WindowGuard.CanTouch(_fullscreen))
                _fullscreen!.UpdateLyrics(current, translated, next, words, elapsed);
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void SyncLyrics()
    {
        if (_lines == null || _lines.Count == 0)
        {
            SafeUpdateLyrics("", null, null, null, 0);
            return;
        }

        var pos = _clock.Position;
        var timeText = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2}";
        if (TxtTime.Text != timeText)
            TxtTime.Text = timeText;

        var lyricPos = LyricClockPos();
        var lines = ShownLines();

        int idx = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (LyricsService.LineIsActive(lines, i, lyricPos, _lineShifts, _lineHolds))
            {
                idx = i;
                break;
            }
        }

        if (idx < 0)
        {
            int reached = -1;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (LyricsService.TimeOf(lines[i], _lineShifts) > lyricPos) continue;
                if (string.IsNullOrWhiteSpace(lines[i].Text)) continue;
                if (LyricsService.IsAttachedTranslationLine(lines, i)) continue;
                reached = i;
                break;
            }
            var upcoming = NextLyricText(lines, reached < 0 ? -1 : reached);
            var lastSung = reached >= 0 ? ToDisplay(lines[reached].Text) : "";
            TxtCurrent.Text = "";
            TxtTrans.Text = "";
            TxtPrev.Text = lastSung ?? "";
            TxtNext.Text = upcoming ?? "";
            TxtRomaji.Text = "";
            SafeUpdateLyrics("", null, upcoming, null, 0);
            return;
        }

        {
            var text = ToDisplay(lines[idx].Text);
            var trans = ToDisplay(LyricsService.ResolvedTranslation(lines, lines[idx]));
            var prevIdx = LyricsService.PrevSungIndex(lines, idx);
            var prev = prevIdx >= 0 ? ToDisplay(lines[prevIdx].Text) : "";
            var next = NextLyricText(lines, idx) ?? "";
            var words = KaraokeWordsForLine(lines, idx);

            TxtCurrent.Text = text;
            TxtTrans.Text = _settings.HideTranslation ? "" : (trans ?? "");
            TxtPrev.Text = prev;
            TxtNext.Text = next;
            ApplyLineFonts(text, trans, prev, next);

            var elapsed = (lyricPos - LyricsService.TimeOf(lines[idx], _lineShifts)).TotalMilliseconds;
            if (elapsed < 0) elapsed = 0;
            SafeUpdateLyrics(text,
                _settings.HideTranslation ? null : trans,
                string.IsNullOrEmpty(next) ? null : next,
                words,
                elapsed);

            if (_settings.ShowRomaji && LyricFonts.HasKana(text))
                UpdateRomaji(text);
            else
                TxtRomaji.Text = "";
        }
    }

    private List<LrcLine> ShownLines()
        => _shown ??= LyricsService.ApplyEdits(_lines, CurrentTiming());

    private void SetLines(List<LrcLine> lines)
    {
        _lines = lines ?? [];
        InvalidateLyricCache();
    }

    private void InvalidateLyricCache()
    {
        _shown = null;
        ResetKaraokeCache();
    }

    private string? NextLyricText(IReadOnlyList<LrcLine> lines, int afterIdx)
    {
        var i = LyricsService.NextSungIndex(lines, afterIdx);
        return i >= 0 ? ToDisplay(lines[i].Text) : null;
    }

    private TimeSpan LyricClockPos()
    {
        var rate = _trackRate <= 0 || double.IsNaN(_trackRate) ? 1.0 : _trackRate;
        var ms = (_clock.Position.TotalMilliseconds + _settings.GlobalOffsetMs + _trackOffsetMs) * rate;
        if (ms < 0) ms = 0;
        return TimeSpan.FromMilliseconds(ms);
    }

    private void UpdateRomaji(string text)
    {
        if (text == _lastRomajiInput)
        {
            TxtRomaji.Text = _lastRomajiOutput;
            return;
        }

        if (text == _romajiInFlight) return;
        _romajiInFlight = text;
        var input = text;
        _ = Task.Run(async () =>
        {
            try
            {
                var q = Uri.EscapeDataString(input);
                var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=ja&tl=en&dt=rm&q={q}";
                var resp = await _romajiHttp.GetAsync(url);
                if (!resp.IsSuccessStatusCode) return;
                var json = await resp.Content.ReadAsStringAsync();
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.GetArrayLength() == 0) return;

                var sb = new System.Text.StringBuilder();
                var first = root[0];
                if (first.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var seg in first.EnumerateArray())
                    {
                        if (seg.ValueKind == System.Text.Json.JsonValueKind.Array && seg.GetArrayLength() > 3)
                        {
                            var rm = seg[3].GetString();
                            if (!string.IsNullOrEmpty(rm)) sb.Append(rm);
                        }
                    }
                }
                var romaji = sb.ToString().Trim();
                if (!string.IsNullOrEmpty(romaji))
                {
                    await Dispatcher.BeginInvoke(() =>
                    {
                        _lastRomajiInput = input;
                        _lastRomajiOutput = romaji;
                        TxtRomaji.Text = romaji;
                    });
                }
            }
            catch { }
        });
    }

    private void ApplyLineFonts(string text, string? trans, string prev, string next)
    {
        var custom = _settings.FontFamily ?? "";
        TxtCurrent.SettingsFont = custom;
        TxtTrans.SettingsFont = custom;
        TxtCurrent.FontSize = Math.Clamp(
            LyricFonts.LineSize(text, current: true) * _settings.OverlayOriginalScale, 10, 48);
        TxtTrans.FontSize = Math.Clamp(
            LyricFonts.LineSize(trans, current: false) * _settings.OverlayTranslationScale, 10, 48);
        TxtPrev.FontFamily = LyricFonts.HasKana(prev) ? LyricFonts.Japanese : LyricFonts.FromSettings(custom);
        TxtNext.FontFamily = LyricFonts.HasKana(next) ? LyricFonts.Japanese : LyricFonts.FromSettings(custom);
    }

    private string ToDisplay(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        if (!_settings.ForceTraditional) return text;
        return S2TConverter.Convert(text);
    }

    private void ResetKaraokeCache()
    {
        _karaokeLineIdx = -1;
        _karaokeWords = null;
        _karaokeSrc = null;
    }

    private List<KaraokeWordTiming>? KaraokeWordsForLine(IReadOnlyList<LrcLine> lines, int idx)
    {
        var src = lines[idx].WordTimings;
        if (idx == _karaokeLineIdx && ReferenceEquals(src, _karaokeSrc))
            return _karaokeWords;
        _karaokeLineIdx = idx;
        _karaokeSrc = src;
        _karaokeWords = ToDisplay(src);
        return _karaokeWords;
    }

    private List<KaraokeWordTiming>? ToDisplay(List<KaraokeWordTiming>? words)
    {
        if (words == null || words.Count == 0 || !_settings.ForceTraditional) return words;
        var converted = new List<KaraokeWordTiming>(words.Count);
        foreach (var w in words)
            converted.Add(w with { Text = S2TConverter.Convert(w.Text) });
        return converted;
    }

    private TimeSpan? GetTrackDuration() => _trackDuration;

    // --- UI handlers ---

    private void Window_Drag(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton != System.Windows.Input.MouseButton.Left) return;
        try { DragMove(); } catch { }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => HideToOverlay();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose)
        {
            e.Cancel = true;
            HideToOverlay();
            return;
        }
        StopSmtc();
        FlushOffsetSave();
        _offsetHold?.Dispose();
        base.OnClosing(e);
    }

    private void HideToOverlay()
    {
        FlushOffsetSave();
        if (_fullscreen is not { IsVisible: true })
            ShowOverlay();
        ShowInTaskbar = false;
        Hide();
    }

    public void RestoreFromOverlay()
    {
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    public void QuitApp()
    {
        if (_forceClose) return;
        _forceClose = true;
        StopSmtc();
        _offsetHold?.Dispose();
        _offsetHold = null;
        FlushOffsetSave();
        Application.Current.Shutdown();
    }

    private void StopSmtc()
    {
        _pollTimer.Stop();
        _syncTimer.Stop();
        if (_mgr != null)
        {
            try { _mgr.CurrentSessionChanged -= OnCurrentSessionChanged; }
            catch { }
        }
        BindSession(null);
        WinRtLifetime.Suppress(_mgr);
        _mgr = null;
    }

    private void OpenSettings() => Settings_Click(this, new RoutedEventArgs());

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var win = new SettingsWindow(_settings);
            if (IsVisible) win.Owner = this;
            win.Topmost = true;
            win.Changed += ApplySettingsLive;
            try { win.ShowDialog(); }
            finally { win.Changed -= ApplySettingsLive; }
        }
        catch (Exception ex)
        {
            ErrorLog.Write(ex);
        }
    }

    private void ApplySettingsLive()
    {
        try
        {
            ApplySettings();
            ApplyTradButton();
            ApplyFontButton();
            ApplyLineFonts(TxtCurrent.Text, TxtTrans.Text, TxtPrev.Text, TxtNext.Text);
            _overlay?.RefreshAppearance();
            _fullscreen?.RefreshAppearance();
            SyncLyrics();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private async void PickSong_Click(object sender, RoutedEventArgs e)
        => await PickSongAsync(this);

    private async Task PickSongAsync(Window? owner)
    {
        var win = new PickSongWindow(_lyrics, _lastTitle, _lastArtist, GetTrackDuration());
        if (owner is { IsVisible: true })
            win.Owner = owner;
        else if (_overlay is { IsVisible: true })
            win.Owner = _overlay;
        win.Topmost = _overlay is { IsVisible: true } || _fullscreen is { IsVisible: true };
        if (win.ShowDialog() != true || win.Chosen == null) return;
        TxtCurrent.Text = "loading...";
        if (win.Remember)
        {
            // Save before fetch so a cancelled download still remembers the pick.
            LyricChoiceStore.Set(_lastTitle, _lastArtist, win.Chosen.Key);
            LyricChoiceStore.Set(win.SearchTitle, win.SearchArtist, win.Chosen.Key);
            LyricChoiceStore.Set(win.Chosen.Title, win.Chosen.Artist, win.Chosen.Key);
        }
        var lines = await _lyrics.FetchAsync(win.Chosen);
        if (lines == null || lines.Count == 0)
        {
            // Fetch raced with auto-search; the pin is saved, so retry via that.
            lines = await _lyrics.SearchAsync(_lastTitle, _lastArtist, GetTrackDuration());
        }
        if (lines == null) return;
        if (lines.Count > 0)
        {
            SetLines(lines);
            TxtCurrent.Text = "♪";
            TxtStatus.Text = $"歌詞：{win.Chosen.Title} · {win.Chosen.Source}";
        }
        else
        {
            TxtCurrent.Text = "no lyrics found";
            TxtStatus.Text = win.Remember ? "已記住，但呢首暫時撈唔到歌詞" : "呢首冇歌詞";
        }
    }

    private async void SavedSongs_Click(object sender, RoutedEventArgs e)
    {
        var win = new SavedSongsWindow(_lyrics) { Owner = this };
        win.ShowDialog();
        if (!win.Dirty || string.IsNullOrEmpty(_lastTitle)) return;
        var result = await _lyrics.SearchAsync(_lastTitle, _lastArtist, GetTrackDuration());
        if (result == null) return;
        if (result.Count > 0)
        {
            SetLines(result);
            TxtCurrent.Text = "♪";
        }
    }

    private void CycleFont_Click(object sender, RoutedEventArgs e)
    {
        _settings.FontFamily = LyricFonts.CycleChinese(_settings.FontFamily);
        _settings.Save();
        ApplyFontButton();
        FontFamily = LyricFonts.FromSettings(_settings.FontFamily);
        ApplyLineFonts(TxtCurrent.Text, TxtTrans.Text, TxtPrev.Text, TxtNext.Text);
        _overlay?.RefreshFonts();
        _fullscreen?.RefreshFonts();
    }

    private void ApplyFontButton()
    {
        BtnFont.Content = LyricFonts.CurrentLabel(_settings.FontFamily);
    }

    private void ToggleTraditional_Click(object sender, RoutedEventArgs e)
    {
        _settings.ForceTraditional = !_settings.ForceTraditional;
        _settings.Save();
        ApplyTradButton();
        _overlay?.RefreshTradButton();
        _fullscreen?.RefreshTradButton();
        ResetKaraokeCache();
    }

    private void ApplyTradButton()
    {
        BtnTrad.Foreground = new System.Windows.Media.SolidColorBrush(
            _settings.ForceTraditional
                ? System.Windows.Media.Color.FromRgb(0x00, 0xd4, 0xff)
                : System.Windows.Media.Color.FromRgb(0xa0, 0xb0, 0xc0));
    }

    private void ShowOverlay()
    {
        if (_overlay != null)
        {
            if (!_overlay.IsVisible) _overlay.Show();
            return;
        }
        _overlay = new OverlayWindow(_settings);
        _overlay.Opacity = _settings.OverlayOpacity / 100.0;
        _overlay.SetTrackInfo(ToDisplay(_lastTitle), ToDisplay(TxtArtist.Text));
        _overlay.TraditionalToggled += ApplyTradButton;
        _overlay.OffsetNudged += NudgeOffset;
        _overlay.PickSongRequested += () => _ = PickSongAsync(_overlay);
        _overlay.FullscreenRequested += ShowFullscreen;
        _overlay.TimingEditorRequested += OpenTimingEditor;
        _overlay.Closed += (_, _) => _overlay = null;
        _overlay.Show();
        RefreshOffsetUi();
    }

    private void ShowFullscreen()
    {
        if (_fullscreen is { IsVisible: true })
        {
            _fullscreen.Activate();
            return;
        }
        if (_overlay is { IsVisible: true })
        {
            _overlayHiddenForFullscreen = true;
            _overlay.Hide();
        }
        else _overlayHiddenForFullscreen = false;

        _fullscreen = new FullscreenWindow(_settings);
        _fullscreen.TraditionalToggled += ApplyTradButton;
        _fullscreen.PickSongRequested += () => _ = PickSongAsync(_fullscreen);
        _fullscreen.SetTrackInfo(ToDisplay(_lastTitle), ToDisplay(TxtArtist.Text));
        _fullscreen.SetAlbumArt(_albumArt);
        _fullscreen.Closed += (_, _) =>
        {
            _fullscreen = null;
            if (_overlayHiddenForFullscreen)
            {
                _overlayHiddenForFullscreen = false;
                ShowOverlay();
            }
        };
        _fullscreen.Show();
        SyncLyrics();
    }

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ShowFullscreen();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11)
        {
            e.Handled = true;
            if (_fullscreen is { IsVisible: true }) _fullscreen.Close();
            else ShowFullscreen();
        }
    }

    private void LoadTrackOffset()
    {
        FlushOffsetSave();
        var t = LyricOffsetStore.GetTiming(_lastTitle, _lastArtist);
        ApplyTimingState(t);
        RefreshOffsetUi();
    }

    private void OpenTimingEditor()
    {
        if (_timingEditor != null)
        {
            _timingEditor.Activate();
            return;
        }
        try
        {
            _timingEditor = new TimingEditorWindow(
                () => _lastTitle,
                () => _lastArtist,
                () => _clock.Position,
                ShownLines,
                () => _lines,
                LyricClockPos,
                () => _settings.GlobalOffsetMs,
                () => CurrentTiming(),
                ApplyTiming)
            {
                Topmost = true,
            };
            if (IsVisible) _timingEditor.Owner = this;
            _timingEditor.Closed += (_, _) => _timingEditor = null;
            _timingEditor.Show();
        }
        catch (Exception ex)
        {
            _timingEditor = null;
            ErrorLog.Write(ex);
        }
    }

    private TrackTiming CurrentTiming()
        => new TrackTiming(
            _trackOffsetMs,
            _trackRate,
            _lineShifts.Count == 0 ? null : new Dictionary<string, int>(_lineShifts),
            _lineHolds.Count == 0 ? null : new Dictionary<string, int>(_lineHolds),
            _lineTexts.Count == 0 ? null : new Dictionary<string, string>(_lineTexts),
            _addedLines.Count == 0 ? null : [.. _addedLines],
            _lineTrans.Count == 0 ? null : new Dictionary<string, string>(_lineTrans));

    private void ApplyTimingState(TrackTiming timing)
    {
        timing = timing.Clamped();
        _trackOffsetMs = timing.OffsetMs;
        _trackRate = timing.Rate;
        _lineShifts = timing.Lines is { Count: > 0 } ? new Dictionary<string, int>(timing.Lines) : new();
        _lineHolds = timing.Holds is { Count: > 0 } ? new Dictionary<string, int>(timing.Holds) : new();
        _lineTexts = timing.Texts is { Count: > 0 } ? new Dictionary<string, string>(timing.Texts) : new();
        _addedLines = timing.Added is { Count: > 0 } ? [.. timing.Added] : [];
        _lineTrans = timing.Trans is { Count: > 0 } ? new Dictionary<string, string>(timing.Trans) : new();
        InvalidateLyricCache();
    }

    private void ApplyTiming(TrackTiming timing)
    {
        ApplyTimingState(timing);
        ScheduleOffsetSave();
        RefreshOffsetUi();
        SyncLyrics();
    }

    private void TimingEditor_Click(object sender, RoutedEventArgs e) => OpenTimingEditor();

    private void OffsetEarlier_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _offsetHold?.Down(1, sender as IInputElement);
    }

    private void OffsetLater_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _offsetHold?.Down(-1, sender as IInputElement);
    }

    private void OffsetHold_Up(object sender, MouseEventArgs e)
    {
        _offsetHold?.Up();
        FlushOffsetSave();
    }

    private void OffsetReset_Click(object sender, RoutedEventArgs e)
        => NudgeOffset(int.MinValue);

    private void NudgeOffset(int delta)
    {
        if (string.IsNullOrEmpty(_lastTitle)) return;
        if (delta == int.MinValue)
        {
            _trackOffsetMs = 0;
            _trackRate = 1.0;
            _lineShifts.Clear();
        }
        else
            _trackOffsetMs = Math.Clamp(_trackOffsetMs + delta, LyricOffsetStore.MinMs, LyricOffsetStore.MaxMs);
        RefreshOffsetUi();
        SyncLyrics();
        ScheduleOffsetSave();
    }

    private void ScheduleOffsetSave()
    {
        _offsetSave ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _offsetSave.Tick -= SaveOffsetNow;
        _offsetSave.Tick += SaveOffsetNow;
        _offsetSave.Stop();
        _offsetSave.Start();
    }

    private void SaveOffsetNow(object? sender, EventArgs e)
    {
        _offsetSave?.Stop();
        if (!string.IsNullOrEmpty(_lastTitle))
            LyricOffsetStore.SetTiming(_lastTitle, _lastArtist, CurrentTiming());
    }

    private void FlushOffsetSave()
    {
        if (_offsetSave is { IsEnabled: true })
            SaveOffsetNow(null, EventArgs.Empty);
    }

    private void RefreshOffsetUi()
    {
        var label = LyricOffsetStore.FormatLabel(_trackOffsetMs, _trackRate);
        BtnOffset.Content = label;
        var custom = _trackOffsetMs != 0
            || Math.Abs(_trackRate - 1.0) >= 0.0005
            || _lineShifts.Count > 0
            || _lineHolds.Count > 0
            || _lineTexts.Count > 0
            || _lineTrans.Count > 0
            || _addedLines.Count > 0;
        BtnOffset.Foreground = new System.Windows.Media.SolidColorBrush(
            custom
                ? System.Windows.Media.Color.FromRgb(0x00, 0xd4, 0xff)
                : System.Windows.Media.Color.FromRgb(0xa0, 0xb0, 0xc0));
        _overlay?.SetOffsetLabel(_trackOffsetMs, _trackRate, custom);
    }

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay == null || !_overlay.IsVisible)
            ShowOverlay();
        else
        {
            _overlay.Close();
            _overlay = null;
        }
    }

    private void ExportLrc_Click(object sender, RoutedEventArgs e)
    {
        if (_lines.Count == 0)
        {
            MessageBox.Show("no lyrics loaded", "export");
            return;
        }

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "LRC files|*.lrc",
            FileName = $"{_lastTitle}.lrc"
        };

        if (dlg.ShowDialog() == true)
        {
            try
            {
                var shown = ShownLines();
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[ti:{_lastTitle}]");
                sb.AppendLine("[by:desktop-lyric]");
                sb.AppendLine();
                sb.Append(LyricsService.FormatShownLrc(shown, CurrentTiming()));
                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show($"saved {shown.Count} lines", "export");
            }
            catch (Exception ex)
            {
                MessageBox.Show("export failed: " + ex.Message, "error");
            }
        }
    }
}
