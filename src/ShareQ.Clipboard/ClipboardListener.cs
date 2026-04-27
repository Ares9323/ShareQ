using ShareQ.Clipboard.Native;

namespace ShareQ.Clipboard;

public sealed class ClipboardListener : IClipboardListener
{
    private IntPtr _hwnd;
    private bool _suppressNext;

    public event EventHandler? ClipboardUpdated;

    public void Attach(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) throw new ArgumentException("Handle cannot be zero.", nameof(windowHandle));
        if (_hwnd != IntPtr.Zero) throw new InvalidOperationException("ClipboardListener is already attached.");
        if (!ClipboardNativeMethods.AddClipboardFormatListener(windowHandle))
            throw new InvalidOperationException("AddClipboardFormatListener failed.");
        _hwnd = windowHandle;
    }

    public bool OnWindowMessage(int message)
    {
        if (message != ClipboardNativeMethods.WmClipboardUpdate) return false;
        if (_suppressNext)
        {
            _suppressNext = false;
            return true;
        }
        ClipboardUpdated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SuppressNext() => _suppressNext = true;

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        ClipboardNativeMethods.RemoveClipboardFormatListener(_hwnd);
        _hwnd = IntPtr.Zero;
    }
}
