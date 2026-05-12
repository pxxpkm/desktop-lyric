using DesktopLyric.Services;
using Xunit;

namespace DesktopLyric.Tests;

public class LrcParseTests
{
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
