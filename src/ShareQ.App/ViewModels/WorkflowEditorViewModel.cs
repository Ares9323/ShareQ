using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Profiles;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Edits the step list of a single pipeline profile (region-capture, manual-upload, …). The list
/// shows only steps that are actually in the profile (no synthetic defaults). User mutations —
/// add via the categorized "+ Add step" picker, remove, reorder, mute via toggle — write through
/// to <see cref="IPipelineProfileStore"/> immediately. Plumbing-tagged tasks
/// (<see cref="WorkflowActionDescriptor.IsPlumbing"/>) stay in the underlying profile but are
/// hidden from the editor.
/// </summary>
public sealed partial class WorkflowEditorViewModel : ObservableObject
{
    private readonly IPipelineProfileStore _profiles;
    private readonly PipelineProfileSeeder _seeder;
    private readonly WorkflowActionProvider _actions;

    /// <summary>Mutable mirror of the persisted profile's Steps. Mutation order: edit this list,
    /// then <see cref="SyncItemsFromStorage"/> rebuilds the UI list, then save the profile.</summary>
    private List<PipelineStep> _storage = [];
    /// <summary>Snapshot of the action catalog (static + per-uploader-enabled) loaded each time
    /// a profile is opened. Drives both the "+ Add step" picker and the per-step display name.</summary>
    private IReadOnlyList<WorkflowActionDescriptor> _descriptors = WorkflowActionCatalog.All;
    private string? _profileId;
    private bool _isReloading;

    /// <summary>True when the currently-loaded profile is a built-in. The "Reset to defaults"
    /// button binds Visibility to this — custom workflows have no seeded default so the button
    /// has nothing meaningful to do (it would crash trying to look up a non-existent default).</summary>
    [ObservableProperty]
    private bool _canResetToDefaults;

    public WorkflowEditorViewModel(IPipelineProfileStore profiles, PipelineProfileSeeder seeder, WorkflowActionProvider actions)
    {
        _profiles = profiles;
        _seeder = seeder;
        _actions = actions;
        Items = [];
        AddableActions = [];
    }

    public ObservableCollection<WorkflowStepViewModel> Items { get; }

    /// <summary>Categorized list of actions for the "+ Add step" menu (sorted by category then name).
    /// Mutated in <see cref="LoadAsync"/> when uploader plug-ins toggle on/off; the menu is built
    /// on click so it always reads the current value without needing INotifyPropertyChanged.</summary>
    public IReadOnlyList<WorkflowActionGroup> AddableActions { get; private set; }

    public string? ProfileId => _profileId;

    public async Task LoadAsync(string profileId)
    {
        ArgumentException.ThrowIfNullOrEmpty(profileId);
        _profileId = profileId;
        _isReloading = true;
        try
        {
            // Refresh the action catalog snapshot — uploader plug-ins may have been toggled since
            // the last open. This decides what's in the picker and how each step's name renders.
            _descriptors = await _actions.GetAllAsync(CancellationToken.None).ConfigureAwait(true);
            AddableActions = _descriptors
                .Where(a => !a.IsPlumbing)
                .GroupBy(a => a.Category)
                .Select(g => new WorkflowActionGroup(g.Key, g.OrderBy(a => a.DisplayName).ToList()))
                .OrderBy(g => g.Category)
                .ToList();

            var profile = await _profiles.GetAsync(profileId, CancellationToken.None).ConfigureAwait(true);
            _storage = profile?.Steps.ToList() ?? [];
            CanResetToDefaults = profile?.IsBuiltIn ?? false;
            SyncItemsFromStorage();
        }
        finally { _isReloading = false; }
    }

