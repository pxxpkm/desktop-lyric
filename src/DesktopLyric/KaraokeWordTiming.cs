namespace DesktopLyric;

/// <summary>
/// word timing within a line — start/duration relative to line start
/// </summary>
public readonly record struct KaraokeWordTiming(int StartMs, int DurationMs, string Text);
