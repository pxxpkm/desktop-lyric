using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Windows.Media.Control;
using DesktopLyric.Services;

namespace DesktopLyric.Views;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DispatcherTimer _pollTimer;
    private DispatcherTimer _syncTimer;

    private readonly LyricsService _lyrics = new();
    private AppSettings _settings;

    private string _lastTitle = "";
    private List<LrcLine> _lines = new();

    // time tracking
    private readonly Stopwatch _sw = new();
    private TimeSpan _basePos = TimeSpan.Zero;
    private bool _isPlaying;
    private OverlayWindow? _overlay;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        Loaded += OnLoaded;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => PollNowPlaying();

        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _syncTimer.Tick += (_, _) => SyncLyrics();

        ApplySettings();
    }

    private void ApplySettings()
    {
        TxtCurrent.FontWeight = _settings.BoldLyrics
            ? FontWeights.Bold : FontWeights.Normal;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _session = _mgr.GetCurrentSession();

            if (_session != null)
            {
                TxtStatus.Text = "connected: " + (_session.SourceAppUserModelId ?? "?");
                PollNowPlaying();
                _pollTimer.Start();
                _syncTimer.Start();
            }
            else
            {
                TxtStatus.Text = "no media session";
            }

            _mgr.CurrentSessionChanged += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _session = _mgr.GetCurrentSession();
                    if (_session != null)
                    {
                        TxtStatus.Text = "connected: " + (_session.SourceAppUserModelId ?? "?");
                        _pollTimer.Start();
                        _syncTimer.Start();
                    }
                });
            };
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "smtc error: " + ex.Message;
        }
    }

    private async void PollNowPlaying()
    {
        if (_session == null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props == null) return;
            var title = props.Title ?? "";
            var artist = props.Artist ?? "";

            if (!string.IsNullOrEmpty(title))
            {
                TxtTitle.Text = title;
                TxtArtist.Text = artist;

                if (title != _lastTitle)
                {
                    _lastTitle = title;
                    _lyrics.Cancel();
                    TxtCurrent.Text = "searching...";
                    TxtTrans.Text = "";
                    TxtPrev.Text = "";
                    TxtNext.Text = "";

                    var result = await _lyrics.SearchAsync(title, artist);
                    if (result != null && result.Count > 0)
                    {
                        _lines = result;
                        TxtCurrent.Text = "♪";
                    }
                    else
                    {
                        _lines = new();
                        TxtCurrent.Text = "no lyrics found";
                    }
                }
            }

            // playback position
            var info = _session.GetPlaybackInfo();
            _isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            if (_isPlaying)
            {
                var tl = _session.GetTimelineProperties();
                _basePos = tl.Position;
                _sw.Restart();
            }
            else
            {
                _sw.Stop();
            }
        }
        catch { }
    }

    private void SyncLyrics()
    {
        if (_lines.Count == 0 || !_isPlaying) return;

        var pos = _basePos + _sw.Elapsed;
        TxtTime.Text = $"{(int)pos.TotalMinutes}:{pos.Seconds:D2}";

        int idx = -1;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Time <= pos) { idx = i; break; }
        }

        if (idx >= 0)
        {
            var text = _lines[idx].Text;
            var trans = _lines[idx].TranslatedText;
            if (_settings.ForceTraditional)
            {
                text = S2TConverter.Convert(text);
                if (trans != null) trans = S2TConverter.Convert(trans);
            }

            TxtCurrent.Text = text;
            TxtTrans.Text = _settings.HideTranslation ? "" : (trans ?? "");
            TxtPrev.Text = idx > 0 ? _lines[idx - 1].Text : "";
            TxtNext.Text = idx < _lines.Count - 1 ? _lines[idx + 1].Text : "";

            _overlay?.UpdateLyrics(text,
                _settings.HideTranslation ? null : trans,
                idx < _lines.Count - 1 ? _lines[idx + 1].Text : null,
                _lines[idx].WordTimings,
                (pos - _lines[idx].Time).TotalMilliseconds);
        }
    }

    private void ToggleOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (_overlay == null || !_overlay.IsVisible)
        {
            _overlay = new OverlayWindow();
            _overlay.Opacity = _settings.OverlayOpacity / 100.0;
            _overlay.Show();
        }
        else
        {
            _overlay.Close();
            _overlay = null;
        }
    }
}
