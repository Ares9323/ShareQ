using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ShareQ.App.Services;

/// <summary>Win32 helper to flip a WPF window's titlebar to immersive-dark mode (Win10 1809+,
/// Win11). Used everywhere we have a system-drawn titlebar that we want to match the dark
/// chrome of the rest of the app — without this every dialog flashes a white titlebar that
/// breaks the dark theme. No-op on older Windows builds.</summary>
public static partial class DarkTitleBar
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;            // build 19041+
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19; // build 18985–19040

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    /// <summary>Wires the dark-titlebar attribute to fire as soon as the window's HWND exists.
    /// Safe to call from a constructor — the SourceInitialized event is the right hook to set
    /// DWM attributes before the chrome paints, so the user never sees a white flash.</summary>
    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int useDark = 1;
            if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
            {
                _ = DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref useDark, sizeof(int));
            }
        };
    }

    /// <summary>Request rounded corners from the desktop window manager. Win11 (build 22000+)
    /// honours DWMWA_WINDOW_CORNER_PREFERENCE = ROUND; older Windows ignore the call. Lets us
    /// have rounded chrome without giving up <c>AllowsTransparency=False</c> (which is what a
    /// pure WPF Border CornerRadius would require). Safe to call on any Window.</summary>
    public static void ApplyRoundedCorners(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            int rounded = DWMWCP_ROUND;
            // Return value < 0 means the attribute isn't supported on this Windows version —
            // ignore (no rounded corners on Win10, but the window still works).
            _ = DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref rounded, sizeof(int));
        };
    }

    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;   // Win11 build 22000+
    private const int DWMWCP_ROUND = 2;                      // round on a small radius
}
