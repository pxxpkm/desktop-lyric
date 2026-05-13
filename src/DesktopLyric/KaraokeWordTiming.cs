namespace DesktopLyric;

/// <summary>
/// word timing within a line — start/duration relative to line start
/// netease calls this YRC, each word gets (startMs, durationMs, 0)text
/// </summary>
public readonly record struct KaraokeWordTiming(int StartMs, int DurationMs, string Text);
