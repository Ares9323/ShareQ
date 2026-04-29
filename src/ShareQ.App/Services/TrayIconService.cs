using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ShareQ.App.Services;

public sealed class TrayIconService : IDisposable
{
    private readonly ILogger<TrayIconService> _logger;
    private readonly IServiceProvider _services;
    private readonly TaskbarIcon _icon;
    private MainWindow? _mainWindow;
    // Click handler bound to the most recently shown toast. Cleared after the toast closes
    // so a click on a stale balloon (which Windows still routes through after dismissal) is a no-op.
    private Action? _pendingToastClick;

    public TrayIconService(IServiceProvider services, ILogger<TrayIconService> logger)
    {
        _services = services;
        _logger = logger;
        _icon = new TaskbarIcon
        {
            IconSource = new BitmapImage(new Uri("pack://application:,,,/Assets/icon.ico", UriKind.Absolute)),
            ToolTipText = "ShareQ",
            Visibility = Visibility.Visible
        };

        _icon.ContextMenu = BuildMenu();
        _icon.LeftClickCommand = new RelayCommand(OnOpen);

        // ShowNotification() requires the underlying Win32 NOTIFYICONDATA to be created.
        // The TaskbarIcon WPF wrapper sometimes defers this until first message; force it now.
        _icon.ForceCreate();

        _icon.TrayBalloonTipClicked += OnTrayBalloonTipClicked;
        _icon.TrayBalloonTipClosed += (_, _) => _pendingToastClick = null;
    }

    public void Attach(MainWindow window) => _mainWindow = window;

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();

        // Capture submenu — mirrors ShareX's top-level Capture menu.
        var capture = new MenuItem { Header = "Capture" };
        capture.Items.Add(BuildMenuItem("Fullscreen",
            () => Run<CaptureCoordinator>(c => _ = c.CaptureFullscreenAsync(CancellationToken.None))));
        capture.Items.Add(BuildMonitorSubmenu());
        capture.Items.Add(BuildMenuItem("Region\tCtrl+Alt+R",
            () => Run<CaptureCoordinator>(c => _ = c.CaptureRegionAsync(CancellationToken.None))));
        capture.Items.Add(BuildMenuItem("Last region",
            () => Run<CaptureCoordinator>(c => _ = c.CaptureLastRegionAsync(CancellationToken.None))));
        capture.Items.Add(new Separator());
        capture.Items.Add(BuildMenuItem("Screen recording\tCtrl+Alt+S",
            () => Run<Services.Recording.RecordingCoordinator>(c => _ = c.ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Mp4, CancellationToken.None))));
        capture.Items.Add(BuildMenuItem("Screen recording (GIF)\tCtrl+Alt+G",
            () => Run<Services.Recording.RecordingCoordinator>(c => _ = c.ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Gif, CancellationToken.None))));
        capture.Items.Add(new Separator());
        capture.Items.Add(BuildMenuItem("Color sampler\tCtrl+Shift+P",
            () => Run<ScreenColorPickerService>(s => s.PickAtCursor())));
        capture.Items.Add(BuildMenuItem("Color picker…",
            () => Run<ColorWheelLauncher>(l => _ = l.ShowAsync())));
        menu.Items.Add(capture);

        // Upload submenu — kicks off the manual-upload pipeline from arbitrary sources.
        var upload = new MenuItem { Header = "Upload" };
        upload.Items.Add(BuildMenuItem("Upload file…", OnUploadFile));
        upload.Items.Add(BuildMenuItem("Upload from clipboard", OnUploadFromClipboard));
        // Upload text/URL go in a later sprint (need a text-input dialog).
        menu.Items.Add(upload);

        // Tools submenu — placeholder; we'll grow this with QR / hash / ruler / etc.
        var tools = new MenuItem { Header = "Tools" };
        tools.Items.Add(BuildMenuItem("Color sampler",
            () => Run<ScreenColorPickerService>(s => s.PickAtCursor())));
        tools.Items.Add(BuildMenuItem("Color picker…",
            () => Run<ColorWheelLauncher>(l => _ = l.ShowAsync())));
        tools.Items.Add(BuildMenuItem("Pin to screen…",
            () => Run<PinToScreenLauncher>(p => _ = p.ShowAsync(CancellationToken.None))));
        menu.Items.Add(tools);

        menu.Items.Add(new Separator());
        menu.Items.Add(BuildMenuItem("Open clipboard popup\tWin+V",
            () => Run<PopupWindowController>(c => _ = c.ShowAsync())));
        menu.Items.Add(BuildMenuItem("Toggle incognito\tCtrl+Alt+I",
            () => Run<IncognitoModeService>(s => _ = s.ToggleAsync(CancellationToken.None))));
        menu.Items.Add(new Separator());
        menu.Items.Add(BuildMenuItem("Settings…", OnOpen));
        menu.Items.Add(BuildMenuItem("Quit", OnQuit));
        return menu;
    }

    private MenuItem BuildMonitorSubmenu()
    {
        // Built each time the parent menu opens so monitor changes (hot-plug, resolution change,
        // primary swap) are reflected without an app restart.
        var item = new MenuItem { Header = "Monitor" };
        item.SubmenuOpened += (_, _) =>
        {
            item.Items.Clear();
            var monitors = ShareQ.Capture.MonitorEnumeration.Enumerate();
            if (monitors.Count == 0)
            {
                item.Items.Add(new MenuItem { Header = "(no monitors detected)", IsEnabled = false });
                return;
            }
            for (var i = 0; i < monitors.Count; i++)
            {
                var monitor = monitors[i];
                var label = $"{i + 1}. {monitor.Name}  {monitor.Width}×{monitor.Height}{(monitor.IsPrimary ? "  (primary)" : string.Empty)}";
                item.Items.Add(BuildMenuItem(label,
                    () => Run<CaptureCoordinator>(c => _ = c.CaptureMonitorAsync(monitor, CancellationToken.None))));
            }
        };
        // Placeholder so the chevron renders before the submenu is opened the first time.
        item.Items.Add(new MenuItem { Header = "(populating…)", IsEnabled = false });
        return item;
    }

    private void OnUploadFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Upload file with ShareQ",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true) return;
        var path = dialog.FileName;
        Run<ManualUploadService>(s => _ = s.UploadFileAsync(path, CancellationToken.None));
    }

    private void OnUploadFromClipboard()
        => Run<ManualUploadService>(s => _ = s.UploadCurrentClipboardAsync(CancellationToken.None));

    private void Run<T>(Action<T> action) where T : notnull
    {
        try
        {
            var service = (T)_services.GetService(typeof(T))!;
            action(service);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tray menu action threw for {Type}", typeof(T).Name);
        }
    }

    private void OnTrayBalloonTipClicked(object sender, RoutedEventArgs e)
    {
        var handler = _pendingToastClick;
        _pendingToastClick = null;
        if (handler is null) return;
        try { handler(); }
        catch (Exception ex) { _logger.LogError(ex, "Toast click handler threw"); }
    }

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

    public void ShowToast(string title, string message, Action? onClick = null)
    {
        _pendingToastClick = onClick;
        try
        {
            _icon.ShowNotification(title, message, H.NotifyIcon.Core.NotificationIcon.Info);
        }
        catch (Exception ex)
        {
            _pendingToastClick = null;
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