    [RelayCommand]
    private async Task AddStepAsync(WorkflowActionDescriptor? descriptor)
    {
        if (_isReloading || _profileId is null || descriptor is null) return;
        var config = string.IsNullOrEmpty(descriptor.DefaultConfigJson) ? null : JsonNode.Parse(descriptor.DefaultConfigJson);
        // New steps get a Guid id so multiple instances of the same task can co-exist (e.g. "Toggle
        // incognito ON" then later "Toggle incognito OFF" in the same workflow).
        _storage.Add(new PipelineStep(descriptor.TaskId, Config: config, Id: Guid.NewGuid().ToString("N")));
        SyncItemsFromStorage();
        await PersistAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        if (_profileId is null) return;
        // Defensive guard: the button is hidden for custom workflows but a stale binding could
        // still trigger this. The seeder would throw on a non-built-in id, so refuse early.
        if (!CanResetToDefaults) return;
        var confirm = MessageBox.Show(
            $"Reset the '{_profileId}' workflow to its default steps and order?\n\nThis discards any custom additions, removals, reordering or per-step toggles you've made here.",
            "Reset workflow",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        await _seeder.ResetToDefaultsAsync(_profileId, CancellationToken.None).ConfigureAwait(true);
        await LoadAsync(_profileId).ConfigureAwait(true);
    }

    private void SyncItemsFromStorage()
    {
        Items.Clear();
        for (var i = 0; i < _storage.Count; i++)
        {
            var step = _storage[i];
            var descriptor = WorkflowActionCatalog.LookupForStep(_descriptors, step);
            if (descriptor?.IsPlumbing == true) continue; // hidden plumbing
            var display = descriptor?.DisplayName ?? step.TaskId;
            var description = descriptor?.Description ?? $"Custom task ({step.TaskId})";
            var category = descriptor?.Category;
            var parameter = descriptor?.IntParameter;
            var parameterValue = parameter is null
                ? 0
                : (int?)step.Config?[parameter.Key] ?? parameter.DefaultValue;
            // Build a snapshot of every bool-parameter's current value for this step. Falls back
            // to the descriptor default when the key isn't yet in step.Config.
            Dictionary<string, bool>? boolValues = null;
            if (descriptor?.BoolParameters is { Count: > 0 } bools)
            {
                boolValues = new Dictionary<string, bool>(bools.Count);
                foreach (var bp in bools)
                {
                    boolValues[bp.Key] = (bool?)step.Config?[bp.Key] ?? bp.DefaultValue;
                }
            }
            Items.Add(new WorkflowStepViewModel(
                storageIndex: i,
                taskId: step.TaskId,
                displayName: display,
                description: description,
                category: category,
                initiallyEnabled: step.Enabled,
                parameter: parameter,
                parameterValue: parameterValue,
                boolParameters: descriptor?.BoolParameters,
                boolParameterValues: boolValues,
                onEnabledChanged: (item, value)   => _ = OnEnabledChangedAsync(item, value),
                onMove:           (item, delta)   => _ = OnMoveAsync(item, delta),
                onRemove:         item            => _ = OnRemoveAsync(item),
                onParameterChanged: (item, value) => _ = OnParameterChangedAsync(item, value),
                onBoolParameterChanged: (item, key, value) => _ = OnBoolParameterChangedAsync(item, key, value)));
        }
        UpdateMoveFlags();
    }

    private async Task OnEnabledChangedAsync(WorkflowStepViewModel item, bool value)
    {
        if (_isReloading || _profileId is null) return;
        if (item.StorageIndex < 0 || item.StorageIndex >= _storage.Count) return;
        _storage[item.StorageIndex] = _storage[item.StorageIndex] with { Enabled = value };
        await PersistAsync().ConfigureAwait(true);
    }

    private async Task OnParameterChangedAsync(WorkflowStepViewModel item, int value)
    {
        if (_isReloading || _profileId is null) return;
        if (item.StorageIndex < 0 || item.StorageIndex >= _storage.Count) return;
        if (item.Parameter is null) return;
        var step = _storage[item.StorageIndex];
        var config = step.Config?.DeepClone() as JsonObject ?? new JsonObject();
        config[item.Parameter.Key] = value;
        _storage[item.StorageIndex] = step with { Config = config };
        await PersistAsync().ConfigureAwait(true);
    }

    private async Task OnBoolParameterChangedAsync(WorkflowStepViewModel item, string key, bool value)
    {
        if (_isReloading || _profileId is null) return;
        if (item.StorageIndex < 0 || item.StorageIndex >= _storage.Count) return;
        var step = _storage[item.StorageIndex];
        var config = step.Config?.DeepClone() as JsonObject ?? new JsonObject();
        config[key] = value;
        _storage[item.StorageIndex] = step with { Config = config };
        await PersistAsync().ConfigureAwait(true);
    }

    private async Task OnMoveAsync(WorkflowStepViewModel item, int delta)
    {
        if (_isReloading || _profileId is null) return;
        var src = item.StorageIndex;
        if (src < 0 || src >= _storage.Count) return;
        // Find the destination storage index by skipping over plumbing-only neighbours.
        var dst = FindUiAdjacentStorageIndex(src, delta);
        if (dst < 0) return;
        var moving = _storage[src];
        _storage.RemoveAt(src);
        // Account for index shift after removal.
        if (dst > src) dst--;
        _storage.Insert(dst, moving);
        SyncItemsFromStorage();
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>Reorder by dropping <paramref name="source"/> next to <paramref name="target"/>.
    /// When <paramref name="insertAfter"/> is true the source lands immediately below the target,
    /// otherwise above. Both arguments are UI rows from <see cref="Items"/>; the editor maps
    /// them back to storage indices and persists.</summary>
    public async Task MoveToAsync(WorkflowStepViewModel source, WorkflowStepViewModel target, bool insertAfter)
    {
        if (_isReloading || _profileId is null) return;
        if (ReferenceEquals(source, target)) return;
        var src = source.StorageIndex;
        var dst = target.StorageIndex;
        if (src < 0 || src >= _storage.Count) return;
        if (dst < 0 || dst >= _storage.Count) return;

        var moving = _storage[src];
        _storage.RemoveAt(src);
        // After RemoveAt, indices > src shift left by one. Translate the target row's storage
        // index into the post-removal coordinate system, then add 1 if dropping below it.
        if (dst > src) dst--;
        if (insertAfter) dst++;
        if (dst < 0) dst = 0;
        if (dst > _storage.Count) dst = _storage.Count;
        _storage.Insert(dst, moving);
        SyncItemsFromStorage();
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>True when the drag's drop position is "after the last row". Bound by the footer
    /// gutter element so the user sees the same single-line indicator at the bottom of the list.</summary>
    [ObservableProperty]
    private bool _isDropTargetAtEnd;

    /// <summary>Drop the source row at the very end of the storage list — used by the footer
    /// gutter when the user drops below the last row.</summary>
    public async Task MoveToEndAsync(WorkflowStepViewModel source)
    {
        if (_isReloading || _profileId is null) return;
        var src = source.StorageIndex;
        if (src < 0 || src >= _storage.Count) return;
        var moving = _storage[src];
        _storage.RemoveAt(src);
        _storage.Add(moving);
        SyncItemsFromStorage();
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>Reset all per-row drag visual flags. Called by the window when the drag operation
    /// ends (drop or Esc) so no stale highlights linger.</summary>
    public void ClearDragVisuals()
    {
        foreach (var item in Items)
        {
            item.IsDragSource = false;
            item.IsDropTargetAbove = false;
        }
        IsDropTargetAtEnd = false;
    }

    private async Task OnRemoveAsync(WorkflowStepViewModel item)
    {
        if (_isReloading || _profileId is null) return;
        if (item.StorageIndex < 0 || item.StorageIndex >= _storage.Count) return;
        _storage.RemoveAt(item.StorageIndex);
        SyncItemsFromStorage();
        await PersistAsync().ConfigureAwait(true);
    }

    /// <summary>Walk <paramref name="storageIndex"/> by <paramref name="delta"/> UI-positions
    /// (skipping plumbing steps that aren't in <see cref="Items"/>) and return the resulting
    /// storage index, or -1 if we'd run off either end.</summary>
    private int FindUiAdjacentStorageIndex(int storageIndex, int delta)
    {
        var step = delta > 0 ? 1 : -1;
        var remaining = Math.Abs(delta);
        var i = storageIndex;
        while (remaining > 0)
        {
            i += step;
            if (i < 0 || i >= _storage.Count) return -1;
            var candidate = _storage[i];
            var descriptor = WorkflowActionCatalog.LookupForStep(_descriptors,candidate);
            if (descriptor?.IsPlumbing == true) continue;
            remaining--;
        }
        return i;
    }

    private async Task PersistAsync()
    {
        if (_profileId is null) return;
        var profile = await _profiles.GetAsync(_profileId, CancellationToken.None).ConfigureAwait(true);
        if (profile is null) return;
        var updated = profile with { Steps = _storage.ToList() };
        await _profiles.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(true);
    }

    private void UpdateMoveFlags()
    {
        for (var i = 0; i < Items.Count; i++)
        {
            Items[i].CanMoveUp = i > 0;
            Items[i].CanMoveDown = i < Items.Count - 1;
        }
    }
}

public sealed record WorkflowActionGroup(string Category, IReadOnlyList<WorkflowActionDescriptor> Actions);
