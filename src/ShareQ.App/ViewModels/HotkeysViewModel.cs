using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Hotkeys;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the Settings → Hotkeys list. Loads the catalog from <see cref="HotkeyConfigService"/>
/// each time the tab opens, splitting it into a built-in section and a custom section so the user
/// sees the separation at a glance. The "Edit workflow" link on each row raises
/// <see cref="OpenWorkflowRequested"/>; <c>SettingsViewModel</c> subscribes and switches to the
/// Workflows tab + selects the matching row.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly HotkeyConfigService _config;

    public HotkeysViewModel(HotkeyConfigService config)
    {
        _config = config;
        BuiltInItems = [];
        CustomItems = [];
        _ = ReloadAsync();
    }

    public ObservableCollection<HotkeyItemViewModel> BuiltInItems { get; }
    public ObservableCollection<HotkeyItemViewModel> CustomItems { get; }

    /// <summary>Raised by an item row when the user clicks "Edit workflow" — caller (Settings VM)
    /// switches the active tab and selects the workflow there.</summary>
    public event EventHandler<string>? OpenWorkflowRequested;

    /// <summary>Raised by the "Add custom workflow" button at the bottom of the Custom section —
    /// caller switches to the Workflows tab and triggers Add.</summary>
    public event EventHandler? AddCustomWorkflowRequested;

    [RelayCommand]
    private void AddCustomWorkflow() => AddCustomWorkflowRequested?.Invoke(this, EventArgs.Empty);

    public async Task ReloadAsync()
    {
        BuiltInItems.Clear();
        CustomItems.Clear();
        var catalog = await _config.GetCatalogAsync(CancellationToken.None).ConfigureAwait(true);
        // Alphabetical inside each section (built-in / custom). Without this the rows appear in
        // store insertion order, which is essentially random for the user.
        var sorted = catalog.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var entry in sorted)
        {
            var current = await _config.GetEffectiveAsync(entry.Id, CancellationToken.None).ConfigureAwait(true);
            var item = new HotkeyItemViewModel(
                entry.Id,
                entry.DisplayName,
                entry.IsBuiltIn,
                current,
                _config,
                refreshList: RefreshNoOp,
                openInWorkflows: id => OpenWorkflowRequested?.Invoke(this, id));
            (entry.IsBuiltIn ? BuiltInItems : CustomItems).Add(item);
        }
    }

    private void RefreshNoOp() { /* placeholder for future global refresh hook (e.g. clear duplicate flag) */ }
}
