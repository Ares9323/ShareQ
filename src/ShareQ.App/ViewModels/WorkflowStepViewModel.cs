using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShareQ.App.ViewModels;

public sealed partial class WorkflowStepViewModel : ObservableObject
{
    private readonly Action<WorkflowStepViewModel, bool> _onEnabledChanged;
    private readonly Action<WorkflowStepViewModel, int> _onMove;
    private readonly Action<WorkflowStepViewModel> _onRemove;
    private bool _suppress;

    public WorkflowStepViewModel(
        int storageIndex,
        string taskId,
        string displayName,
        string? description,
        string? category,
        bool initiallyEnabled,
        Action<WorkflowStepViewModel, bool> onEnabledChanged,
        Action<WorkflowStepViewModel, int> onMove,
        Action<WorkflowStepViewModel> onRemove)
    {
        StorageIndex = storageIndex;
        TaskId = taskId;
        DisplayName = displayName;
        Description = description;
        Category = category;
        _onEnabledChanged = onEnabledChanged;
        _onMove = onMove;
        _onRemove = onRemove;
        _suppress = true;
        IsEnabled = initiallyEnabled;
        _suppress = false;
    }

    /// <summary>Index of this step in the underlying profile.Steps list (mutated as steps are
    /// added / removed / reordered, kept in sync by <see cref="WorkflowEditorViewModel"/>).</summary>
    public int StorageIndex { get; set; }
    public string TaskId { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public string? Category { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress) return;
        _onEnabledChanged(this, value);
    }

    [RelayCommand]
    private void MoveUp() => _onMove(this, -1);

    [RelayCommand]
    private void MoveDown() => _onMove(this, 1);

    [RelayCommand]
    private void Remove() => _onRemove(this);
}
