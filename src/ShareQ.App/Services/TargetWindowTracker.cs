using System.Runtime.InteropServices;
using ShareQ.App.Native;

namespace ShareQ.App.Services;

public sealed class TargetWindowTracker
{
    private IntPtr _captured;

    public void CaptureCurrentForeground()
    {
        _captured = AppNativeMethods.GetForegroundWindow();
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
