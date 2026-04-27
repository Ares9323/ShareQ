namespace ShareQ.Clipboard;

public interface IClipboardReader
{
    /// <summary>Read whatever is currently on the clipboard. Returns null if the clipboard is empty or unreadable.</summary>
    ClipboardChange? ReadCurrent(IntPtr ownerHwnd);
}
