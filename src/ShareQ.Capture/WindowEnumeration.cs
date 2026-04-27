using System.Runtime.InteropServices;

namespace ShareQ.Capture;

public sealed record WindowSnapshot(string Title, int X, int Y, int Width, int Height);

/// <summary>Enumerates top-level visible windows (filtered like ShareX: skip cloaked, skip pure
/// tool windows). Used by the region picker to snap the selection to a real app window.</summary>
public static class WindowEnumeration
{
    public static IReadOnlyList<WindowSnapshot> EnumerateVisibleWindows()
    {
        var list = new List<WindowSnapshot>();
        EnumWindows((hWnd, _) =>
        {
            try
            {
                if (!IsWindowVisible(hWnd)) return true;
                if (IsCloaked(hWnd)) return true;
                if (IsToolNoActivate(hWnd)) return true;
                if (!GetWindowRect(hWnd, out var r)) return true;
                var w = r.Right - r.Left;
                var h = r.Bottom - r.Top;
                if (w <= 1 || h <= 1) return true;

                var title = GetWindowTitle(hWnd);
                if (string.IsNullOrEmpty(title)) return true;

                list.Add(new WindowSnapshot(title, r.Left, r.Top, w, h));
            }
            catch { /* keep enumerating regardless of one bad window */ }
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>Find the topmost window that contains the given screen-pixel point. Topmost = first
    /// in EnumWindows z-order that contains the point. Returns null if none.</summary>
    public static WindowSnapshot? FindWindowAt(int x, int y, IReadOnlyList<WindowSnapshot> windows)
    {
        foreach (var w in windows)
        {
            if (x >= w.X && x < w.X + w.Width && y >= w.Y && y < w.Y + w.Height) return w;
        }
        return null;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        var len = GetWindowTextLength(hWnd);
        if (len <= 0) return string.Empty;
        var buf = new char[len + 1];
        int copied;
        unsafe
        {
            fixed (char* p = buf) { copied = GetWindowText(hWnd, p, buf.Length); }
        }
        return copied <= 0 ? string.Empty : new string(buf, 0, copied);
    }

    private static bool IsCloaked(IntPtr hWnd)
    {
        // DWMWA_CLOAKED = 14
        if (DwmGetWindowAttribute(hWnd, 14, out var cloaked, sizeof(int)) == 0)
        {
            return cloaked != 0;
        }
        return false;
    }

    private static bool IsToolNoActivate(IntPtr hWnd)
    {
        const long WS_EX_TOOLWINDOW = 0x00000080L;
        const long WS_EX_NOACTIVATE = 0x08000000L;
        var ex = GetWindowLongPtr(hWnd, -20).ToInt64(); // GWL_EXSTYLE
        return (ex & WS_EX_TOOLWINDOW) != 0 && (ex & WS_EX_NOACTIVATE) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetWindowTextW")]
    private static extern unsafe int GetWindowText(IntPtr hWnd, char* lpString, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out int pvAttribute, int cbAttribute);
}
