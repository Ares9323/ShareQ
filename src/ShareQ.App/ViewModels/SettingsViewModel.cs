using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services;
using ShareQ.App.Services.Plugins;
using ShareQ.Storage.Settings;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private const string StartMinimizedKey = "app.start_minimized";
    private readonly AutostartService _autostart;
    private readonly ISettingsStore _settingsStore;

    public SettingsViewModel(
        PluginRegistry registry,
        AutostartService autostart,
        ISettingsStore settingsStore,
        UploadersViewModel uploaders,
        HotkeysViewModel hotkeys,
        CaptureDefaultsViewModel capture,
        WorkflowsViewModel workflows,
        ThemeViewModel theme,
        CategoriesViewModel categories,
        DebugViewModel debug)
    {
        _autostart = autostart;
        _settingsStore = settingsStore;
        Theme = theme;
        Categories = categories;
        Debug = debug;
        // Initial state mirrors the registry. Future toggles persist via OnStartWithWindowsChanged.
        _suppressAutostartPersist = true;
        StartWithWindows = autostart.IsEnabled;
        _suppressAutostartPersist = false;

        // StartMinimized hydrates async from the SQLite settings store. The fire-and-forget is
        // intentional: SettingsViewModel is constructed eagerly during DI, before the user has
        // navigated to the Settings tab, so we don't block startup waiting for a value most
        // users will never look at on this launch. Suppress persists during the load so the
        // initial assignment doesn't round-trip back to disk with the same value.
        _ = LoadStartMinimizedAsync();
        Uploaders = uploaders;
        // Hotkeys "Add custom workflow" button → run the Add flow (no modal — auto-default name)
        // then drop straight into the edit view with the inline name field focused + selected
        // so the user can type the real name immediately.
        hotkeys.AddCustomWorkflowRequested += async (_, _) =>
        {
            await workflows.ReloadWorkflowsAsync().ConfigureAwait(true);
            await workflows.AddWorkflowCommand.ExecuteAsync(null).ConfigureAwait(true);
            if (workflows.SelectedWorkflow is { } added)
                await hotkeys.BeginEditAsync(added.Id, focusName: true).ConfigureAwait(true);
        };
        Hotkeys = hotkeys;
        Capture = capture;
        Workflows = workflows;
        // Reads the same string that MSBuild's -p:Version was given (suffix included). Falls
        // back to the 4-part numeric version when the InformationalVersion attribute is
        // missing — defensive only, the SDK always emits one.
        var asm = typeof(SettingsViewModel).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        // SourceLink appends "+<git-sha>" to InformationalVersion in modern .NET SDKs; keep
        // just the leading semver part so About reads "0.1.0-dev" not "0.1.0-dev+abc1234".
        var plus = info?.IndexOf('+') ?? -1;
        AppVersion = plus >= 0 ? info![..plus] : (info ?? asm.GetName().Version?.ToString() ?? "dev");
        // Hold a reference so DI doesn't garbage-collect the registry singleton through this VM.
        _ = registry;
    }

    public UploadersViewModel Uploaders { get; }
    public HotkeysViewModel Hotkeys { get; }
    public CaptureDefaultsViewModel Capture { get; }
    public WorkflowsViewModel Workflows { get; }
    public ThemeViewModel Theme { get; }
    public CategoriesViewModel Categories { get; }
    public DebugViewModel Debug { get; }

    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.Hotkeys;

    /// <summary>Bound to the Settings-tab "Run ShareQ when Windows starts" checkbox. Reads /
    /// writes HKCU Run via <see cref="AutostartService"/>. Constructor seeds it from the current
    /// registry value (no flicker), then user toggles propagate immediately.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    private bool _suppressAutostartPersist;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressAutostartPersist) return;
        _autostart.SetEnabled(value);
    }

    /// <summary>Bound to the Settings-tab "Start minimized" checkbox. Persisted in the SQLite
    /// settings store under <see cref="StartMinimizedKey"/>; <see cref="App.OnStartup"/> reads
    /// it before deciding whether to call <c>window.Show()</c> on launch.</summary>
    [ObservableProperty]
    private bool _startMinimized;

    private bool _suppressStartMinimizedPersist;

    private async Task LoadStartMinimizedAsync()
    {
        var raw = await _settingsStore.GetAsync(StartMinimizedKey, CancellationToken.None).ConfigureAwait(true);
        _suppressStartMinimizedPersist = true;
        try { StartMinimized = string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase); }
        finally { _suppressStartMinimizedPersist = false; }
    }

    partial void OnStartMinimizedChanged(bool value)
    {
        if (_suppressStartMinimizedPersist) return;
        _ = _settingsStore.SetAsync(StartMinimizedKey,
            value ? "true" : "false",
            sensitive: false,
            CancellationToken.None);
    }

    public bool IsUploadersSelected => SelectedTab == SettingsTab.Uploaders;
    public bool IsHotkeysSelected   => SelectedTab == SettingsTab.Hotkeys;
    public bool IsCaptureSelected   => SelectedTab == SettingsTab.Capture;
    public bool IsThemeSelected      => SelectedTab == SettingsTab.Theme;
    public bool IsCategoriesSelected => SelectedTab == SettingsTab.Categories;
    public bool IsSettingsSelected   => SelectedTab == SettingsTab.Settings;
    public bool IsDebugSelected     => SelectedTab == SettingsTab.Debug;
    public bool IsAboutSelected     => SelectedTab == SettingsTab.About;

    public string AppVersion { get; }

    partial void OnSelectedTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsUploadersSelected));
        OnPropertyChanged(nameof(IsHotkeysSelected));
        OnPropertyChanged(nameof(IsCaptureSelected));
        OnPropertyChanged(nameof(IsThemeSelected));
        OnPropertyChanged(nameof(IsCategoriesSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
        OnPropertyChanged(nameof(IsDebugSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        if (value == SettingsTab.Uploaders) _ = Uploaders.ReloadAsync();
        if (value == SettingsTab.Hotkeys) _ = Hotkeys.ReloadAsync();
    }

    [RelayCommand] private void ShowUploaders() => SelectedTab = SettingsTab.Uploaders;
    [RelayCommand] private void ShowHotkeys()   => SelectedTab = SettingsTab.Hotkeys;
    [RelayCommand] private void ShowCapture()   => SelectedTab = SettingsTab.Capture;
    [RelayCommand] private void ShowTheme()      => SelectedTab = SettingsTab.Theme;
    [RelayCommand] private void ShowCategories() => SelectedTab = SettingsTab.Categories;
    [RelayCommand] private void ShowSettings()   => SelectedTab = SettingsTab.Settings;
    [RelayCommand] private void ShowDebug()     => SelectedTab = SettingsTab.Debug;
    [RelayCommand] private void ShowAbout()     => SelectedTab = SettingsTab.About;
}

public enum SettingsTab { Uploaders, Hotkeys, Capture, Theme, Categories, Settings, Debug, About }
