using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShareQ.App.Windows;

public partial class RecordingOverlayWindow : Window
{
    private readonly DispatcherTimer _timer;
    private DateTime _resumedAt;
    private TimeSpan _accumulated = TimeSpan.Zero;
    private bool _paused;

    public RecordingOverlayWindow(int x, int y, int width, int height)
    {
        InitializeComponent();
        // (x,y,width,height) are physical pixels (Win32 capture region). Convert to DIPs once HwndSource exists.
        Left = x; Top = y; Width = width; Height = height;
        Loaded += (_, _) =>
        {
            var d = VisualTreeHelper.GetDpi(this);
            Left = x / d.DpiScaleX;
            Top = y / d.DpiScaleY;
            Width = width / d.DpiScaleX;
            Height = height / d.DpiScaleY;
        };

        _resumedAt = DateTime.UtcNow;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            var elapsed = _accumulated + (_paused ? TimeSpan.Zero : DateTime.UtcNow - _resumedAt);
            ElapsedLabel.Text = (_paused ? "PAUSED " : "REC ") + elapsed.ToString("mm\\:ss", System.Globalization.CultureInfo.InvariantCulture);
        };
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
    }

    public event EventHandler? StopRequested;
    public event EventHandler? PauseRequested;
    public event EventHandler? ResumeRequested;
    public event EventHandler? AbortRequested;

    public void SetPausedVisual(bool paused)
    {
        if (paused == _paused) return;
        if (paused) _accumulated += DateTime.UtcNow - _resumedAt;
        else _resumedAt = DateTime.UtcNow;
        _paused = paused;
        PauseButton.Content = paused ? "▶ Resume" : "⏸ Pause";
    }

    private void OnStopClicked(object sender, RoutedEventArgs e) => StopRequested?.Invoke(this, EventArgs.Empty);
    private void OnPauseClicked(object sender, RoutedEventArgs e)
    {
        if (_paused) ResumeRequested?.Invoke(this, EventArgs.Empty);
        else PauseRequested?.Invoke(this, EventArgs.Empty);
    }
    private void OnAbortClicked(object sender, RoutedEventArgs e) => AbortRequested?.Invoke(this, EventArgs.Empty);
}
