using System.Text;

namespace DesktopLyric;

/// <summary>
/// simplified → traditional, just the common ones in lyrics
/// not a full converter, but good enough for song lyrics
/// </summary>
public static class S2TConverter
{
    public static string Convert(string input)
    {
        if (string.IsNullOrEmpty(input)) return input;
        var sb = new StringBuilder(input.Length);
        foreach (var c in input)
            sb.Append(Map.TryGetValue(c, out var t) ? t : c);
        return sb.ToString();
    }

    private static readonly Dictionary<char, char> Map = BuildMap();

    private static Dictionary<char, char> BuildMap()
    {
        var m = new Dictionary<char, char>(300);
        void A(char s, char t) { m[s] = t; }

        // most common in lyrics — added as I find them
        A('爱','愛'); A('边','邊'); A('变','變'); A('别','別');
        A('长','長'); A('车','車'); A('从','從'); A('达','達');
        A('带','帶'); A('单','單'); A('当','當'); A('点','點');
        A('东','東'); A('动','動'); A('对','對'); A('发','發');
        A('飞','飛'); A('风','風'); A('个','個'); A('给','給');
        A('关','關'); A('过','過'); A('还','還'); A('后','後');
        A('华','華'); A('欢','歡'); A('会','會'); A('机','機');
        A('几','幾'); A('间','間'); A('见','見'); A('将','將');
        A('进','進'); A('经','經'); A('开','開'); A('来','來');
        A('乐','樂'); A('离','離'); A('里','裡'); A('两','兩');
        A('灵','靈'); A('龙','龍'); A('马','馬'); A('么','麼');
        A('没','沒'); A('门','門'); A('们','們'); A('梦','夢');
        A('难','難'); A('鸟','鳥'); A('让','讓'); A('热','熱');
        A('认','認'); A('时','時'); A('实','實'); A('说','說');
        A('虽','雖'); A('岁','歲'); A('听','聽'); A('头','頭');
        A('万','萬'); A('为','為'); A('问','問'); A('无','無');
        A('习','習'); A('现','現'); A('乡','鄉'); A('写','寫');
        A('心','心'); A('兴','興'); A('学','學'); A('样','樣');
        A('业','業'); A('义','義'); A('应','應'); A('远','遠');
        A('云','雲'); A('这','這'); A('种','種'); A('转','轉');

        // extra ones I keep seeing in lyrics
        A('泪','淚'); A('谢','謝'); A('烟','煙'); A('忆','憶');
        A('隐','隱'); A('忧','憂'); A('游','遊'); A('钟','鐘');
        A('终','終'); A('属','屬');

        return m;
    }
}
