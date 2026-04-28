using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Plugins;

namespace ShareQ.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;

    public SettingsViewModel(PluginRegistry registry)
    {
        _registry = registry;
        Plugins = [];
        AppVersion = typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "dev";
        _ = LoadPluginsAsync();
    }

    public ObservableCollection<PluginItemViewModel> Plugins { get; }

    [ObservableProperty]
    private SettingsTab _selectedTab = SettingsTab.Home;

    public bool IsHomeSelected => SelectedTab == SettingsTab.Home;
    public bool IsPluginsSelected => SelectedTab == SettingsTab.Plugins;
    public bool IsAboutSelected => SelectedTab == SettingsTab.About;

    public string AppVersion { get; }

    partial void OnSelectedTabChanged(SettingsTab value)
    {
        OnPropertyChanged(nameof(IsHomeSelected));
        OnPropertyChanged(nameof(IsPluginsSelected));
        OnPropertyChanged(nameof(IsAboutSelected));
    }

    [RelayCommand] private void ShowHome()    => SelectedTab = SettingsTab.Home;
    [RelayCommand] private void ShowPlugins() => SelectedTab = SettingsTab.Plugins;
    [RelayCommand] private void ShowAbout()   => SelectedTab = SettingsTab.About;

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

public enum SettingsTab { Home, Plugins, About }
