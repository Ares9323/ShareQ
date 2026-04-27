using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ShareQ.App.Native;
using ShareQ.App.Services;
using ShareQ.App.Services.PipelineTasks;
using ShareQ.App.ViewModels;
using ShareQ.App.Windows;
using ShareQ.Capture.DependencyInjection;
using ShareQ.Clipboard;
using ShareQ.Clipboard.DependencyInjection;
using ShareQ.Core.Pipeline;
using ShareQ.Editor.DependencyInjection;
using ShareQ.Hotkeys;
using ShareQ.Pipeline.DependencyInjection;
using ShareQ.Pipeline.Profiles;
using ShareQ.Storage.Database;
using ShareQ.Storage.DependencyInjection;

namespace ShareQ.App;

public partial class App : Application
{
    private IHost? _host;

    public IServiceProvider Services => _host?.Services
        ?? throw new InvalidOperationException("Host not initialized.");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var guard = new SingleInstanceGuard();
        if (!guard.IsPrimary)
        {
            try { await guard.NotifyExistingInstanceAsync(CancellationToken.None); }
            catch { /* primary may have died */ }
            guard.Dispose();
            Shutdown();
            return;
        }

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); })
            .ConfigureServices(services =>
            {
                services.AddSingleton(guard);

                services.AddShareQStorage();
                services.AddShareQClipboard();
                services.AddShareQCapture();
                services.AddShareQPipeline();
                services.AddShareQEditor();

                // App-side pipeline tasks (registered as IPipelineTask alongside Pipeline's baked tasks).
                services.AddSingleton<IPipelineTask, CopyImageToClipboardTask>();
                services.AddSingleton<IPipelineTask, NotifyToastTask>();

                services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();
                services.AddSingleton<IHotkeyManager, HotkeyManager>();

                services.AddSingleton<NativeClipboardHistoryProbe>();
                services.AddSingleton<NativeClipboardHistoryBanner>();
                services.AddSingleton<IncognitoModeService>();
                services.AddSingleton<ClipboardIngestionService>();
                services.AddSingleton<TargetWindowTracker>();
                services.AddSingleton<AutoPaster>();
                services.AddSingleton<PopupWindowController>();
                services.AddSingleton<CaptureCoordinator>();
                services.AddSingleton<IToastNotifier, TrayToastNotifier>();
                services.AddSingleton<EditorLauncher>();

                services.AddTransient<PopupWindowViewModel>();
                services.AddTransient<PopupWindow>();

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
                services.AddSingleton<TrayIconService>();
            })
            .Build();

        await _host.StartAsync();

        var db = _host.Services.GetRequiredService<IShareQDatabase>();
        await db.InitializeAsync(CancellationToken.None);

        // Seed default pipeline profiles (idempotent — leaves user customizations).
        var seeder = _host.Services.GetRequiredService<PipelineProfileSeeder>();
        await seeder.SeedAsync(CancellationToken.None);

        var incognito = _host.Services.GetRequiredService<IncognitoModeService>();
        await incognito.LoadAsync(CancellationToken.None);

        var banner = _host.Services.GetRequiredService<NativeClipboardHistoryBanner>();
        _ = banner.EvaluateAsync(CancellationToken.None);

        var tray = _host.Services.GetRequiredService<TrayIconService>();
        var window = _host.Services.GetRequiredService<MainWindow>();
        tray.Attach(window);

        guard.AnotherInstanceStarted += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (!window.IsVisible) window.Show();
                if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                window.Activate();
            });
        };
        guard.StartListening();

        window.Show();
        window.Closing += (sender, args) =>
        {
            args.Cancel = true;
            ((Window)sender!).Hide();
        };

        var helper = new WindowInteropHelper(window);
        helper.EnsureHandle();
        var source = HwndSource.FromHwnd(helper.Handle)!;
        source.AddHook(WndProc);

        var hotkeys = _host.Services.GetRequiredService<IHotkeyManager>();
        var hotkeyLogger = _host.Services.GetRequiredService<ILogger<App>>();
        hotkeys.Attach(helper.Handle);
        hotkeys.Triggered += OnHotkeyTriggered;

        // TODO: temporary; revisit hotkey defaults during UX pass.
        var popupOk = hotkeys.Register(new HotkeyDefinition("popup", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x56)); // Ctrl+Alt+V
        var incoOk = hotkeys.Register(new HotkeyDefinition("incognito", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x49)); // Ctrl+Alt+I
        var captureOk = hotkeys.Register(new HotkeyDefinition("capture-region", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x52)); // Ctrl+Alt+R
        hotkeyLogger.LogInformation(
            "Hotkey registration — popup(Ctrl+Alt+V): {PopupOk}, incognito(Ctrl+Alt+I): {IncoOk}, capture-region(Ctrl+Alt+R): {CaptureOk}",
            popupOk, incoOk, captureOk);

        var ingestion = _host.Services.GetRequiredService<ClipboardIngestionService>();
        ingestion.Start(helper.Handle);
    }

    private void OnHotkeyTriggered(object? sender, HotkeyTriggeredEventArgs e)
    {
        switch (e.Definition.Id)
        {
            case "popup":
                var controller = Services.GetRequiredService<PopupWindowController>();
                _ = controller.ShowAsync();
                break;
            case "incognito":
                var incognito = Services.GetRequiredService<IncognitoModeService>();
                _ = incognito.ToggleAsync(CancellationToken.None);
                break;
            case "capture-region":
                var capture = Services.GetRequiredService<CaptureCoordinator>();
                _ = capture.CaptureRegionAsync(CancellationToken.None);
                break;
            default:
                break;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == AppNativeMethods.WmHotkey)
        {
            var hotkeys = Services.GetService<IHotkeyManager>();
            handled = hotkeys?.Dispatch(wParam.ToInt32()) ?? false;
            return IntPtr.Zero;
        }
        if (msg == AppNativeMethods.WmClipboardUpdate)
        {
            var listener = Services.GetService<IClipboardListener>();
            handled = listener?.OnWindowMessage(msg) ?? false;
            return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            _host.Services.GetService<ClipboardIngestionService>()?.Dispose();
            _host.Services.GetService<IHotkeyManager>()?.Dispose();
            _host.Services.GetService<IClipboardListener>()?.Dispose();
            _host.Services.GetService<TrayIconService>()?.Dispose();
            _host.Services.GetService<SingleInstanceGuard>()?.Dispose();
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
