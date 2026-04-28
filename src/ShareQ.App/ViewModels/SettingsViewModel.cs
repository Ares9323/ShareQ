using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Plugins;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;

    public SettingsViewModel(PluginRegistry registry, UploadersViewModel uploaders, HotkeysViewModel hotkeys, CaptureDefaultsViewModel capture)
    {
        _registry = registry;
        Uploaders = uploaders;
        Hotkeys = hotkeys;
        Capture = capture;
        Plugins = [];
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "dev";
        _ = LoadPluginsAsync();
    }

    public ObservableCollection<PluginItemViewModel> Plugins { get; }
    public UploadersViewModel Uploaders { get; }
    public HotkeysViewModel Hotkeys { get; }
    public CaptureDefaultsViewModel Capture { get; }

    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.Home;

    public bool IsHomeSelected      => SelectedTab == SettingsTab.Home;
    public bool IsPluginsSelected   => SelectedTab == SettingsTab.Plugins;
    public bool IsUploadersSelected => SelectedTab == SettingsTab.Uploaders;
    public bool IsHotkeysSelected   => SelectedTab == SettingsTab.Hotkeys;
    public bool IsCaptureSelected   => SelectedTab == SettingsTab.Capture;
    public bool IsAboutSelected     => SelectedTab == SettingsTab.About;

    public string AppVersion { get; }

    partial void OnSelectedTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsPluginsSelected));
        OnPropertyChanged(nameof(IsUploadersSelected));
        OnPropertyChanged(nameof(IsHotkeysSelected));
        OnPropertyChanged(nameof(IsCaptureSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
        // Refresh the uploaders view so plugin enable/disable changes from the Plugins tab are
        // reflected (greyed-out checkboxes for disabled plugins).
        if (value == SettingsTab.Uploaders) _ = Uploaders.ReloadAsync();
        if (value == SettingsTab.Hotkeys) _ = Hotkeys.ReloadAsync();
    }

    [RelayCommand] private void ShowHome()      => SelectedTab = SettingsTab.Home;
    [RelayCommand] private void ShowPlugins()   => SelectedTab = SettingsTab.Plugins;
    [RelayCommand] private void ShowUploaders() => SelectedTab = SettingsTab.Uploaders;
    [RelayCommand] private void ShowHotkeys()   => SelectedTab = SettingsTab.Hotkeys;
    [RelayCommand] private void ShowCapture()   => SelectedTab = SettingsTab.Capture;
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

public enum SettingsTab { Home, Plugins, Uploaders, Hotkeys, Capture, About }
