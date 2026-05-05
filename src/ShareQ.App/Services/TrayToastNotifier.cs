using System.Windows;

namespace ShareQ.App.Services;

public sealed class TrayToastNotifier : IToastNotifier
{
    private readonly TrayIconService _tray;

    public TrayToastNotifier(TrayIconService tray)
    {
        _tray = tray;
    }

    public void Show(string title, string message, Action? onClick = null, string? imagePath = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(message);
        // Tray balloon notifications (NOTIFYICONDATA) don't support inline images — the legacy
        // Win32 API only carries an icon hint. Ignore the imagePath so callers get a text-only
        // balloon when this notifier is active.

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _tray.ShowToast(title, message, onClick);
        });
    }
}
