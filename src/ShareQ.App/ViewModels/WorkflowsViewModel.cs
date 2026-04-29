using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Hotkeys;
using ShareQ.App.Windows;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Profiles;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the Settings → Workflows tab. Lists every pipeline profile (built-in + custom) loaded
/// from <see cref="IPipelineProfileStore"/>; the editor below mutates the currently-selected one
/// via <see cref="WorkflowEditorViewModel"/>. Owns the CRUD toolbar (add / rename / duplicate /
/// remove / reset-all) — the data layer just persists; the routing back to the keyboard hook
/// happens through <see cref="HotkeyConfigService"/>.
/// </summary>
public sealed partial class WorkflowsViewModel : ObservableObject
{
    /// <summary>Friendlier descriptions for the built-in profiles where the bare <c>Trigger</c>
    /// string isn't very readable. Custom profiles fall back to the trigger as description.</summary>
    private static readonly Dictionary<string, string> BuiltInDescriptions = new(StringComparer.Ordinal)
    {
        [DefaultPipelineProfiles.RegionCaptureId] =
            "Triggered by the configured hotkey or the tray Capture menu. Default order: editor → save → history → clipboard → upload → URL → toast.",
        [DefaultPipelineProfiles.ManualUploadId] =
            "Triggered by tray → Upload (Upload file… / Upload from clipboard). Skips save-to-disk and image-to-clipboard since the source is already there.",
        [DefaultPipelineProfiles.OnClipboardId] =
            "Runs every time something new lands on the system clipboard so it gets indexed in history.",
    };

    private readonly IPipelineProfileStore _profiles;
    private readonly PipelineProfileSeeder _seeder;
    private readonly HotkeyConfigService _hotkeys;

    public WorkflowsViewModel(IPipelineProfileStore profiles, PipelineProfileSeeder seeder, HotkeyConfigService hotkeys, WorkflowEditorViewModel editor)
    {
        _profiles = profiles;
        _seeder = seeder;
        _hotkeys = hotkeys;
        Editor = editor;
        Workflows = [];
        _ = ReloadWorkflowsAsync();
    }

