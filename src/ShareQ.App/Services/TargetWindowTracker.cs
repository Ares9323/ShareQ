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
        return AppNativeMethods.SetForegroundWindow(_captured);
    }
}
