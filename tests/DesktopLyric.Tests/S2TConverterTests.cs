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
}
