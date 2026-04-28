using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShareQ.Pipeline.Profiles;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the Settings → Workflows tab. Lists every pipeline profile in a dropdown; the editor
/// below mutates the currently-selected profile via <see cref="WorkflowEditorViewModel"/>.
/// </summary>
public sealed partial class WorkflowsViewModel : ObservableObject
{
    private static readonly Dictionary<string, (string DisplayName, string Description)> WorkflowLabels = new(StringComparer.Ordinal)
    {
        [DefaultPipelineProfiles.RegionCaptureId] = ("Region capture",
            "Triggered by Ctrl+Alt+R or the tray Capture menu. Default order: editor → save → history → clipboard → upload → URL → toast."),
        [DefaultPipelineProfiles.ManualUploadId] = ("Manual upload",
            "Triggered by tray → Upload (Upload file… / Upload from clipboard). Skips save-to-disk and image-to-clipboard since the source is already there."),
        [DefaultPipelineProfiles.OnClipboardId] = ("On clipboard",
            "Runs every time something new lands on the system clipboard so it gets indexed in history."),
    };

    public WorkflowsViewModel(WorkflowEditorViewModel editor)
    {
        Editor = editor;
        Workflows = [];
        foreach (var profile in DefaultPipelineProfiles.All)
        {
            var (display, description) = WorkflowLabels.TryGetValue(profile.Id, out var l)
                ? l
                : (profile.DisplayName, profile.Trigger);
            Workflows.Add(new WorkflowOption(profile.Id, display, description));
        }
        SelectedWorkflow = Workflows.FirstOrDefault(w => w.Id == DefaultPipelineProfiles.RegionCaptureId)
                       ?? Workflows.FirstOrDefault();
    }

    public ObservableCollection<WorkflowOption> Workflows { get; }
    public WorkflowEditorViewModel Editor { get; }

    [ObservableProperty]
    private WorkflowOption? _selectedWorkflow;

    partial void OnSelectedWorkflowChanged(WorkflowOption? value)
    {
        if (value is null) return;
        _ = Editor.LoadAsync(value.Id);
    }

    public Task ReloadAsync()
    {
        var current = SelectedWorkflow ?? Workflows.FirstOrDefault();
        if (current is null) return Task.CompletedTask;
        return Editor.LoadAsync(current.Id);
    }
}

public sealed record WorkflowOption(string Id, string DisplayName, string Description);
