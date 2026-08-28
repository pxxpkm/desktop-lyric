using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using DesktopLyric.Services;

namespace DesktopLyric.Views;

public partial class TimingEditorWindow : Window
{
    private readonly Func<string> _title;
    private readonly Func<string> _artist;
    private readonly Func<TimeSpan> _playPos;
    private readonly Func<IReadOnlyList<LrcLine>> _lines;
    private readonly Func<TimeSpan> _lyricPos;
    private readonly Func<int> _globalMs;
    private readonly Func<TrackTiming> _getTiming;
    private readonly Action<TrackTiming> _apply;
    private readonly DispatcherTimer _timer;
    private bool _ready;
    private bool _dragging;
    private bool _syncing;
    private string _listSig = "";
    private HoldRepeat? _lineHold;
    private HoldRepeat? _rateHold;

    public TimingEditorWindow(
        Func<string> title,
        Func<string> artist,
        Func<TimeSpan> playPos,
        Func<IReadOnlyList<LrcLine>> lines,
        Func<TimeSpan> lyricPos,
        Func<int> globalMs,
        Func<TrackTiming> getTiming,
        Action<TrackTiming> apply)
    {
        InitializeComponent();
        _title = title;
        _artist = artist;
        _playPos = playPos;
        _lines = lines;
        _lyricPos = lyricPos;
        _globalMs = globalMs;
        _getTiming = getTiming;
        _apply = apply;
        LoadFromTiming(_getTiming());
        RebuildLines();
        SldOffset.ValueChanged += Slider_Changed;
        _lineHold = new HoldRepeat(NudgeSelectedLine);
        _rateHold = new HoldRepeat(delta => NudgeRate(delta >= 0 ? 1 : -1));
        _ready = true;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => RefreshLive();
        _timer.Start();
        RefreshLive();
    }

    private void LoadFromTiming(TrackTiming t)
    {
        _syncing = true;
        SldOffset.Value = Math.Clamp(t.OffsetMs, SldOffset.Minimum, SldOffset.Maximum);
        _syncing = false;
        RefreshLabels();
    }

