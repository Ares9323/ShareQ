namespace ShareQ.Core.Domain;

public enum ItemSource
{
    Clipboard,
    CaptureRegion,
    CaptureWindow,
    CaptureFullscreen,
    CaptureMonitor,
    CaptureWebpage,
    CaptureRecording,
    /// <summary>User-driven upload (file picker, "upload from clipboard", text editor).</summary>
    Manual,
}
