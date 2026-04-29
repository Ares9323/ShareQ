using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services;
using ShareQ.App.Services.Plugins;
using ShareQ.PluginContracts;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;
    private readonly IPluginConfigStoreFactory _configFactory;
    private readonly AutostartService _autostart;

    public SettingsViewModel(
        PluginRegistry registry,
        IPluginConfigStoreFactory configFactory,
        AutostartService autostart,
        UploadersViewModel uploaders,
        HotkeysViewModel hotkeys,
        CaptureDefaultsViewModel capture,
        WorkflowsViewModel workflows,
        ThemeViewModel theme,
        DebugViewModel debug)
    {
        _registry = registry;
        _configFactory = configFactory;
        _autostart = autostart;
        Theme = theme;
        Debug = debug;
        // Initial state mirrors the registry. Future toggles persist via OnStartWithWindowsChanged.
        _suppressAutostartPersist = true;
        StartWithWindows = autostart.IsEnabled;
        _suppressAutostartPersist = false;
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
        Plugins = [];
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "dev";
        _ = LoadPluginsAsync();
    }

    public ObservableCollection<PluginItemViewModel> Plugins { get; }
    public UploadersViewModel Uploaders { get; }
    public HotkeysViewModel Hotkeys { get; }
    public CaptureDefaultsViewModel Capture { get; }
    public WorkflowsViewModel Workflows { get; }
    public ThemeViewModel Theme { get; }
    public DebugViewModel Debug { get; }

    /// <summary>Non-null while the user is in the Plugins → Configure detail view. Drives the
    /// list/detail toggle in the Plugins XAML; cleared by <see cref="BackFromConfigCommand"/>.
    /// Same shape as Hotkeys' edit-view pattern — single tab, two sub-views.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConfiguringPlugin))]
    [NotifyCanExecuteChangedFor(nameof(BackFromConfigCommand))]
    private PluginConfigViewModel? _configuringPlugin;

    public bool IsConfiguringPlugin => ConfiguringPlugin is not null;

    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.Home;

    /// <summary>Bound to the Home-tab "Run ShareQ when Windows starts" checkbox. Reads / writes
    /// HKCU Run via <see cref="AutostartService"/>. Constructor seeds it from the current
    /// registry value (no flicker), then user toggles propagate immediately.</summary>
    [ObservableProperty]
    private bool _startWithWindows;

    private bool _suppressAutostartPersist;

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_suppressAutostartPersist) return;
        _autostart.SetEnabled(value);
    }

    public bool IsHomeSelected      => SelectedTab == SettingsTab.Home;
    public bool IsPluginsSelected   => SelectedTab == SettingsTab.Plugins;
    public bool IsUploadersSelected => SelectedTab == SettingsTab.Uploaders;
    public bool IsHotkeysSelected   => SelectedTab == SettingsTab.Hotkeys;
    public bool IsCaptureSelected   => SelectedTab == SettingsTab.Capture;
    public bool IsThemeSelected     => SelectedTab == SettingsTab.Theme;
    public bool IsDebugSelected     => SelectedTab == SettingsTab.Debug;
    public bool IsAboutSelected     => SelectedTab == SettingsTab.About;

    public string AppVersion { get; }

    partial void OnSelectedTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsPluginsSelected));
        OnPropertyChanged(nameof(IsUploadersSelected));
        OnPropertyChanged(nameof(IsHotkeysSelected));
        OnPropertyChanged(nameof(IsCaptureSelected));
        OnPropertyChanged(nameof(IsThemeSelected));
        OnPropertyChanged(nameof(IsDebugSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        if (value == SettingsTab.Uploaders) _ = Uploaders.ReloadAsync();
        if (value == SettingsTab.Hotkeys) _ = Hotkeys.ReloadAsync();
    }

    [RelayCommand] private void ShowHome()      => SelectedTab = SettingsTab.Home;
    [RelayCommand] private void ShowPlugins()   => SelectedTab = SettingsTab.Plugins;
    [RelayCommand] private void ShowUploaders() => SelectedTab = SettingsTab.Uploaders;
    [RelayCommand] private void ShowHotkeys()   => SelectedTab = SettingsTab.Hotkeys;
    [RelayCommand] private void ShowCapture()   => SelectedTab = SettingsTab.Capture;
    [RelayCommand] private void ShowTheme()     => SelectedTab = SettingsTab.Theme;
    [RelayCommand] private void ShowDebug()     => SelectedTab = SettingsTab.Debug;
    [RelayCommand] private void ShowAbout()     => SelectedTab = SettingsTab.About;

    /// <summary>Open the inline configure view for a plugin. Called by
    /// <see cref="PluginItemViewModel"/> via the openConfig callback wired up in
    /// <see cref="LoadPluginsAsync"/>.</summary>
    public async Task OpenPluginConfigAsync(PluginConfigViewModel vm)
    {
        ConfiguringPlugin = vm;
        await vm.LoadAsync(CancellationToken.None).ConfigureAwait(true);
    }

    private bool CanGoBackFromConfig() => IsConfiguringPlugin;

    [RelayCommand(CanExecute = nameof(CanGoBackFromConfig))]
    private void BackFromConfig() => ConfiguringPlugin = null;

    private async Task LoadPluginsAsync()
    {
        Plugins.Clear();
        foreach (var descriptor in _registry.AllDescriptors())
        {
            var enabled = await _registry.IsEnabledAsync(descriptor.Id, CancellationToken.None).ConfigureAwait(true);
            var uploader = _registry.GetUploader(descriptor.Id);
            Plugins.Add(new PluginItemViewModel(
                descriptor, enabled, _registry, uploader, _configFactory,
                openConfig: vm => _ = OpenPluginConfigAsync(vm)));
        }
    }
}

public enum SettingsTab { Home, Plugins, Uploaders, Hotkeys, Capture, Theme, Debug, About }
