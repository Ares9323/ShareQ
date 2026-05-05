using System.Diagnostics;
using System.Runtime.InteropServices;
using ShareQ.App.Native;

namespace ShareQ.App.Services;

public sealed class TargetWindowTracker
{
    private static readonly int OwnProcessId = Environment.ProcessId;
    private IntPtr _captured;

    public void CaptureCurrentForeground()
    {
        var hwnd = AppNativeMethods.GetForegroundWindow();
        if (hwnd != IntPtr.Zero && !IsOwnProcess(hwnd))
        {
            _captured = hwnd;
            return;
        }

        // Foreground is ShareQ (or zero — desktop). Two scenarios where the user still
        // expects paste to work:
        //   1. They opened the popup from a ShareQ window (Settings, tray, etc.) and want
        //      paste to land in the previous app they were using.
        //   2. The foreground bounced to ShareQ between hotkey press and dispatcher tick.
        // Walk the top-level Z-order via EnumWindows and pick the topmost visible non-ShareQ
        // window — that's the user's most-recent foreign foreground in practice. If a
        // previously captured target is still alive, prefer that (most accurate to the user's
        // actual flow); otherwise fall back to the EnumWindows scan.
        if (_captured != IntPtr.Zero
            && AppNativeMethods.IsWindow(_captured)
            && !IsOwnProcess(_captured))
        {
            Debug.WriteLine($"[TargetWindowTracker] Foreground is own process; keeping previous capture {_captured}.");
            return;
        }

        _captured = FindTopmostNonOwnWindow();
        Debug.WriteLine($"[TargetWindowTracker] Foreground is own; EnumWindows fallback found {_captured}.");
    }

    private static bool IsOwnProcess(IntPtr hwnd)
    {
        AppNativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        return (int)pid == OwnProcessId;
    }

    /// <summary>Scan top-level windows in Z-order (foreground → background) and return the
    /// first visible, non-ShareQ, non-tool window. Tool windows (WS_EX_TOOLWINDOW) and
    /// no-activate windows are skipped so the popup doesn't try to paste into a system tray
    /// flyout, the Start menu, or our own toast surface. Returns IntPtr.Zero when nothing
    /// suitable is found — AutoPaster then skips the Ctrl+V step.</summary>
    private static IntPtr FindTopmostNonOwnWindow()
    {
        IntPtr best = IntPtr.Zero;
        AppNativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!AppNativeMethods.IsWindowVisible(hwnd)) return true;
            if (IsOwnProcess(hwnd)) return true;
            // Skip tool / no-activate windows — they're never paste targets.
            var exStyle = AppNativeMethods.GetWindowLong(hwnd, AppNativeMethods.GWL_EXSTYLE);
            if ((exStyle & (AppNativeMethods.WS_EX_TOOLWINDOW | AppNativeMethods.WS_EX_NOACTIVATE)) != 0) return true;
            // Skip windows with no visible title — usually invisible helper surfaces.
            if (AppNativeMethods.GetWindowTextLength(hwnd) == 0) return true;
            best = hwnd;
            return false; // first match in Z-order wins
        }, IntPtr.Zero);
        return best;
    }

    public bool TryRestoreCaptured()
    {
        if (_captured == IntPtr.Zero) return false;

        // Win32 SetForegroundWindow has anti-focus-stealing rules: only the foreground process
        // (or one that just got input) can call it successfully. Two tricks combined make this reliable:
        //   1. AttachThreadInput between us and the target's thread to share the foreground state.
        //   2. Send a fake Alt key press to "unlock" the foreground assignment.
        var thisThread = AppNativeMethods.GetCurrentThreadId();
        var targetThread = AppNativeMethods.GetWindowThreadProcessId(_captured, out _);

        if (targetThread == 0) return false;
        if (thisThread != targetThread)
        {
            AppNativeMethods.AttachThreadInput(thisThread, targetThread, true);
        }

        SendAltTap();
        var ok = AppNativeMethods.SetForegroundWindow(_captured);

        if (thisThread != targetThread)
        {
            AppNativeMethods.AttachThreadInput(thisThread, targetThread, false);
        }
        return ok;
    }

    private static void SendAltTap()
    {
        var inputs = new AppNativeMethods.INPUT[2];
        inputs[0] = new AppNativeMethods.INPUT
        {
            type = AppNativeMethods.InputKeyboard,
            u = new AppNativeMethods.InputUnion { ki = new AppNativeMethods.KEYBDINPUT { wVk = AppNativeMethods.VkMenu } }
        };
        inputs[1] = new AppNativeMethods.INPUT
        {
            type = AppNativeMethods.InputKeyboard,
            u = new AppNativeMethods.InputUnion { ki = new AppNativeMethods.KEYBDINPUT { wVk = AppNativeMethods.VkMenu, dwFlags = AppNativeMethods.KeyEventfKeyUp } }
        };
        AppNativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<AppNativeMethods.INPUT>());
    }
}
