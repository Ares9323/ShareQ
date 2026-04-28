using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Plugins;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;

    public SettingsViewModel(
        PluginRegistry registry,
        UploadersViewModel uploaders,
        HotkeysViewModel hotkeys,
        CaptureDefaultsViewModel capture,
        WorkflowsViewModel workflows)
    {
        _registry = registry;
        Uploaders = uploaders;
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

    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.Home;

    public bool IsHomeSelected      => SelectedTab == SettingsTab.Home;
    public bool IsPluginsSelected   => SelectedTab == SettingsTab.Plugins;
    public bool IsUploadersSelected => SelectedTab == SettingsTab.Uploaders;
    public bool IsHotkeysSelected   => SelectedTab == SettingsTab.Hotkeys;
    public bool IsCaptureSelected   => SelectedTab == SettingsTab.Capture;
    public bool IsWorkflowsSelected => SelectedTab == SettingsTab.Workflows;
    public bool IsAboutSelected     => SelectedTab == SettingsTab.About;

    public string AppVersion { get; }

    partial void OnSelectedTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsPluginsSelected));
        OnPropertyChanged(nameof(IsUploadersSelected));
        OnPropertyChanged(nameof(IsHotkeysSelected));
        OnPropertyChanged(nameof(IsCaptureSelected));
        OnPropertyChanged(nameof(IsWorkflowsSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        if (value == SettingsTab.Uploaders) _ = Uploaders.ReloadAsync();
        if (value == SettingsTab.Hotkeys) _ = Hotkeys.ReloadAsync();
        if (value == SettingsTab.Workflows) _ = Workflows.ReloadAsync();
    }

    [RelayCommand] private void ShowHome()      => SelectedTab = SettingsTab.Home;
    [RelayCommand] private void ShowPlugins()   => SelectedTab = SettingsTab.Plugins;
    [RelayCommand] private void ShowUploaders() => SelectedTab = SettingsTab.Uploaders;
    [RelayCommand] private void ShowHotkeys()   => SelectedTab = SettingsTab.Hotkeys;
    [RelayCommand] private void ShowCapture()   => SelectedTab = SettingsTab.Capture;
    [RelayCommand] private void ShowWorkflows() => SelectedTab = SettingsTab.Workflows;
    [RelayCommand] private void ShowAbout()     => SelectedTab = SettingsTab.About;

    private async Task LoadPluginsAsync()
    {
        Plugins.Clear();
        foreach (var descriptor in _registry.AllDescriptors())
        {
            var enabled = await _registry.IsEnabledAsync(descriptor.Id, CancellationToken.None).ConfigureAwait(true);
            Plugins.Add(new PluginItemViewModel(descriptor, enabled, _registry));
        }
    }
}

public enum SettingsTab { Home, Plugins, Uploaders, Hotkeys, Capture, Workflows, About }
