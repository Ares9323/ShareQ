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

// CA1001 doesn't apply: WPF Application is not IDisposable but has OnExit which we use for cleanup.
#pragma warning disable CA1001
public partial class App : Application
#pragma warning restore CA1001
{
    private IHost? _host;
    private KeyboardHook? _keyboardHook;

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
                services.AddSingleton<IToastNotifier, WpfToastNotifier>();
                services.AddSingleton<EditorLauncher>();
                services.AddSingleton<ScreenColorPickerService>();
                services.AddSingleton<Services.Recording.FfmpegLocator>();
                services.AddSingleton<Services.Recording.FfmpegDownloader>();
                services.AddSingleton<Services.Recording.ScreenRecordingService>();
                services.AddSingleton<Services.Recording.RecordingCoordinator>();
                services.AddSingleton<ShareQ.Editor.Persistence.ColorRecentsStore>();
                services.AddSingleton<ShareQ.Editor.Persistence.EditorDefaultsStore>();

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
        var notifier = _host.Services.GetRequiredService<IToastNotifier>();
        incognito.StateChanged += (_, _) =>
            notifier.Show("Incognito mode", incognito.IsActive ? "ON — clipboard items won't be captured" : "OFF — capture resumed");

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
        var pickerOk = hotkeys.Register(new HotkeyDefinition("screen-color-picker", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x50)); // Ctrl+Shift+P
        var recordOk = hotkeys.Register(new HotkeyDefinition("record-screen", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x53)); // Ctrl+Alt+S
        var recordGifOk = hotkeys.Register(new HotkeyDefinition("record-screen-gif", HotkeyModifiers.Control | HotkeyModifiers.Alt, 0x47)); // Ctrl+Alt+G
        hotkeyLogger.LogInformation(
            "Hotkey registration — popup(Ctrl+Alt+V): {PopupOk}, incognito(Ctrl+Alt+I): {IncoOk}, capture-region(Ctrl+Alt+R): {CaptureOk}, screen-color-picker(Ctrl+Shift+P): {PickerOk}, record(Ctrl+Alt+S): {RecOk}, record-gif(Ctrl+Alt+G): {RecGifOk}",
            popupOk, incoOk, captureOk, pickerOk, recordOk, recordGifOk);

        // Win+V is reserved by Windows for the native clipboard history; RegisterHotKey can't bind
        // it, so we use a low-level keyboard hook (same trick PowerToys KeyboardManager uses) and
        // suppress the keystroke before Windows sees it.
        _keyboardHook = new KeyboardHook();
        _keyboardHook.Register(HotkeyModifiers.Win, 0x56, () => // Win+V
        {
            var controller = _host.Services.GetRequiredService<PopupWindowController>();
            Dispatcher.InvokeAsync(() => _ = controller.ShowAsync());
        });
        try { _keyboardHook.Install(); hotkeyLogger.LogInformation("Win+V intercepted via low-level keyboard hook."); }
        catch (Exception ex) { hotkeyLogger.LogWarning(ex, "Failed to install keyboard hook for Win+V"); }

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
            case "screen-color-picker":
                var picker = Services.GetRequiredService<ScreenColorPickerService>();
                picker.PickAtCursor();
                break;
            case "record-screen":
                var rec = Services.GetRequiredService<Services.Recording.RecordingCoordinator>();
                _ = rec.ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Mp4, CancellationToken.None);
                break;
            case "record-screen-gif":
                var recGif = Services.GetRequiredService<Services.Recording.RecordingCoordinator>();
                _ = recGif.ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Gif, CancellationToken.None);
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
        _keyboardHook?.Dispose();
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
