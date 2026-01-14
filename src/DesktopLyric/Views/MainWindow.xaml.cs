using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DesktopLyric.Views;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DispatcherTimer _timer;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private string _lastTitle = "";
    private List<LrcLine> _lines = new();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => PollNowPlaying();
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
                _timer.Start();
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
                        _timer.Start();
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
                    TxtLyrics.Text = "searching...";
                    var raw = await SearchNetease(title, artist);
                    if (raw != null)
                    {
                        _lines = ParseLrc(raw);
                        // just show all lines for now, no time sync yet
                        TxtLyrics.Text = string.Join("\n", _lines.Select(l => l.Text));
                    }
                    else
                    {
                        _lines = new();
                        TxtLyrics.Text = "no lyrics found";
                    }
                }
            }
        }
        catch { }
    }

    private async Task<string?> SearchNetease(string title, string artist)
    {
        try
        {
            var q = Uri.EscapeDataString(title + " " + artist);
            var url = "https://music.163.com/api/search/get?s=" + q + "&type=1&limit=5";
            using var req = new HttpRequestMessage(HttpMethod.Post, url);
            req.Content = new StringContent("", Encoding.UTF8, "application/x-www-form-urlencoded");
            req.Headers.Referrer = new Uri("https://music.163.com");

            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            if (!root.TryGetProperty("result", out var result)) return null;
            if (!result.TryGetProperty("songs", out var songs)) return null;
            if (songs.GetArrayLength() == 0) return null;

            var songId = songs[0].GetProperty("id").GetInt64();

            var lUrl = "https://music.163.com/api/song/lyric?id=" + songId + "&lv=1";
            using var lReq = new HttpRequestMessage(HttpMethod.Get, lUrl);
            lReq.Headers.Referrer = new Uri("https://music.163.com");
            var lResp = await _http.SendAsync(lReq);
            if (!lResp.IsSuccessStatusCode) return null;

            using var lDoc = JsonDocument.Parse(await lResp.Content.ReadAsStringAsync());
            if (!lDoc.RootElement.TryGetProperty("lrc", out var lrc)) return null;
            if (!lrc.TryGetProperty("lyric", out var lyricEl)) return null;

            return lyricEl.GetString();
        }
        catch
        {
            return null;
        }
    }

    // [mm:ss.xx] text
    private static readonly Regex LrcRegex = new(@"\[(\d+):(\d+)\.(\d+)\](.*)");

    private static List<LrcLine> ParseLrc(string raw)
    {
        var lines = new List<LrcLine>();
        foreach (var line in raw.Split('\n'))
        {
            var m = LrcRegex.Match(line);
            if (m.Success)
            {
                var min = int.Parse(m.Groups[1].Value);
                var sec = int.Parse(m.Groups[2].Value);
                var ms = int.Parse(m.Groups[3].Value) * 10; // xx is centiseconds
                var text = m.Groups[4].Value.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var time = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(ms);
                lines.Add(new LrcLine(time, text));
            }
        }
        return lines.OrderBy(l => l.Time).ToList();
    }
}

public record LrcLine(TimeSpan Time, string Text);
