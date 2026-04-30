using System.Runtime.InteropServices;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.App.Windows;

namespace ShareQ.App.Services;

public sealed partial class PopupWindowController
{
    private readonly IServiceProvider _services;
    private readonly TargetWindowTracker _target;
    private readonly AutoPaster _paster;
    private PopupWindow? _window;

    public PopupWindowController(IServiceProvider services, TargetWindowTracker target, AutoPaster paster)
    {
        _services = services;
        _target = target;
        _paster = paster;
    }

    public async Task ShowAsync()
    {
        // Toggle behaviour: if the popup is already up, hide it and bail. Same UX the
        // launcher exposes — one shortcut becomes "open" on first press, "close" on the
        // second. IsActive guards against the case where the window is technically visible
        // but minimised / behind something: we still want a tap of the shortcut to bring
        // it forward, not hide it.
        if (_window is { IsVisible: true, IsActive: true })
        {
            _window.Hide();
            return;
        }

        _target.CaptureCurrentForeground();
        EnsureWindow();
        await _window!.ViewModel.RefreshAsync(CancellationToken.None).ConfigureAwait(true);

        // Show first (so layout runs and ActualWidth/Height are real), then reposition.
        _window!.Show();
        _window!.Activate();
        // Honor the user's last saved position when present — only fall back to cursor
        // placement on the very first open (or if the saved position was off-screen).
        if (!_window!.HasPersistedPosition) RepositionAtCursor();
    }

    private void RepositionAtCursor()
    {
        if (_window is null) return;
        if (!TryGetCursorPosition(out var px, out var py)) return;

        var pt = new POINT { X = px, Y = py };
        var hMon = NativeCursor.MonitorFromPoint(pt, NativeCursor.MonitorDefaultToNearest);
        var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
        if (!NativeCursor.GetMonitorInfoW(hMon, ref info)) return;

        // Convert physical pixels (Win32) to WPF DIPs using the monitor's effective DPI.
        double scaleX = 1.0, scaleY = 1.0;
        if (NativeCursor.GetDpiForMonitor(hMon, 0, out var dpiX, out var dpiY) == 0)
        {
            scaleX = dpiX / 96.0;
            scaleY = dpiY / 96.0;
        }

        var cursorDipX = px / scaleX;
        var cursorDipY = py / scaleY;
        var workLeftDip = info.rcWork.Left / scaleX;
        var workTopDip = info.rcWork.Top / scaleY;
        var workRightDip = info.rcWork.Right / scaleX;
        var workBottomDip = info.rcWork.Bottom / scaleY;

        const double Margin = 8.0;
        var maxX = workRightDip - _window.ActualWidth - Margin;
        var maxY = workBottomDip - _window.ActualHeight - Margin;
        var minX = workLeftDip + Margin;
        var minY = workTopDip + Margin;

        _window.Left = Math.Clamp(cursorDipX, minX, Math.Max(maxX, minX));
        _window.Top = Math.Clamp(cursorDipY, minY, Math.Max(maxY, minY));
    }

    private void EnsureWindow()
    {
        if (_window is not null) return;
        _window = _services.GetRequiredService<PopupWindow>();
        _window.PasteRequested += OnPasteRequested;
        _window.OpenInEditorRequested += OnOpenInEditorRequested;
    }

    private async void OnPasteRequested(object? sender, long itemId)
    {
        try
        {
            await _paster.PasteAsync(itemId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Failures are logged inside collaborators.
        }
        finally
        {
            // The popup normally hides itself via Deactivated when AutoPaster brings the target
            // window forward; this fallback covers the case where TryRestoreCaptured fails (no valid
            // target, anti-focus-stealing trip, etc.) so the popup doesn't linger.
            if (_window is { IsVisible: true })
            {
                _window.Dispatcher.Invoke(_window.Hide);
            }
        }
    }

    private async void OnOpenInEditorRequested(object? sender, long itemId)
    {
        try
        {
            var launcher = _services.GetRequiredService<EditorLauncher>();
            await launcher.OpenAsync(itemId, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Failures are logged inside EditorLauncher.
        }
    }

    private static bool TryGetCursorPosition(out int x, out int y)
    {
        if (NativeCursor.GetCursorPos(out var pt))
        {
            x = pt.X;
            y = pt.Y;
            return true;
        }
        x = 0; y = 0;
        return false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static partial class NativeCursor
    {
        public const uint MonitorDefaultToNearest = 0x00000002;

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetCursorPos(out POINT lpPoint);

        [LibraryImport("user32.dll")]
        public static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetMonitorInfoW(IntPtr hMonitor, ref MONITORINFO lpmi);

        // Returns 0 (S_OK) on success. dpiType 0 = MDT_EFFECTIVE_DPI.
        [LibraryImport("shcore.dll")]
        public static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
