using DesktopLyric.Services;
using Xunit;

namespace DesktopLyric.Tests;

[Collection("LyricOffsetStore")]
public class LyricOffsetStoreTests : IDisposable
{
    private readonly string _path;

    public LyricOffsetStoreTests()
    {
        _path = Path.Combine(Path.GetTempPath(), "DesktopLyric-off-" + Guid.NewGuid().ToString("N") + ".json");
        LyricOffsetStore.ResetForTests(_path);
    }

    public void Dispose()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch { }
        LyricOffsetStore.ResetForTests(_path + ".done");
    }

    [Fact]
    public void remembers_offset_for_a_track()
    {
        LyricOffsetStore.SetMs("七里香", "周杰倫", 200);
        Assert.Equal(200, LyricOffsetStore.GetMs("七里香", "周杰倫"));
    }

    [Fact]
    public void zero_clears_offset()
    {
        LyricOffsetStore.SetMs("七里香", "周杰倫", 200);
        LyricOffsetStore.SetMs("七里香", "周杰倫", 0);
        Assert.Equal(0, LyricOffsetStore.GetMs("七里香", "周杰倫"));
    }

    [Fact]
    public void youtube_dump_and_extracted_song_share_offset()
    {
        const string dump =
            "TVアニメ「とある科学の超電磁砲」後期OP映像（ LEVEL5 -judgelight-／ fripSide）【NBCユニバーサルAnime✕Music30周年記念OP/ED毎日投稿企画】";
        LyricOffsetStore.SetMs(dump, "NBCUNIVERSAL ANIME/MUSIC", -150);
        Assert.Equal(-150, LyricOffsetStore.GetMs(dump, "NBCUNIVERSAL ANIME/MUSIC"));
        Assert.Equal(-150, LyricOffsetStore.GetMs("LEVEL5 -judgelight-", "fripSide"));
    }

    [Fact]
    public void nudge_steps_and_clamps()
    {
        Assert.Equal(50, LyricOffsetStore.Nudge("a", "b", 50));
        Assert.Equal(100, LyricOffsetStore.Nudge("a", "b", 50));
        Assert.Equal(LyricOffsetStore.MaxMs, LyricOffsetStore.Nudge("a", "b", 99_000));
    }

    [Fact]
    public void format_shows_seconds()
    {
        Assert.Equal("±0.00s", LyricOffsetStore.Format(0));
        Assert.Equal("+0.20s", LyricOffsetStore.Format(200));
        Assert.Equal("−0.05s", LyricOffsetStore.Format(-50));
    }
}
