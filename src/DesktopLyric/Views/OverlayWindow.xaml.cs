using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace DesktopLyric.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
        // topmost + no taskbar icon = always visible but not annoying
        // like those karaoke bars in mong kok lol
    }

    public void UpdateLyrics(string current, string? translated, string? next = null,
        List<KaraokeWordTiming>? wordTimings = null, double lineElapsedMs = 0)
    {
        // karaoke mode: color each word based on timing
        if (wordTimings != null && wordTimings.Count > 0)
        {
            OvCurrent.Inlines.Clear();
            foreach (var w in wordTimings)
            {
                var endMs = w.StartMs + w.DurationMs;
                Color c;
                if (lineElapsedMs >= endMs)
                    c = Color.FromRgb(0x00, 0xd4, 0xff); // sung
                else if (lineElapsedMs <= w.StartMs)
                    c = Color.FromRgb(0x55, 0x66, 0x77); // not yet
                else
                {
                    // mid-word, blend
                    var pct = (lineElapsedMs - w.StartMs) / Math.Max(1, w.DurationMs);
                    var r = (byte)(0x55 + (0x00 - 0x55) * pct);
                    var g = (byte)(0x66 + (0xd4 - 0x66) * pct);
                    var b = (byte)(0x77 + (0xff - 0x77) * pct);
                    c = Color.FromRgb(r, g, b);
                }
                OvCurrent.Inlines.Add(new Run(w.Text)
                {
                    Foreground = new SolidColorBrush(c)
                });
            }
        }
        else
        {
            OvCurrent.Inlines.Clear();
            OvCurrent.Inlines.Add(new Run(current ?? ""));
        }

        OvTrans.Text = translated ?? "";
        OvNext.Text = next ?? "";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
