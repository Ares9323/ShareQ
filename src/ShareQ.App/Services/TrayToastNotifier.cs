using System.Windows;

namespace ShareQ.App.Services;

public sealed class TrayToastNotifier : IToastNotifier
{
    private readonly TrayIconService _tray;

    public TrayToastNotifier(TrayIconService tray)
    {
        _tray = tray;
    }

    public void Show(string title, string message, Action? onClick = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(title);
        ArgumentNullException.ThrowIfNull(message);

        Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _tray.ShowToast(title, message, onClick);
        });
    }
}
