using System.Windows;
using System.Windows.Input;
using DesktopLyric.Services;

namespace DesktopLyric.Views;

public partial class PickSongWindow : Window
{
    private readonly LyricsService _lyrics;

    public LyricCandidate? Chosen { get; private set; }
    public bool Remember => ChkRemember.IsChecked == true;

    public PickSongWindow(LyricsService lyrics, string title, string artist)
    {
        InitializeComponent();
        _lyrics = lyrics;
        TxtTitle.Text = title ?? "";
        TxtArtist.Text = artist ?? "";
        Loaded += async (_, _) => await RunSearch();
    }

    private void Window_Drag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
            DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private async void Search_Click(object sender, RoutedEventArgs e) => await RunSearch();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) await RunSearch();
    }

    private async Task RunSearch()
    {
        var title = TxtTitle.Text.Trim();
        var artist = TxtArtist.Text.Trim();
        if (string.IsNullOrEmpty(title))
        {
            TxtStatus.Text = "輸入歌名再搜";
            return;
        }

        TxtStatus.Text = "搜尋中...";
        LstResults.ItemsSource = null;
        try
        {
            var list = await _lyrics.SearchCandidatesAsync(title, artist);
            LstResults.ItemsSource = list;
            if (list.Count > 0)
            {
                LstResults.SelectedIndex = 0;
                TxtStatus.Text = $"搵到 {list.Count} 首，雙擊或按「使用」";
            }
            else TxtStatus.Text = "搵唔到，試下改歌名／歌手";
        }
        catch
        {
            TxtStatus.Text = "搜尋失敗";
        }
    }

    private void Use_Click(object sender, RoutedEventArgs e)
    {
        if (LstResults.SelectedItem is LyricCandidate c)
        {
            Chosen = c;
            DialogResult = true;
        }
    }
}
