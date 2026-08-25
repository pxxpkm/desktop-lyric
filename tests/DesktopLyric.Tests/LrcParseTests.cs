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
    public void skips_empty_lines_and_metadata()
    {
        var lrc = "[ti:Song Title]\n[ar:Artist]\n[00:05.00]\n[00:10.00]actual lyric\n[00:15.00]  ";
        var lines = LyricsService.ParseLrc(lrc);

        // only "actual lyric" has text, metadata lines don't match regex, empty text skipped
        Assert.Single(lines);
        Assert.Equal("actual lyric", lines[0].Text);
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
