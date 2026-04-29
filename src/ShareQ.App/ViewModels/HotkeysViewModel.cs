using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Hotkeys;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the Settings → Hotkeys tab. Two view modes:
///   - <b>List mode</b> (default): grouped list of every workflow with a hotkey, plus an
///     "Add custom workflow" button at the bottom.
///   - <b>Edit mode</b>: a single-workflow detail view with inline rename, Duplicate / Remove /
///     Reset buttons and the pipeline-step editor. Triggered by clicking Edit on a row; the
///     Back button returns to list mode.
///
/// The edit view is the spiritual successor of the old standalone Workflows tab — folded in
/// here so the user has one entry point for everything workflow-related.
/// </summary>
public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly HotkeyConfigService _config;
    private readonly WorkflowsViewModel _workflows;

    public HotkeysViewModel(HotkeyConfigService config, WorkflowsViewModel workflows)
    {
        _config = config;
        _workflows = workflows;
        BuiltInItems = [];
        CustomItems = [];
        // When a workflow is deleted (only customs can be), bail out of edit view back to the
        // list — otherwise we'd be sitting on a stale editor for a profile that's gone.
        _workflows.WorkflowDeleted += async (_, _) =>
        {
            IsEditingWorkflow = false;
            await ReloadAsync().ConfigureAwait(true);
        };
        // Inline rename in edit view: refresh the row list so the new DisplayName shows when the
        // user clicks Back (or even live, if they switch back to the list manually).
        _workflows.WorkflowDisplayNameChanged += async (_, _) => await ReloadAsync().ConfigureAwait(true);
        _ = ReloadAsync();
    }

    public ObservableCollection<HotkeyItemViewModel> BuiltInItems { get; }
    public ObservableCollection<HotkeyItemViewModel> CustomItems { get; }

    /// <summary>Pass-through to the workflows view model so the edit view can bind to the same
    /// SelectedWorkflow / EditingDisplayName / Editor surface that used to live in its own tab.</summary>
    public WorkflowsViewModel Workflows => _workflows;

    /// <summary>True when the user has clicked Edit on a row → the list view is hidden and the
    /// edit view replaces it. Back returns to false.</summary>
    [ObservableProperty]
    private bool _isEditingWorkflow;

    /// <summary>Raised by the "+ Add custom workflow" button. Caller (Settings VM) runs the
    /// Workflows.AddWorkflowCommand and then enters edit mode on the new row.</summary>
    public event EventHandler? AddCustomWorkflowRequested;

    /// <summary>Raised right after we enter edit mode on a freshly-created workflow — the window
    /// listens to focus + SelectAll the inline name TextBox so the user can rename by just
    /// typing. Plain Edit clicks on existing rows don't fire this; they leave the text alone.</summary>
    public event EventHandler? EditNameFocusRequested;

    public async Task ReloadAsync()
    {
        BuiltInItems.Clear();
        CustomItems.Clear();
        var catalog = await _config.GetCatalogAsync(CancellationToken.None).ConfigureAwait(true);
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
                openInWorkflows: id => _ = BeginEditAsync(id));
            (entry.IsBuiltIn ? BuiltInItems : CustomItems).Add(item);
        }
    }

    /// <summary>Enter edit mode on the workflow with the given id. Selects it in the underlying
    /// WorkflowsViewModel so the editor + inline rename + Duplicate/Remove/Reset all bind to
    /// the right profile. Set <paramref name="focusName"/> when entering edit on a newly-created
    /// workflow — the window will focus the inline name field and SelectAll so the user can
    /// type the real name immediately without first clearing the placeholder.</summary>
    public async Task BeginEditAsync(string id, bool focusName = false)
    {
        var match = _workflows.Workflows.FirstOrDefault(w => w.Id == id);
        if (match is null)
        {
            // The workflow list might be stale (e.g. just added). Refresh and retry once.
            await _workflows.ReloadWorkflowsAsync().ConfigureAwait(true);
            match = _workflows.Workflows.FirstOrDefault(w => w.Id == id);
            if (match is null) return;
        }
        _workflows.SelectedWorkflow = match;
        IsEditingWorkflow = true;
        if (focusName) EditNameFocusRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private async Task BackToList()
    {
        // Commit any pending inline rename FIRST. The TextBox's LostFocus fires when the user
        // clicks Back / hits Esc, but it's a fire-and-forget save — without explicitly awaiting
        // here we'd race the upcoming ReloadAsync and pull stale names from the store.
        await _workflows.SaveDisplayNameAsync().ConfigureAwait(true);
        IsEditingWorkflow = false;
        // Refresh the list — the user may have renamed / removed / duplicated while editing.
        await ReloadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void AddCustomWorkflow() => AddCustomWorkflowRequested?.Invoke(this, EventArgs.Empty);

    private void RefreshNoOp() { /* placeholder for future global refresh hook (e.g. clear duplicate flag) */ }
}
