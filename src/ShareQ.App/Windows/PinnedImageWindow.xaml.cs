using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShareQ.App.Services;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Windows;

public partial class PinnedImageWindow : Window
{
    public const string BorderThicknessSettingKey = "pin.border_thickness";
    public const int MaxBorderThickness = 12;

    private readonly ISettingsStore? _settings;
    private readonly EditorLauncher? _editor;
    private readonly ILogger _logger;
    private BitmapSource _bitmap;
    private double _scale = 1.0;
    private int _borderThickness;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private readonly (int X, int Y)? _initialScreenPos;

    /// <param name="initialScreenPos">Optional top-left in physical screen pixels. When set, the
    /// window appears there so "Pin from screen" can leave the captured region exactly where it
    /// was — at any monitor DPI.</param>
    /// <param name="settings">Optional store for sticky settings persistence (border thickness).
    /// The window itself only WRITES via this store; reads happen at the call site so the value
    /// is known before the constructor runs (see <see cref="LoadStickyBorderAsync"/>).</param>
    /// <param name="editor">Optional editor launcher. When provided, the overlay's Edit button is
    /// active and re-opens the pinned image in the annotation editor.</param>
    /// <param name="initialBorderThickness">Sticky border thickness loaded by the caller before
    /// construction. Applied synchronously in the constructor so position math accounts for it
    /// at first paint — avoids the previous "image jumps after Loaded fires" flicker.</param>
    public PinnedImageWindow(
        BitmapSource bitmap,
        (int X, int Y)? initialScreenPos = null,
        ISettingsStore? settings = null,
        EditorLauncher? editor = null,
        int initialBorderThickness = 0,
        ILogger<PinnedImageWindow>? logger = null)
    {
        InitializeComponent();
        _bitmap = bitmap;
        _settings = settings;
        _editor = editor;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _borderThickness = Math.Clamp(initialBorderThickness, 0, MaxBorderThickness);
        _initialScreenPos = initialScreenPos;
        PinnedImage.Source = bitmap;

        if (initialScreenPos is { } pos)
        {
            SnapshotDpiFromScreenPoint(pos.X, pos.Y);
            // XAML default is now Manual — keep it consistent for the pin path.
            WindowStartupLocation = WindowStartupLocation.Manual;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            SnapshotDpiFromScreenPoint(0, 0);
        }

        ApplyBorder();
        ApplyImageSize();

        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };

