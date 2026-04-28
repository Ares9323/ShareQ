using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Plugins;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ShareQ.App.ViewModels;

/// <summary>
/// One row in the Settings → Plugins list. Wraps a <see cref="PluginDescriptor"/> + its enabled
/// state, persists changes to <see cref="PluginRegistry"/>, and handles uninstall for external
/// plugins (deleting the folder; the registry will simply not load it next launch).
/// </summary>
public sealed partial class PluginItemViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;
    private bool _suppressPersist;

    public PluginItemViewModel(PluginDescriptor descriptor, bool isEnabled, PluginRegistry registry)
    {
        _registry = registry;
        Id = descriptor.Id;
        DisplayName = descriptor.DisplayName;
        Version = descriptor.Version;
        Description = descriptor.Manifest.Description;
        Author = descriptor.Manifest.Author;
        RepositoryUrl = descriptor.Manifest.RepositoryUrl;
        BuiltIn = descriptor.BuiltIn;
        FolderPath = descriptor.FolderPath;
        _suppressPersist = true;
        IsEnabled = isEnabled;
        _suppressPersist = false;
    }

    public string Id { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string? Description { get; }
    public string? Author { get; }
    public string? RepositoryUrl { get; }
    public bool BuiltIn { get; }
    public string? FolderPath { get; }

    public string Subtitle
    {
        get
        {
            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(Description)) parts.Add(Description);
            if (!BuiltIn) parts.Add($"v{Version}");
            if (!string.IsNullOrEmpty(Author)) parts.Add($"by {Author}");
            return string.Join(" · ", parts);
        }
    }

    public string Badge => BuiltIn ? "Built-in" : "External";

    public bool CanUninstall => !BuiltIn && !string.IsNullOrEmpty(FolderPath);

    [ObservableProperty]
    private bool _isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppressPersist) return;
        _ = _registry.SetEnabledAsync(Id, value, CancellationToken.None);
    }

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private void Uninstall()
    {
        if (!CanUninstall) return;
        var confirm = MessageBox.Show(
            $"Delete the plugin folder for '{DisplayName}'?\n\n{FolderPath}\n\nThe plugin will stay loaded for this session and disappear after restart.",
            "Uninstall plugin",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;
        try
        {
            if (Directory.Exists(FolderPath)) Directory.Delete(FolderPath!, recursive: true);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to delete the plugin folder:\n{ex.Message}",
                "Uninstall plugin", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
