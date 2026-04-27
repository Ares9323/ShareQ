namespace ShareQ.App.Services;

public interface IToastNotifier
{
    /// <summary>Show a toast. The optional <paramref name="onClick"/> handler is invoked
    /// on the UI thread if the user clicks the balloon while it's visible.</summary>
    void Show(string title, string message, Action? onClick = null);
}
