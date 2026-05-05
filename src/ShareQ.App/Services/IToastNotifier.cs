namespace ShareQ.App.Services;

public interface IToastNotifier
{
    /// <summary>Show a toast. The optional <paramref name="onClick"/> handler is invoked
    /// on the UI thread if the user clicks the balloon while it's visible.
    /// <paramref name="imagePath"/> is an absolute file path to a PNG/JPEG the toast should
    /// display alongside the text — implementations that don't support inline images (the
    /// legacy balloon, the custom WPF stack) silently ignore it.</summary>
    void Show(string title, string message, Action? onClick = null, string? imagePath = null);
}
