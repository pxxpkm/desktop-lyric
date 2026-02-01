using System.Diagnostics;
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
    private DispatcherTimer _pollTimer;
    private DispatcherTimer _syncTimer;
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private string _lastTitle = "";
    private List<LrcLine> _lines = new();
    private int _searchGen; // cancel stale searches

    // time tracking — use stopwatch between smtc polls
    private readonly Stopwatch _sw = new();
    private TimeSpan _basePos = TimeSpan.Zero;
    private bool _isPlaying;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => PollNowPlaying();

        // sync lyrics display faster
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _syncTimer.Tick += (_, _) => SyncLyrics();
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
                    var gen = ++_searchGen;
                    TxtCurrent.Text = "searching...";
                    TxtTrans.Text = "";
                    TxtPrev.Text = "";
                    TxtNext.Text = "";

                    // try netease first, then qq, then lrclib
                    var result = await SearchNetease(title, artist);
                    if (gen != _searchGen) return;

                    if (result == null || result.Count == 0)
                    {
                        result = await SearchQQ(title, artist);
                        if (gen != _searchGen) return;
                    }

                    if (result == null || result.Count == 0)
                    {
                        result = await SearchLrcLib(title, artist);
                        if (gen != _searchGen) return;
                    }

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

            // read playback position
            var info = _session.GetPlaybackInfo();
            _isPlaying = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            if (_isPlaying)
            {
                var tl = _session.GetTimelineProperties();
                var pos = tl.Position;
                _basePos = pos;
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

        // find current line
        int idx = -1;
        for (int i = _lines.Count - 1; i >= 0; i--)
        {
            if (_lines[i].Time <= pos)
            {
                idx = i;
                break;
            }
        }

        if (idx >= 0)
        {
            TxtCurrent.Text = _lines[idx].Text;
            TxtTrans.Text = _lines[idx].TranslatedText ?? "";
            TxtPrev.Text = idx > 0 ? _lines[idx - 1].Text : "";
            TxtNext.Text = idx < _lines.Count - 1 ? _lines[idx + 1].Text : "";
        }
    }

    private async Task<List<LrcLine>?> SearchNetease(string title, string artist)
    {
        try
        {
            var q = Uri.EscapeDataString(title + " " + artist);
            var url = "https://music.163.com/api/search/get?s=" + q + "&type=1&limit=8";
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

            // pick best match instead of just first
            var songId = PickBestSong(songs, title, artist);
            if (songId < 0) return null;

            var lUrl = "https://music.163.com/api/song/lyric?id=" + songId + "&lv=1&tv=1";
            using var lReq = new HttpRequestMessage(HttpMethod.Get, lUrl);
            lReq.Headers.Referrer = new Uri("https://music.163.com");
            var lResp = await _http.SendAsync(lReq);
            if (!lResp.IsSuccessStatusCode) return null;

            using var lDoc = JsonDocument.Parse(await lResp.Content.ReadAsStringAsync());
            if (!lDoc.RootElement.TryGetProperty("lrc", out var lrc)) return null;
            if (!lrc.TryGetProperty("lyric", out var lyricEl)) return null;
            var lrcStr = lyricEl.GetString();
            if (string.IsNullOrEmpty(lrcStr)) return null;

            var lyrics = ParseLrc(lrcStr);

            // merge translation if available
            if (lDoc.RootElement.TryGetProperty("tlyric", out var tl) &&
                tl.TryGetProperty("lyric", out var tlEl))
            {
                var transStr = tlEl.GetString();
                if (!string.IsNullOrEmpty(transStr))
                {
                    var transLines = ParseLrc(transStr);
                    foreach (var t in transLines)
                    {
                        // find closest original line
                        var match = lyrics.MinBy(l => Math.Abs((l.Time - t.Time).Ticks));
                        if (match != null && Math.Abs((match.Time - t.Time).TotalMilliseconds) < 500)
                            match.TranslatedText = t.Text;
                    }
                }
            }

            return lyrics;
        }
        catch
        {
            return null;
        }
    }

    private static long PickBestSong(JsonElement songs, string title, string artist)
    {
        long bestId = -1;
        int bestScore = -1;
        var tLow = title.ToLowerInvariant().Trim();
        var aLow = artist.ToLowerInvariant().Trim();

        foreach (var song in songs.EnumerateArray())
        {
            var name = (song.GetProperty("name").GetString() ?? "").ToLowerInvariant();
            int score = 0;

            if (name == tLow) score += 100;
            else if (name.Contains(tLow) || tLow.Contains(name)) score += 50;

            if (song.TryGetProperty("artists", out var arts))
            {
                foreach (var a in arts.EnumerateArray())
                {
                    var an = (a.GetProperty("name").GetString() ?? "").ToLowerInvariant();
                    if (an == aLow || aLow.Contains(an) || an.Contains(aLow))
                    {
                        score += 30;
                        break;
                    }
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestId = song.GetProperty("id").GetInt64();
            }
        }

        return bestScore >= 20 ? bestId : -1;
    }

    // some lrc files use [mm:ss.xx], some use [mm:ss.xxx]
    private static readonly Regex LrcRegex = new(@"\[(\d+):(\d+)\.(\d{2,3})\](.*)");

    private async Task<List<LrcLine>?> SearchQQ(string title, string artist)
    {
        try
        {
            var query = (title + " " + artist).Replace("\"", "");
            var body = "{\"comm\":{\"ct\":19,\"cv\":1845},\"req\":{\"method\":\"DoSearchForQQMusicDesktop\",\"module\":\"music.search.SearchCgiService\",\"param\":{\"num_per_page\":8,\"page_num\":1,\"query\":\"" + query + "\",\"search_type\":0}}}";
            using var sReq = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg");
            sReq.Content = new StringContent(body, Encoding.UTF8, "application/json");
            sReq.Headers.Referrer = new Uri("https://y.qq.com");
            var sResp = await _http.SendAsync(sReq);
            if (!sResp.IsSuccessStatusCode) return null;

            using var sDoc = JsonDocument.Parse(await sResp.Content.ReadAsStringAsync());
            var list = sDoc.RootElement.GetProperty("req").GetProperty("data")
                .GetProperty("body").GetProperty("song").GetProperty("list");
            if (list.GetArrayLength() == 0) return null;

            // just grab first for now, matching is hard with qq's format
            var mid = list[0].GetProperty("mid").GetString();
            if (string.IsNullOrEmpty(mid)) return null;

            // fetch lyrics
            var lyricBody = "{\"comm\":{\"ct\":19,\"cv\":1845},\"req\":{\"method\":\"GetPlayLyricInfo\",\"module\":\"music.musichallSong.PlayLyricInfo\",\"param\":{\"songMID\":\"" + mid + "\",\"songID\":0}}}";
            using var lReq = new HttpRequestMessage(HttpMethod.Post, "https://u.y.qq.com/cgi-bin/musicu.fcg");
            lReq.Content = new StringContent(lyricBody, Encoding.UTF8, "application/json");
            lReq.Headers.Referrer = new Uri("https://y.qq.com");
            var lResp = await _http.SendAsync(lReq);
            if (!lResp.IsSuccessStatusCode) return null;

            using var lDoc = JsonDocument.Parse(await lResp.Content.ReadAsStringAsync());
            var data = lDoc.RootElement.GetProperty("req").GetProperty("data");
            var lyricB64 = data.GetProperty("lyric").GetString();
            if (string.IsNullOrEmpty(lyricB64)) return null;

            var lrcStr = Encoding.UTF8.GetString(Convert.FromBase64String(lyricB64));
            var lyrics = ParseLrc(lrcStr);
            if (lyrics.Count == 0) return null;

            // qq also has translation
            if (data.TryGetProperty("trans", out var transEl))
            {
                var transB64 = transEl.GetString();
                if (!string.IsNullOrEmpty(transB64))
                {
                    var transStr = Encoding.UTF8.GetString(Convert.FromBase64String(transB64));
                    var transLines = ParseLrc(transStr);
                    foreach (var t in transLines)
                    {
                        var match = lyrics.MinBy(l => Math.Abs((l.Time - t.Time).Ticks));
                        if (match != null && Math.Abs((match.Time - t.Time).TotalMilliseconds) < 500)
                            match.TranslatedText = t.Text;
                    }
                }
            }

            return lyrics;
        }
        catch { return null; }
    }

    private async Task<List<LrcLine>?> SearchLrcLib(string title, string artist)
    {
        try
        {
            var url = "https://lrclib.net/api/search?track_name=" + Uri.EscapeDataString(title);
            if (!string.IsNullOrEmpty(artist))
                url += "&artist_name=" + Uri.EscapeDataString(artist);

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("DesktopLyric/0.1");
            var resp = await _http.SendAsync(req);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            if (doc.RootElement.GetArrayLength() == 0) return null;

            // grab first with synced lyrics
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.TryGetProperty("syncedLyrics", out var sl) &&
                    sl.ValueKind == JsonValueKind.String)
                {
                    var lrcStr = sl.GetString();
                    if (!string.IsNullOrEmpty(lrcStr))
                    {
                        var lines = ParseLrc(lrcStr);
                        if (lines.Count > 0) return lines;
                    }
                }
            }
            return null;
        }
        catch { return null; }
    }

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
                var msRaw = m.Groups[3].Value;
                // if 2 digits, it's centiseconds; if 3, milliseconds
                var ms = msRaw.Length == 2 ? int.Parse(msRaw) * 10 : int.Parse(msRaw);
                var text = m.Groups[4].Value.Trim();
                if (string.IsNullOrEmpty(text)) continue;
                var time = TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec) + TimeSpan.FromMilliseconds(ms);
                lines.Add(new LrcLine(time, text));
            }
        }
        return lines.OrderBy(l => l.Time).ToList();
    }
}

public record LrcLine(TimeSpan Time, string Text)
{
    public string? TranslatedText { get; set; }
}
