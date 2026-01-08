using System.Windows;
using System.Windows.Threading;
using Windows.Media.Control;

namespace DesktopLyric.Views;

public partial class MainWindow : Window
{
    private GlobalSystemMediaTransportControlsSessionManager? _mgr;
    private GlobalSystemMediaTransportControlsSession? _session;
    private DispatcherTimer _timer;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;

        // poll every 2s, lazy but it works
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _timer.Tick += (_, _) => PollNowPlaying();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            _session = _mgr.GetCurrentSession();

            if (_session != null)
            {
                TxtStatus.Text = "connected: " + (_session.SourceAppUserModelId ?? "?");
                PollNowPlaying();
                _timer.Start();
            }
            else
            {
                TxtStatus.Text = "no media session found";
            }

            _mgr.CurrentSessionChanged += (_, _) =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    _session = _mgr.GetCurrentSession();
                    if (_session != null)
                    {
                        TxtStatus.Text = "connected: " + (_session.SourceAppUserModelId ?? "?");
                        _timer.Start();
                    }
                });
            };
        }
        catch (Exception ex)
        {
            TxtStatus.Text = "smtc error: " + ex.Message;
        }
    }

    private async void PollNowPlaying()
    {
        if (_session == null) return;
        try
        {
            var props = await _session.TryGetMediaPropertiesAsync();
            if (props == null) return;
            if (!string.IsNullOrEmpty(props.Title))
            {
                TxtTitle.Text = props.Title;
                TxtArtist.Text = props.Artist ?? "";
            }
        }
        catch { }
    }
}
