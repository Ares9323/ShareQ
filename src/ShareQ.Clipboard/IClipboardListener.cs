namespace ShareQ.Clipboard;

public interface IClipboardListener : IDisposable
{
    event EventHandler? ClipboardUpdated;

    /// <summary>Bind to the message-pump window handle. Must be called once.</summary>
    void Attach(IntPtr windowHandle);

    /// <summary>Forward a window message; returns true if the message was a clipboard-update.</summary>
    bool OnWindowMessage(int message);
}
