using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;

namespace ShareQ.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private readonly TaskbarIcon _icon;
    private MainWindow? _mainWindow;

    public TrayIconService(ILogger<TrayIconService> logger)
    {
        _logger = logger;
        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/tray.ico", UriKind.Absolute)),
            ToolTipText = "ShareQ",
            Visibility = Visibility.Visible
        };

        var menu = new ContextMenu();
        menu.Items.Add(BuildMenuItem("Open ShareQ", OnOpen));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildMenuItem("Quit", OnQuit));
        _icon.ContextMenu = menu;

        _icon.LeftClickCommand = new RelayCommand(OnOpen);

        // ShowNotification() requires the underlying Win32 NOTIFYICONDATA to be created.
        // The TaskbarIcon WPF wrapper sometimes defers this until first message; force it now.
        _icon.ForceCreate();
    }

    public void Attach(MainWindow window) => _mainWindow = window;

    private void OnOpen()
    {
        if (_mainWindow is null) return;
        if (!_mainWindow.IsVisible) _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void OnQuit()
    {
        _logger.LogInformation("Quit requested from tray menu");
        Application.Current.Shutdown();
    }

    private static MenuItem BuildMenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    public void ShowToast(string title, string message)
    {
        try
        {
            _icon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            // Some H.NotifyIcon builds and OS configurations don't expose ShowNotification reliably.
            // Log and continue — the screenshot has already been saved/added to history at this point.
            _logger.LogWarning(ex, "Tray toast failed; capture pipeline continues");
        }
    }

    public void Dispose() => _icon.Dispose();

    private sealed class RelayCommand(Action execute) : ICommand
    {
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