        Loaded += (_, _) => UpdateZoomLabel();
    }

    /// <summary>Show + Activate + reposition. Matches PopupWindowController.ShowAsync exactly —
    /// that path is verified to land its window precisely at cursor on Win+V, so we use the
    /// same shape. Set Left/Top in DIPs AFTER Show so the layout pass has run and WPF accepts
    /// the values without falling back to startup-location logic.</summary>
    public void ShowAtCapturedPixel()
    {
        _logger.LogInformation("Pin: ShowAtCapturedPixel — initialScreenPos={Pos}, dpiScale=({Sx}×{Sy}), border={Border} DIPs, bitmap={Bw}×{Bh} px",
            _initialScreenPos, _dpiScaleX, _dpiScaleY, _borderThickness, _bitmap.PixelWidth, _bitmap.PixelHeight);
        Show();
        _logger.LogInformation("Pin: after Show — WPF Left={Left}, Top={Top}, ActualW={W}, ActualH={H}",
            Left, Top, ActualWidth, ActualHeight);
        Activate();
        if (_initialScreenPos is { } pos)
        {
            var newLeft = pos.X / _dpiScaleX - _borderThickness;
            var newTop  = pos.Y / _dpiScaleY - _borderThickness;
            Left = newLeft;
            Top  = newTop;
            _logger.LogInformation("Pin: assigned Left={Left}, Top={Top} (DIPs) — readback Left={ReadL}, Top={ReadT}",
                newLeft, newTop, Left, Top);
        }
        else
        {
            _logger.LogInformation("Pin: no initialScreenPos — leaving WPF default placement");
        }
    }

    /// <summary>Caller helper: read the sticky border value from settings BEFORE constructing the
    /// window. Doing this at the call site means the constructor can apply the border + position
    /// synchronously — matching ShareX's pattern of "Options known up front, set Location once".</summary>
    public static async Task<int> LoadStickyBorderAsync(ISettingsStore settings, CancellationToken ct)
    {
        var raw = await settings.GetAsync(BorderThicknessSettingKey, ct).ConfigureAwait(false);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var t))
            return Math.Clamp(t, 0, MaxBorderThickness);
        return 0;
    }

    /// <summary>Looks up the DPI of the monitor that contains a given screen pixel, no
    /// PresentationSource required. Used in the constructor where we don't have a visual tree
    /// yet but already know which monitor the captured pixel belongs to.</summary>
    private void SnapshotDpiFromScreenPoint(int x, int y)
    {
        var pt = new POINT { X = x, Y = y };
        var hMon = MonitorFromPoint(pt, 2 /* MONITOR_DEFAULTTONEAREST */);
        if (GetDpiForMonitor(hMon, 0 /* MDT_EFFECTIVE_DPI */, out var dpiX, out var dpiY) == 0
            && dpiX > 0 && dpiY > 0)
        {
            _dpiScaleX = dpiX / 96.0;
            _dpiScaleY = dpiY / 96.0;
        }
    }

    /// <summary>Sets both Image.Width/Height (in DIPs at 1:1 with captured physical pixels) AND
    /// the Window's outer Width/Height (image + 2× border). We size the Window explicitly because
    /// the previous SizeToContent="WidthAndHeight" approach interacted badly with manual Left/Top
    /// — WPF would re-position the window to centered during the SizeToContent layout pass even
    /// after our constructor / SourceInitialized / SetWindowPos set it elsewhere. Without
    /// SizeToContent, WPF leaves position alone and we control everything explicitly.</summary>
    private void ApplyImageSize()
    {
        var imgW = _bitmap.PixelWidth  / _dpiScaleX * _scale;
        var imgH = _bitmap.PixelHeight / _dpiScaleY * _scale;
        PinnedImage.Width  = imgW;
        PinnedImage.Height = imgH;
        Width  = imgW + 2 * _borderThickness;
        Height = imgH + 2 * _borderThickness;
    }

    private void ApplyBorder() => ImageBorder.BorderThickness = new Thickness(_borderThickness);

    private void PersistBorder()
    {
        _ = _settings?.SetAsync(BorderThicknessSettingKey,
            _borderThickness.ToString(CultureInfo.InvariantCulture),
            sensitive: false, CancellationToken.None);
    }

    private void UpdateZoomLabel()
        => ZoomLabel.Text = $"{Math.Round(_scale * 100)}%";

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        try { System.Windows.Clipboard.SetImage(_bitmap); }
        catch { /* clipboard locked — silent */ }
    }

    /// <summary>Re-open the current image in the annotation editor. On save we replace the
    /// displayed bitmap and recompute size; cancel leaves it untouched. The pin window stays at
    /// the same Left/Top throughout, so the user's spatial context is preserved.</summary>
    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if (_editor is null) return;
        try
        {
            using var ms = new MemoryStream();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_bitmap));
            encoder.Save(ms);
            var edited = await _editor.EditAsync(ms.ToArray(), CancellationToken.None).ConfigureAwait(true);
            if (edited is null || edited.Length == 0) return;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = new MemoryStream(edited);
            bmp.EndInit();
            bmp.Freeze();
            _bitmap = bmp;
            PinnedImage.Source = bmp;
            ApplyImageSize();
        }
        catch
        {
            // Editor crashes shouldn't take down the pin — keep the original image visible.
        }
    }

    private void OnResetZoomClick(object sender, RoutedEventArgs e)
    {
        if (Math.Abs(_scale - 1.0) < 1e-4) return;
        _scale = 1.0;
        ApplyImageSize();
        UpdateZoomLabel();
    }

    private void OnRootMouseEnter(object sender, MouseEventArgs e) => OverlayBar.Visibility = Visibility.Visible;
    private void OnRootMouseLeave(object sender, MouseEventArgs e) => OverlayBar.Visibility = Visibility.Collapsed;

    private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }

    private void OnImageRightClick(object sender, MouseButtonEventArgs e) => Close();

    private void OnImageWheel(object sender, MouseWheelEventArgs e)
    {
        // Ctrl+wheel = zoom (centred on the mouse cursor's pixel — same UX as image viewers).
        // Bare wheel = adjust border thickness (cheap visual customisation, sticky default).
        if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            ZoomFromCursor(sender, e);
        }
        else
        {
            AdjustBorder(e);
        }
        e.Handled = true;
    }

    private void AdjustBorder(MouseWheelEventArgs e)
    {
        var next = Math.Clamp(_borderThickness + (e.Delta > 0 ? 1 : -1), 0, MaxBorderThickness);
        if (next == _borderThickness) return;
        var delta = next - _borderThickness;
        _borderThickness = next;
        ApplyBorder();
        // Window grows / shrinks by 2*delta around the image; shift Left/Top by -delta so the
        // image stays visually anchored at its current position. Without SizeToContent we have
        // to update Width/Height ourselves — done via ApplyImageSize.
        ApplyImageSize();
        Left -= delta;
        Top  -= delta;
        PersistBorder();
    }

    private void ZoomFromCursor(object sender, MouseWheelEventArgs e)
    {
        var cursorScreen = PointToScreen(e.GetPosition(this));

        var factor = e.Delta > 0 ? 1.1 : 1.0 / 1.1;
        var newScale = Math.Clamp(_scale * factor, 0.1, 8.0);
        if (Math.Abs(newScale - _scale) < 1e-4) return;
        var actualFactor = newScale / _scale;
        _scale = newScale;

        ApplyImageSize();

        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, () =>
        {
            var ptOnScreenNow = PointToScreen(new Point(0, 0));
            var offsetX = cursorScreen.X - ptOnScreenNow.X;
            var offsetY = cursorScreen.Y - ptOnScreenNow.Y;
            var newOffsetX = offsetX * actualFactor;
            var newOffsetY = offsetY * actualFactor;
            var src2 = PresentationSource.FromVisual(this);
            var fromDevice = src2?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var newTopLeftScreen = new Point(cursorScreen.X - newOffsetX, cursorScreen.Y - newOffsetY);
            var dip = fromDevice.Transform(newTopLeftScreen);
            Left = dip.X;
            Top  = dip.Y;
            UpdateZoomLabel();
        });
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    /// <summary>Returns 0 (S_OK) on success. dpiType 0 = MDT_EFFECTIVE_DPI.</summary>
    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
