namespace ShareQ.Clipboard;

public interface IForegroundProcessProbe
{
    /// <summary>Returns the executable name (e.g. "chrome.exe") of the foreground window's owning process, or null on failure.</summary>
    string? GetForegroundProcessName();

    /// <summary>Returns the title of the foreground window, or null on failure.</summary>
    string? GetForegroundWindowTitle();
}
