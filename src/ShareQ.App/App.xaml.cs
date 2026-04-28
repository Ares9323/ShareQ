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
using ShareQ.Plugins.DependencyInjection;
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

        // Discover external plugins from disk before building the host so their types can be
        // registered alongside built-in services. Errors per plugin are isolated.
        var pluginLoader = new ShareQ.App.Services.Plugins.PluginLoader();
        var loadedPlugins = new List<ShareQ.App.Services.Plugins.PluginDescriptor>();

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => { logging.ClearProviders(); logging.AddConsole(); })
            .ConfigureServices(services =>
            {
                services.AddSingleton(guard);

                services.AddShareQStorage();
                services.AddShareQClipboard();
                services.AddShareQCapture();
                services.AddShareQPipeline();
                services.AddShareQPlugins();
                services.AddShareQEditor();

                // Bundled plugins: registered as IUploader so the registry treats them like any
                // other plugin. The same toggle on/off (in Settings → Plugins) applies.
                services.AddSingleton<ShareQ.PluginContracts.IUploader, ShareQ.Uploaders.Catbox.CatboxUploader>();
                services.AddSingleton<ShareQ.PluginContracts.IUploader, ShareQ.Uploaders.Litterbox.LitterboxUploader>();
                services.AddSingleton<ShareQ.PluginContracts.IUploader, ShareQ.Uploaders.OneDrive.OneDriveUploader>();

                // External plugins (drop a folder under %LOCALAPPDATA%\ShareQ\plugins).
                var pluginsRoot = ShareQ.App.Services.Plugins.PluginLoader.DefaultPluginsRoot;
                loadedPlugins.AddRange(pluginLoader.LoadFromFolder(pluginsRoot, services,
                    onError: (folder, ex) => System.Diagnostics.Debug.WriteLine($"[Plugins] failed to load {folder}: {ex.Message}")));

                services.AddSingleton<ShareQ.App.Services.Plugins.PluginRegistry>(sp =>
                    new ShareQ.App.Services.Plugins.PluginRegistry(
                        sp.GetRequiredService<ShareQ.Storage.Settings.ISettingsStore>(),
                        sp.GetServices<ShareQ.PluginContracts.IUploader>(),
                        loadedPlugins));
                services.AddSingleton<ShareQ.Plugins.IUploaderResolver>(sp =>
                    sp.GetRequiredService<ShareQ.App.Services.Plugins.PluginRegistry>());

                // Host services exposed to every plugin (built-in + external).
                services.AddSingleton<ShareQ.PluginContracts.IPluginConfigStoreFactory,
                                      ShareQ.App.Services.Plugins.HostPluginConfigStoreFactory>();
                services.AddSingleton<ShareQ.PluginContracts.IOAuthHelper,
                                      ShareQ.App.Services.Plugins.HostOAuthHelper>();

                // App-side pipeline tasks (registered as IPipelineTask alongside Pipeline's baked tasks).
                services.AddSingleton<IPipelineTask, CopyImageToClipboardTask>();
                services.AddSingleton<IPipelineTask, CopyTextToClipboardTask>();
                services.AddSingleton<IPipelineTask, NotifyToastTask>();

                services.AddSingleton<NativeClipboardHistoryProbe>();
                services.AddSingleton<NativeClipboardHistoryBanner>();
                services.AddSingleton<IncognitoModeService>();
                services.AddSingleton<ClipboardIngestionService>();
                services.AddSingleton<TargetWindowTracker>();
                services.AddSingleton<AutoPaster>();
                services.AddSingleton<PopupWindowController>();
                services.AddSingleton<CaptureCoordinator>();
                services.AddSingleton<ManualUploadService>();
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

                services.AddSingleton<ShareQ.App.Services.Hotkeys.HotkeyConfigService>();
                services.AddSingleton<UploadersViewModel>();
                services.AddSingleton<HotkeysViewModel>();
                services.AddSingleton<CaptureDefaultsViewModel>();
                services.AddSingleton<SettingsViewModel>();
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

        var hotkeyConfig = _host.Services.GetRequiredService<ShareQ.App.Services.Hotkeys.HotkeyConfigService>();
        var hotkeyLogger = _host.Services.GetRequiredService<ILogger<App>>();

        // Single low-level keyboard hook for ALL global hotkeys (popup, region capture, color picker,
        // recording, ...). Hook-based registration wins against any foreground app's keyboard
        // shortcut and even against OS-reserved combos like Win+Shift+S — same approach PowerToys
        // KeyboardManager uses. In-app shortcuts (popup Ctrl+P / Enter / Ctrl+1-9, editor bindings)
        // stay focus-local via the WPF Window's KeyDown handlers — those don't pass through here.
        _keyboardHook = new KeyboardHook();

        async Task RegisterCatalogAsync(string id, Action callback)
        {
            var def = await hotkeyConfig.GetEffectiveAsync(id, CancellationToken.None);
            _keyboardHook.Register(id, def.Modifiers, def.VirtualKey, callback, suppress: true);
            hotkeyLogger.LogInformation("Hotkey {Id} bound to {Combo}",
                id, ShareQ.App.Services.Hotkeys.HotkeyDisplay.Format(def.Modifiers, def.VirtualKey));
        }

        await RegisterCatalogAsync("popup",               () => Dispatcher.InvokeAsync(() => _ = _host.Services.GetRequiredService<PopupWindowController>().ShowAsync()));
        await RegisterCatalogAsync("incognito",           () => Dispatcher.InvokeAsync(() => _ = _host.Services.GetRequiredService<IncognitoModeService>().ToggleAsync(CancellationToken.None)));
        await RegisterCatalogAsync("capture-region",      () => Dispatcher.InvokeAsync(() => _ = _host.Services.GetRequiredService<CaptureCoordinator>().CaptureRegionAsync(CancellationToken.None)));
        await RegisterCatalogAsync("screen-color-picker", () => Dispatcher.InvokeAsync(() => _host.Services.GetRequiredService<ScreenColorPickerService>().PickAtCursor()));
        await RegisterCatalogAsync("record-screen",       () => Dispatcher.InvokeAsync(() => _ = _host.Services.GetRequiredService<Services.Recording.RecordingCoordinator>().ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Mp4, CancellationToken.None)));
        await RegisterCatalogAsync("record-screen-gif",   () => Dispatcher.InvokeAsync(() => _ = _host.Services.GetRequiredService<Services.Recording.RecordingCoordinator>().ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Gif, CancellationToken.None)));

        // Settings UI raises Changed when the user rebinds → unregister + register live.
        hotkeyConfig.Changed += (_, def) =>
        {
            _keyboardHook.Unregister(def.Id);
            // Re-resolve the callback by id (we don't store callbacks in the config service).
            Action? callback = def.Id switch
            {
                "popup"               => () => Dispatcher.InvokeAsync(() => _ = _host!.Services.GetRequiredService<PopupWindowController>().ShowAsync()),
                "incognito"           => () => Dispatcher.InvokeAsync(() => _ = _host!.Services.GetRequiredService<IncognitoModeService>().ToggleAsync(CancellationToken.None)),
                "capture-region"      => () => Dispatcher.InvokeAsync(() => _ = _host!.Services.GetRequiredService<CaptureCoordinator>().CaptureRegionAsync(CancellationToken.None)),
                "screen-color-picker" => () => Dispatcher.InvokeAsync(() => _host!.Services.GetRequiredService<ScreenColorPickerService>().PickAtCursor()),
                "record-screen"       => () => Dispatcher.InvokeAsync(() => _ = _host!.Services.GetRequiredService<Services.Recording.RecordingCoordinator>().ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Mp4, CancellationToken.None)),
                "record-screen-gif"   => () => Dispatcher.InvokeAsync(() => _ = _host!.Services.GetRequiredService<Services.Recording.RecordingCoordinator>().ToggleAsync(ShareQ.Capture.Recording.RecordingFormat.Gif, CancellationToken.None)),
                _ => null,
            };
            if (callback is null) return;
            _keyboardHook.Register(def.Id, def.Modifiers, def.VirtualKey, callback, suppress: true);
            hotkeyLogger.LogInformation("Hotkey {Id} re-bound to {Combo}",
                def.Id, ShareQ.App.Services.Hotkeys.HotkeyDisplay.Format(def.Modifiers, def.VirtualKey));
        };

        try { _keyboardHook.Install(); hotkeyLogger.LogInformation("Low-level keyboard hook installed for global hotkeys."); }
        catch (Exception ex) { hotkeyLogger.LogWarning(ex, "Failed to install keyboard hook"); }

        var ingestion = _host.Services.GetRequiredService<ClipboardIngestionService>();
        ingestion.Start(helper.Handle);
    }


    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_HOTKEY no longer needed (all hotkeys go through the low-level keyboard hook). Only the
        // clipboard-update message is still routed through here for the Win32ClipboardListener.
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
            _host.Services.GetService<IClipboardListener>()?.Dispose();
            _host.Services.GetService<TrayIconService>()?.Dispose();
            _host.Services.GetService<SingleInstanceGuard>()?.Dispose();
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