    public ObservableCollection<WorkflowOption> Workflows { get; }
    public WorkflowEditorViewModel Editor { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RenameWorkflowCommand))]
    [NotifyCanExecuteChangedFor(nameof(DuplicateWorkflowCommand))]
    [NotifyCanExecuteChangedFor(nameof(RemoveWorkflowCommand))]
    private WorkflowOption? _selectedWorkflow;

    partial void OnSelectedWorkflowChanged(WorkflowOption? value)
    {
        if (value is null) return;
        _ = Editor.LoadAsync(value.Id);
    }

    /// <summary>(Re)load the workflow list from the store. Tries to keep the current selection by
    /// id; falls back to the first row when the previous selection was deleted.</summary>
    public async Task ReloadWorkflowsAsync()
    {
        var stored = await _profiles.ListAsync(CancellationToken.None).ConfigureAwait(true);
        var previousSelectedId = SelectedWorkflow?.Id;

        Workflows.Clear();
        foreach (var profile in stored.OrderBy(p => p.IsBuiltIn ? 0 : 1).ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var description = BuiltInDescriptions.TryGetValue(profile.Id, out var d)
                ? d
                : profile.Trigger;
            Workflows.Add(new WorkflowOption(profile.Id, profile.DisplayName, description, profile.IsBuiltIn));
        }

        var nextSelection = previousSelectedId is null
            ? null
            : Workflows.FirstOrDefault(w => w.Id == previousSelectedId);
        SelectedWorkflow = nextSelection
                       ?? Workflows.FirstOrDefault(w => w.Id == DefaultPipelineProfiles.RegionCaptureId)
                       ?? Workflows.FirstOrDefault();
    }

    public Task ReloadAsync()
    {
        var current = SelectedWorkflow ?? Workflows.FirstOrDefault();
        if (current is null) return Task.CompletedTask;
        return Editor.LoadAsync(current.Id);
    }

    [RelayCommand]
    private async Task AddWorkflowAsync()
    {
        var dialog = new WorkflowNameDialog("Add workflow", "New workflow")
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true) return;

        // Generate a stable, collision-free id. "custom-" prefix lets debug logs distinguish
        // user-created profiles at a glance from the kebab-case built-in ids.
        var id = $"custom-{Guid.NewGuid():N}";
        var profile = new PipelineProfile(
            Id: id,
            DisplayName: dialog.ResultName,
            Trigger: $"hotkey:{id}",
            Steps: [],
            IsBuiltIn: false);
        await _profiles.UpsertAsync(profile, CancellationToken.None).ConfigureAwait(true);
        await ReloadWorkflowsAsync().ConfigureAwait(true);
        SelectedWorkflow = Workflows.FirstOrDefault(w => w.Id == id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameWorkflowAsync()
    {
        if (SelectedWorkflow is not { } current) return;
        var dialog = new WorkflowNameDialog("Rename workflow", current.DisplayName)
        {
            Owner = Application.Current.MainWindow,
        };
        if (dialog.ShowDialog() != true) return;
        if (string.Equals(dialog.ResultName, current.DisplayName, StringComparison.Ordinal)) return;

        var profile = await _profiles.GetAsync(current.Id, CancellationToken.None).ConfigureAwait(true);
        if (profile is null) return;
        var updated = profile with { DisplayName = dialog.ResultName };
        await _profiles.UpsertAsync(updated, CancellationToken.None).ConfigureAwait(true);
        await ReloadWorkflowsAsync().ConfigureAwait(true);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DuplicateWorkflowAsync()
    {
        if (SelectedWorkflow is not { } current) return;
        var source = await _profiles.GetAsync(current.Id, CancellationToken.None).ConfigureAwait(true);
        if (source is null) return;

        var newId = $"custom-{Guid.NewGuid():N}";
        var copy = source with
        {
            Id = newId,
            DisplayName = $"{source.DisplayName} (copy)",
            Trigger = $"hotkey:{newId}",
            // Don't carry the hotkey binding to the copy — two profiles on the same combo would
            // race; user re-binds the duplicate explicitly if they want one.
            Hotkey = null,
            // The copy is always user-editable, even if cloned from a built-in.
            IsBuiltIn = false,
        };
        await _profiles.UpsertAsync(copy, CancellationToken.None).ConfigureAwait(true);
        await ReloadWorkflowsAsync().ConfigureAwait(true);
        SelectedWorkflow = Workflows.FirstOrDefault(w => w.Id == newId);
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    private async Task RemoveWorkflowAsync()
    {
        if (SelectedWorkflow is not { } current) return;
        if (current.IsBuiltIn) return;
        var confirm = MessageBox.Show(
            $"Delete workflow '{current.DisplayName}'?\n\nThis can't be undone. Built-in workflows aren't affected.",
            "Delete workflow",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        // Unregister the hotkey hook BEFORE deleting the profile — once the profile is gone,
        // ClearAsync would fail to find it. NotifyHotkeyRemoved is a fire-and-forget signal.
        _hotkeys.NotifyHotkeyRemoved(current.Id);
        await _profiles.DeleteAsync(current.Id, CancellationToken.None).ConfigureAwait(true);
        await ReloadWorkflowsAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ResetAllToDefaultsAsync()
    {
        var confirm = MessageBox.Show(
            "Reset all built-in workflows to their default steps and hotkeys?\n\n" +
            "Custom workflows you've added are NOT touched. This is meant for recovering from " +
            "a built-in workflow that's been misconfigured to the point of being unusable.",
            "Reset built-ins",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;

        foreach (var profile in DefaultPipelineProfiles.All)
        {
            await _seeder.ResetToDefaultsAsync(profile.Id, CancellationToken.None).ConfigureAwait(true);
            // Re-emit the hotkey so the runtime hook re-registers with the default binding.
            if (profile.Hotkey is { } binding)
                _hotkeys.NotifyHotkeyRemoved(profile.Id); // unregister first; the next event will rebind
            // Now read back and emit Changed with the actual binding to register fresh. Simpler:
            // just kick the routine via ClearAsync→re-bind isn't worth the dance, so trigger a
            // synthetic Changed event ourselves with the seeded values.
            if (profile.Hotkey is { } b)
                _hotkeys.NotifyHotkeyRebound(profile.Id, (ShareQ.Hotkeys.HotkeyModifiers)b.Modifiers, b.VirtualKey);
        }
        await ReloadWorkflowsAsync().ConfigureAwait(true);
    }

    private bool HasSelection() => SelectedWorkflow is not null;
    private bool CanRemove() => SelectedWorkflow is { IsBuiltIn: false };
}

/// <summary>One row in the Workflows list. <paramref name="IsBuiltIn"/> drives the gating of the
/// Remove command — built-ins can be reset but not deleted.</summary>
public sealed record WorkflowOption(string Id, string DisplayName, string Description, bool IsBuiltIn);
