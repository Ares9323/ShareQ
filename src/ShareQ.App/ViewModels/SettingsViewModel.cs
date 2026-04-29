using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Plugins;
using ShareQ.PluginContracts;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;
    private readonly IPluginConfigStoreFactory _configFactory;

    public SettingsViewModel(
        PluginRegistry registry,
        IPluginConfigStoreFactory configFactory,
        UploadersViewModel uploaders,
        HotkeysViewModel hotkeys,
        CaptureDefaultsViewModel capture,
        WorkflowsViewModel workflows)
    {
        _registry = registry;
        _configFactory = configFactory;
        Uploaders = uploaders;
        // Hotkeys "Edit workflow" link → switch to the Workflows tab and select the matching row.
        hotkeys.OpenWorkflowRequested += (_, id) =>
        {
            SelectedTab = SettingsTab.Workflows;
            var match = workflows.Workflows.FirstOrDefault(w => w.Id == id);
            if (match is not null) workflows.SelectedWorkflow = match;
        };
        // Hotkeys "Add custom workflow" button → jump to Workflows tab and run the Add flow.
        // The Add command opens the name dialog and selects the new row on success, so the user
        // lands on Workflows ready to configure the new workflow's steps.
        hotkeys.AddCustomWorkflowRequested += async (_, _) =>
        {
            SelectedTab = SettingsTab.Workflows;
            // Reload first so the dropdown is fresh, then trigger Add. The order matters because
            // ReloadWorkflowsAsync may reset SelectedWorkflow; running Add after the reload means
            // the new entry's selection sticks.
            await workflows.ReloadWorkflowsAsync().ConfigureAwait(true);
            workflows.AddWorkflowCommand.Execute(null);
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
        if (value == SettingsTab.Workflows) _ = Workflows.ReloadWorkflowsAsync();
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
            var uploader = _registry.GetUploader(descriptor.Id);
            Plugins.Add(new PluginItemViewModel(descriptor, enabled, _registry, uploader, _configFactory));
        }
    }
}

public enum SettingsTab { Home, Plugins, Uploaders, Hotkeys, Capture, Workflows, About }
