using System.Windows;
using System.Windows.Input;

namespace DesktopLyric.Views;

public partial class OverlayWindow : Window
{
    public OverlayWindow()
    {
        InitializeComponent();
    }

    public void UpdateLyrics(string current, string? translated, string? next = null)
    {
        OvCurrent.Text = current ?? "";
        OvTrans.Text = translated ?? "";
        OvNext.Text = next ?? "";
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }
}
