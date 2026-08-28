using DesktopLyric.Services;
using Xunit;

namespace DesktopLyric.Tests;

public class LrcParseTests
{
    [Fact]
    public void splits_jp_cn_packed_into_one_line()
    {
        var (orig, trans) = LyricsService.SplitBilingual("夕暮れ 駆け抜けた在黃昏中奔馳而過");
        Assert.Equal("夕暮れ 駆け抜けた", orig);
        Assert.Equal("在黃昏中奔馳而過", trans);
    }

    [Fact]
    public void splits_jp_cn_slash_line()
    {
        var (orig, trans) = LyricsService.SplitBilingual("きみの声 まだ残る / 你的聲音仍迴盪著");
        Assert.Equal("きみの声 まだ残る", orig);
        Assert.Equal("你的聲音仍迴盪著", trans);
    }

    [Fact]
    public void pairs_same_timestamp_jp_then_cn()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(12), "夕暮れ 駆け抜けた"),
            new(TimeSpan.FromSeconds(12), "在黃昏中奔馳而過"),
            new(TimeSpan.FromSeconds(15), "きみの声 まだ残る"),
        };
        LyricsService.SplitMixedLyrics(lines);
        Assert.Equal(2, lines.Count);
        Assert.Equal("夕暮れ 駆け抜けた", lines[0].Text);
        Assert.Equal("在黃昏中奔馳而過", lines[0].TranslatedText);
    }

    [Fact]
    public void karaoke_words_drop_trailing_chinese()
    {
        var line = new LrcLine(TimeSpan.FromSeconds(1), "夕暮れ駆け抜けた在黃昏奔馳");
        line.WordTimings =
        [
            new(0, 100, "夕"), new(100, 100, "暮"), new(200, 100, "れ"),
            new(300, 100, "駆"), new(400, 100, "け"), new(500, 100, "抜"),
            new(600, 100, "け"), new(700, 100, "た"),
            new(800, 100, "在"), new(900, 100, "黃"), new(1000, 100, "昏"),
            new(1100, 100, "奔"), new(1200, 100, "馳"),
        ];
        var list = new List<LrcLine> { line };
        LyricsService.SplitMixedLyrics(list);
        Assert.Equal("夕暮れ駆け抜けた", list[0].Text);
        Assert.Equal("在黃昏奔馳", list[0].TranslatedText);
        Assert.Equal(8, list[0].WordTimings!.Count);
        Assert.DoesNotContain(list[0].WordTimings, w => w.Text is "在" or "黃");
    }

    [Fact]
    public void yrc_absolute_word_times_become_relative_to_line()
    {
        var yrc = "[14726,1200](14726,240,0)何(14966,240,0)か(15206,400,0)を";
        var lines = LyricsService.ParseYrcLines(yrc);
        Assert.Single(lines);
        Assert.Equal(14726, lines[0].startMs);
        Assert.Equal(1200, lines[0].durMs);
        Assert.Equal(3, lines[0].words.Count);
        Assert.Equal(0, lines[0].words[0].StartMs);
        Assert.Equal(240, lines[0].words[1].StartMs);
        Assert.Equal(480, lines[0].words[2].StartMs);
        Assert.Equal("何", lines[0].words[0].Text);
    }

    [Fact]
    public void yrc_already_relative_word_times_stay_relative()
    {
        var yrc = "[14726,1200](0,240,0)何(240,240,0)か(480,400,0)を";
        var lines = LyricsService.ParseYrcLines(yrc);
        Assert.Single(lines);
        Assert.Equal(0, lines[0].words[0].StartMs);
        Assert.Equal(240, lines[0].words[1].StartMs);
        Assert.Equal(480, lines[0].words[2].StartMs);
    }

    [Fact]
    public void parses_standard_lrc_timestamps()
    {
        var lrc = "[00:12.34]hello world\n[00:15.67]second line";
        var lines = LyricsService.ParseLrc(lrc);

        Assert.Equal(2, lines.Count);
        Assert.Equal("hello world", lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(12340), lines[0].Time);
        Assert.Equal("second line", lines[1].Text);
    }

    [Fact]
    public void handles_3_digit_milliseconds()
    {
        // yoasobi - idol has these
        var lrc = "[01:05.123]three digits\n[01:10.456]another";
        var lines = LyricsService.ParseLrc(lrc);

        Assert.Equal(2, lines.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(65123), lines[0].Time);
    }

    [Fact]
    public void keeps_empty_timestamps_as_gap_markers()
    {
        var lrc = "[ti:Song Title]\n[ar:Artist]\n[00:05.00]\n[00:10.00]actual lyric\n[00:15.00]  ";
        var lines = LyricsService.ParseLrc(lrc);

        Assert.Equal(3, lines.Count);
        Assert.Equal("", lines[0].Text);
        Assert.Equal("actual lyric", lines[1].Text);
        Assert.Equal("", lines[2].Text);
    }

    [Fact]
    public void gap_after_karaoke_clears_only_after_a_long_hold()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(10), "verse end")
            {
                WordTimings = [new(0, 800, "verse"), new(800, 400, "end")],
            },
            new(TimeSpan.FromSeconds(50), "next verse"),
        };
        Assert.True(LyricsService.LineIsActive(lines, 0, TimeSpan.FromSeconds(10.5)));
        Assert.True(LyricsService.LineIsActive(lines, 0, TimeSpan.FromSeconds(16.5)));
        Assert.False(LyricsService.LineIsActive(lines, 0, TimeSpan.FromSeconds(18)));
        Assert.True(LyricsService.LineIsActive(lines, 1, TimeSpan.FromSeconds(50.2)));
    }

    [Fact]
    public void empty_timestamp_between_nearby_lines_does_not_cut()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(5), "hello"),
            new(TimeSpan.FromSeconds(8), ""),
            new(TimeSpan.FromSeconds(12), "later"),
        };
        Assert.True(LyricsService.LineIsActive(lines, 0, TimeSpan.FromSeconds(9)));
        Assert.Equal(TimeSpan.FromSeconds(12), LyricsService.LineDisplayEnd(lines, 0));
        Assert.False(LyricsService.LineIsActive(lines, 1, TimeSpan.FromSeconds(9)));
    }

    [Fact]
    public void consecutive_lines_hold_until_the_next_stamp()
    {
        var lines = new List<LrcLine>
        {
            new(TimeSpan.FromSeconds(10), "a"),
            new(TimeSpan.FromSeconds(13), "b"),
        };
        Assert.Equal(TimeSpan.FromSeconds(13), LyricsService.LineDisplayEnd(lines, 0));
        Assert.True(LyricsService.LineIsActive(lines, 0, TimeSpan.FromSeconds(12.5)));
    }

    [Fact]
    public void lines_sorted_by_time()
    {
        var lrc = "[00:30.00]late\n[00:05.00]early\n[00:15.00]middle";
        var lines = LyricsService.ParseLrc(lrc);

        Assert.Equal("early", lines[0].Text);
        Assert.Equal("middle", lines[1].Text);
        Assert.Equal("late", lines[2].Text);
    }
}
