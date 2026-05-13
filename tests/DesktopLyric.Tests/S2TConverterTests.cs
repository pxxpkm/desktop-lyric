using Xunit;

namespace DesktopLyric.Tests;

public class S2TConverterTests
{
    [Fact]
    public void converts_common_chars()
    {
        var input = "我爱你";
        var result = S2TConverter.Convert(input);
        Assert.Equal("我愛你", result);
    }

    [Fact]
    public void leaves_already_traditional_alone()
    {
        var input = "這是繁體";
        var result = S2TConverter.Convert(input);
        Assert.Equal("這是繁體", result);
    }

    [Fact]
    public void handles_mixed_content()
    {
        var input = "I love 音乐 and 梦想";
        var result = S2TConverter.Convert(input);
        Assert.Contains("樂", result);
        Assert.Contains("夢", result);
        Assert.Contains("I love", result);
    }

    [Fact]
    public void empty_input_returns_empty()
    {
        Assert.Equal("", S2TConverter.Convert(""));
        Assert.Null(S2TConverter.Convert(null!));
    }

    [Fact]
    public void jay_chou_lyrics_convert_correctly()
    {
        // 七里香 chorus
        var input = "雨下整夜 我的爱溢出就像雨水";
        var result = S2TConverter.Convert(input);
        Assert.Contains("愛", result);
        Assert.Contains("溢", result); // this one stays the same
    }
}