    private void RebuildLines()
    {
        var lines = _lines() ?? [];
        var shifts = _getTiming().Lines;
        var shiftSum = 0;
        if (shifts != null)
            foreach (var kv in shifts) shiftSum += kv.Value + kv.Key.Length;
        var sig = $"{lines.Count}|{shifts?.Count ?? 0}|{shiftSum}|{lines.FirstOrDefault()?.Text}|{lines.LastOrDefault()?.Text}";
        if (sig == _listSig && LstLines.Items.Count > 0) return;
        _listSig = sig;
        var keep = (LstLines.SelectedItem as LineRow)?.Line;
        LstLines.Items.Clear();
        LineRow? reselect = null;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line.Text)) continue;
            var shown = LyricsService.TimeOf(line, shifts);
            var extra = 0;
            shifts?.TryGetValue(LyricsService.LineKey(line), out extra);
            var mark = extra == 0 ? "" : $"  {LyricOffsetStore.Format(extra)}";
            var row = new LineRow(line,
                $"[{(int)shown.TotalMinutes}:{shown.Seconds:D2}.{shown.Milliseconds / 10:D2}]{mark}  {line.Text}");
            LstLines.Items.Add(row);
            if (keep != null && ReferenceEquals(keep, line)) reselect = row;
        }
        if (reselect != null) LstLines.SelectedItem = reselect;
        RefreshLineLabel();
    }

    private void RefreshLive()
    {
        try
        {
            TxtTrack.Text = $"{_title()}  ·  {_artist()}";
            var play = _playPos();
            var lyric = _lyricPos();
            TxtClock.Text = $"播放 {Fmt(play)}   →   歌詞 {Fmt(lyric)}";
            RebuildLines();
            if (_dragging) return;
            var t = _getTiming();
            if (Math.Abs(SldOffset.Value - t.OffsetMs) > 1)
                LoadFromTiming(t);
            if (ChkFollow?.IsChecked != false)
                HighlightCurrent(lyric);
            RefreshLineLabel();
        }
        catch (Exception ex) { ErrorLog.Write(ex); }
    }

    private void Lines_UserPick(object sender, MouseButtonEventArgs e)
    {
        if (ChkFollow != null) ChkFollow.IsChecked = false;
    }

    private void HighlightCurrent(TimeSpan lyricPos)
    {
        var lines = _lines() ?? [];
        int idx = -1;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            if (LyricsService.TimeOf(lines[i], _getTiming().Lines) > lyricPos) continue;
            if (string.IsNullOrWhiteSpace(lines[i].Text)) continue;
            idx = i;
            break;
        }
        if (idx < 0 || !LyricsService.LineIsActive(lines, idx, lyricPos, _getTiming().Lines))
            return;
        var want = lines[idx];
        for (int i = 0; i < LstLines.Items.Count; i++)
        {
            if (LstLines.Items[i] is LineRow row && ReferenceEquals(row.Line, want))
            {
                if (LstLines.SelectedIndex != i)
                {
                    LstLines.SelectedIndex = i;
                    LstLines.ScrollIntoView(row);
                }
                return;
            }
        }
    }

    private static string Fmt(TimeSpan t)
        => $"{(int)t.TotalMinutes}:{t.Seconds:D2}.{t.Milliseconds / 10:D2}";

    private void RefreshLabels()
    {
        if (LblOffset == null || LblRate == null) return;
        LblOffset.Text = LyricOffsetStore.Format((int)SldOffset.Value);
        LblRate.Text = LyricOffsetStore.FormatRate(_getTiming().Rate);
        RefreshLineLabel();
    }

    private void RefreshLineLabel()
    {
        if (LblLine == null) return;
        if (LstLines.SelectedItem is not LineRow row)
        {
            LblLine.Text = "±0.00s";
            return;
        }
        var extra = 0;
        _getTiming().Lines?.TryGetValue(LyricsService.LineKey(row.Line), out extra);
        LblLine.Text = LyricOffsetStore.Format(extra);
        LblLine.Foreground = extra == 0
            ? System.Windows.Media.Brushes.Gray
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x00, 0xd4, 0xff));
    }

    private void Slider_DragStarted(object sender, DragStartedEventArgs e) => _dragging = true;

    private void Slider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _dragging = false;
        Commit();
    }

    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshLabels();
        if (!_ready || _syncing || _dragging) return;
        Commit();
    }

    private void Commit()
    {
        if (!_ready || _syncing) return;
        var cur = _getTiming();
        _apply(cur with { OffsetMs = (int)SldOffset.Value });
    }

    private void NudgeRate(int dir)
    {
        var t = _getTiming();
        var step = LyricOffsetStore.RateStep;
        var rate = Math.Clamp(t.Rate + dir * step, LyricOffsetStore.RateMin, LyricOffsetStore.RateMax);
        rate = Math.Round(rate / step) * step;
        _apply(t with { Rate = rate });
        RefreshLabels();
    }

    private void RateFaster_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _rateHold?.Down(1, sender as IInputElement);
    }

    private void RateSlower_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _rateHold?.Down(-1, sender as IInputElement);
    }

    private void RateHold_Up(object sender, MouseEventArgs e) => _rateHold?.Up();

    private void NudgeSelectedLine(int delta)
    {
        if (LstLines.SelectedItem is not LineRow row) return;
        var key = LyricsService.LineKey(row.Line);
        var t = _getTiming();
        var cur = 0;
        t.Lines?.TryGetValue(key, out cur);
        _apply(t.WithLineShift(key, cur + delta));
        _listSig = "";
        RebuildLines();
    }

    private void LineEarlier_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ChkFollow != null) ChkFollow.IsChecked = false;
        _lineHold?.Down(1, sender as IInputElement);
    }

    private void LineLater_Down(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (ChkFollow != null) ChkFollow.IsChecked = false;
        _lineHold?.Down(-1, sender as IInputElement);
    }

    private void LineHold_Up(object sender, MouseEventArgs e) => _lineHold?.Up();

    private void Align_Click(object sender, RoutedEventArgs e)
    {
        if (LstLines.SelectedItem is not LineRow row) return;
        if (ChkFollow != null) ChkFollow.IsChecked = false;
        var shift = (int)Math.Round(_lyricPos().TotalMilliseconds - row.Line.Time.TotalMilliseconds);
        shift = Math.Clamp(shift, LyricOffsetStore.MinMs, LyricOffsetStore.MaxMs);
        _apply(_getTiming().WithLineShift(LyricsService.LineKey(row.Line), shift));
        _listSig = "";
        RebuildLines();
    }

    private void ResetLine_Click(object sender, RoutedEventArgs e)
    {
        if (LstLines.SelectedItem is not LineRow row) return;
        _apply(_getTiming().WithLineShift(LyricsService.LineKey(row.Line), 0));
        _listSig = "";
        RebuildLines();
    }

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        _apply(TrackTiming.Default);
        LoadFromTiming(TrackTiming.Default);
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        if (e.OriginalSource is Button or Slider or ListBox or ListBoxItem
            or System.Windows.Controls.Primitives.Thumb)
            return;
        try { DragMove(); } catch { }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _lineHold?.Dispose();
        _lineHold = null;
        _rateHold?.Dispose();
        _rateHold = null;
        base.OnClosed(e);
    }

    private sealed record LineRow(LrcLine Line, string Display)
    {
        public override string ToString() => Display;
    }
}
