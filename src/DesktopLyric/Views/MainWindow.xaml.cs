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

    static MainWindow()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
    }

    // time tracking — use stopwatch between smtc polls
    private readonly Stopwatch _sw = new();
    private TimeSpan _basePos = TimeSpan.Zero;
    private bool _isPlaying;
    private string _lastArtist = "";
    private OverlayWindow? _overlay;
    private AppSettings _settings;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        Loaded += OnLoaded;

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _pollTimer.Tick += (_, _) => PollNowPlaying();

        // sync lyrics display — 100ms feels smoother
        _syncTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _syncTimer.Tick += (_, _) => SyncLyrics();

        ApplySettings();
    }

    private void ApplySettings()
    {
        TxtCurrent.FontWeight = _settings.BoldLyrics
            ? System.Windows.FontWeights.Bold
            : System.Windows.FontWeights.Normal;
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

                    // run all sources in parallel, pick first with results
                    var tasks = new[]
                    {
                        SearchNetease(title, artist),
                        SearchQQ(title, artist),
                        SearchKugou(title, artist),
                        SearchLrcLib(title, artist)
                    };
                    var results = await Task.WhenAll(tasks);
                    if (gen != _searchGen) return;

                    // pick first non-null result (priority order: netease > qq > kugou > lrclib)
                    var result = results.FirstOrDefault(r => r != null && r.Count > 0);

                    // if nothing found, try with cleaned title
                    if (result == null || result.Count == 0)
                    {
                        var clean = CleanTitle(title);
                        if (clean != title)
                        {
                            var retry = await Task.WhenAll(
                                SearchNetease(clean, artist),
                                SearchLrcLib(clean, artist));
                            if (gen != _searchGen) return;
                            result = retry.FirstOrDefault(r => r != null && r.Count > 0);
                        }
                    }

                    if (result != null && result.Count > 0)
                    {
                        _lines = result;
                        TxtCurrent.Text = "♪";

                        // translate lines that don't have translation yet
                        var needsTrans = _lines.Any(l => string.IsNullOrEmpty(l.TranslatedText));
                        if (needsTrans)
                        {
                            _ = Task.Run(() => TranslateInBackground(_lines, gen));
                        }
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

            _overlay?.UpdateLyrics(
                text,
                _settings.HideTranslation ? null : trans,
                idx < _lines.Count - 1 ? _lines[idx + 1].Text : null);
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
    private static readonly Regex TitleCleanRegex = new(@"\s*[\(\[（].*?[\)\]）]\s*$");

    /// <summary>strip (feat. X), (Remastered), [Deluxe] etc from title for better search</summary>
    private static string CleanTitle(string title)
    {
        var cleaned = TitleCleanRegex.Replace(title, "").Trim();
        return string.IsNullOrEmpty(cleaned) ? title : cleaned;
    }
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

            // match by title + artist
            string? mid = null;
            var tLow = title.ToLowerInvariant().Trim();
            var aLow = artist.ToLowerInvariant().Trim();
            int best = -1;
            foreach (var s in list.EnumerateArray())
            {
                var n = (s.GetProperty("name").GetString() ?? "").ToLowerInvariant();
                int sc = 0;
                if (n == tLow) sc += 100; else if (n.Contains(tLow) || tLow.Contains(n)) sc += 50;
                if (s.TryGetProperty("singer", out var singers))
                {
                    foreach (var si in singers.EnumerateArray())
                    {
                        var sn = (si.GetProperty("name").GetString() ?? "").ToLowerInvariant();
                        if (sn == aLow || aLow.Contains(sn) || sn.Contains(aLow)) { sc += 30; break; }
                    }
                }
                if (sc > best) { best = sc; mid = s.GetProperty("mid").GetString(); }
            }
            if (best < 10 || string.IsNullOrEmpty(mid)) return null;

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

    private async Task<List<LrcLine>?> SearchKugou(string title, string artist)
    {
        try
        {
            var kw = Uri.EscapeDataString(title + " " + artist);
            var sResp = await _http.GetAsync(
                "http://mobilecdn.kugou.com/api/v3/search/song?format=json&keyword=" + kw + "&page=1&pagesize=8");
            if (!sResp.IsSuccessStatusCode) return null;

            using var sDoc = JsonDocument.Parse(await sResp.Content.ReadAsStringAsync());
            var info = sDoc.RootElement.GetProperty("data").GetProperty("info");
            if (info.GetArrayLength() == 0) return null;

            // pick best match
            string? hash = null;
            var tLow = title.ToLowerInvariant().Trim();
            var aLow = artist.ToLowerInvariant().Trim();
            int best = -1;
            foreach (var s in info.EnumerateArray())
            {
                var n = (s.GetProperty("songname").GetString() ?? "").ToLowerInvariant();
                var sn = (s.GetProperty("singername").GetString() ?? "").ToLowerInvariant();
                int sc = 0;
                if (n == tLow) sc += 100; else if (n.Contains(tLow) || tLow.Contains(n)) sc += 50;
                if (sn == aLow || aLow.Contains(sn) || sn.Contains(aLow)) sc += 30;
                if (sc > best) { best = sc; hash = s.GetProperty("hash").GetString(); }
            }
            if (best < 20 || string.IsNullOrEmpty(hash)) return null;

            // search for lyrics by hash
            var lsResp = await _http.GetAsync(
                "https://lyrics.kugou.com/search?ver=1&man=yes&client=pc&keyword=" + kw + "&hash=" + hash);
            if (!lsResp.IsSuccessStatusCode) return null;

            using var lsDoc = JsonDocument.Parse(await lsResp.Content.ReadAsStringAsync());
            var cands = lsDoc.RootElement.GetProperty("candidates");
            if (cands.GetArrayLength() == 0) return null;

            var c0 = cands[0];
            var id = c0.GetProperty("id").GetString();
            var ak = c0.GetProperty("accesskey").GetString();
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(ak)) return null;

            var dlResp = await _http.GetAsync(
                "https://lyrics.kugou.com/download?ver=1&client=pc&id=" + id + "&accesskey=" + ak + "&fmt=lrc&charset=utf8");
            if (!dlResp.IsSuccessStatusCode) return null;

            using var dlDoc = JsonDocument.Parse(await dlResp.Content.ReadAsStringAsync());
            var contentB64 = dlDoc.RootElement.GetProperty("content").GetString();
            if (string.IsNullOrEmpty(contentB64)) return null;

            var lrcStr = Encoding.UTF8.GetString(Convert.FromBase64String(contentB64));
            var lyrics = ParseLrc(lrcStr);
            return lyrics.Count > 0 ? lyrics : null;
        }
        catch { return null; }
    }

    private async Task TranslateInBackground(List<LrcLine> lines, int gen)
    {
        var toTranslate = lines.Where(l =>
            !string.IsNullOrWhiteSpace(l.Text) && string.IsNullOrEmpty(l.TranslatedText)).ToList();
        if (toTranslate.Count == 0) return;

        // batch 10 lines at a time
        for (int i = 0; i < toTranslate.Count; i += 10)
        {
            if (gen != _searchGen) return;
            var batch = toTranslate.Skip(i).Take(10).ToList();
            var combined = string.Join("\n", batch.Select(l => l.Text));

            try
            {
                var q = Uri.EscapeDataString(combined);
                var url = "https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=zh-TW&dt=t&q=" + q;
                var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0) continue;

                var sb = new StringBuilder();
                var segs = root[0];
                if (segs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in segs.EnumerateArray())
                    {
                        if (seg.ValueKind == JsonValueKind.Array && seg.GetArrayLength() > 0)
                            sb.Append(seg[0].GetString());
                    }
                }

                var translated = sb.ToString().Trim();
                if (string.IsNullOrEmpty(translated)) continue;

                var tLines = translated.Split('\n');
                for (int j = 0; j < batch.Count && j < tLines.Length; j++)
                {
                    var t = tLines[j].Trim();
                    if (!string.IsNullOrEmpty(t) && t != batch[j].Text)
                        batch[j].TranslatedText = t;
                }
            }
            catch { }

            // don't spam google
            await Task.Delay(80);
        }
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

public record LrcLine(TimeSpan Time, string Text)
{
    public string? TranslatedText { get; set; }
}
