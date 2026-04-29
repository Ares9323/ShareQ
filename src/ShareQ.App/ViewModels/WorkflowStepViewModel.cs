using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShareQ.App.ViewModels;

public sealed partial class WorkflowStepViewModel : ObservableObject
{
    private readonly Action<WorkflowStepViewModel, bool> _onEnabledChanged;
    private readonly Action<WorkflowStepViewModel, int> _onMove;
    private readonly Action<WorkflowStepViewModel> _onRemove;
    private readonly Action<WorkflowStepViewModel, int>? _onParameterChanged;
    private bool _suppress;

    public WorkflowStepViewModel(
        int storageIndex,
        string taskId,
        string displayName,
        string? description,
        string? category,
        bool initiallyEnabled,
        IntParameter? parameter,
        int parameterValue,
        Action<WorkflowStepViewModel, bool> onEnabledChanged,
        Action<WorkflowStepViewModel, int> onMove,
        Action<WorkflowStepViewModel> onRemove,
        Action<WorkflowStepViewModel, int>? onParameterChanged)
    {
        StorageIndex = storageIndex;
        TaskId = taskId;
        DisplayName = displayName;
        Description = description;
        Category = category;
        Parameter = parameter;
        _onEnabledChanged = onEnabledChanged;
        _onMove = onMove;
        _onRemove = onRemove;
        _onParameterChanged = onParameterChanged;
        _suppress = true;
        IsEnabled = initiallyEnabled;
        ParameterValue = parameter is null ? 0 : Math.Clamp(parameterValue, parameter.Min, parameter.Max);
        _suppress = false;
    }

    /// <summary>Index of this step in the underlying profile.Steps list (mutated as steps are
    /// added / removed / reordered, kept in sync by <see cref="WorkflowEditorViewModel"/>).</summary>
    public int StorageIndex { get; set; }
    public string TaskId { get; }
    public string DisplayName { get; }
    public string? Description { get; }
    public string? Category { get; }

    /// <summary>The integer parameter shape for this step (null = no inline input).</summary>
    public IntParameter? Parameter { get; }
    public bool HasParameter => Parameter is not null;
    public string? ParameterLabel => Parameter?.Label;
    public int ParameterMin => Parameter?.Min ?? 0;
    public int ParameterMax => Parameter?.Max ?? 0;

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _canMoveUp;

    [ObservableProperty]
    private bool _canMoveDown;

    [ObservableProperty]
    private int _parameterValue;

    /// <summary>True while this row is being dragged. UI dims the row so the user can see what
    /// they picked up; cleared by the editor when the drag operation completes.</summary>
    [ObservableProperty]
    private bool _isDragSource;

    /// <summary>True when the drag's drop position is just above this row — UI shows a single
    /// insertion line in the gap above this row. "Drop after row N" is rendered as
    /// "above row N+1" so we never need a second indicator per row; the visual is always one
    /// line in one gap.</summary>
    [ObservableProperty]
    private bool _isDropTargetAbove;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_suppress) return;
        _onEnabledChanged(this, value);
    }

    partial void OnParameterValueChanged(int value)
    {
        if (_suppress) return;
        if (Parameter is null) return;
        var clamped = Math.Clamp(value, Parameter.Min, Parameter.Max);
        if (clamped != value)
        {
            _suppress = true;
            ParameterValue = clamped;
            _suppress = false;
        }
        _onParameterChanged?.Invoke(this, clamped);
    }

    [RelayCommand]
    private void MoveUp() => _onMove(this, -1);

    [RelayCommand]
    private void MoveDown() => _onMove(this, 1);

    [RelayCommand]
    private void Remove() => _onRemove(this);

    [RelayCommand]
    private void DecrementParameter()
    {
        if (Parameter is null) return;
        var next = ParameterValue - 1;
        if (next < Parameter.Min) return;
        ParameterValue = next; // OnParameterValueChanged persists.
    }

    [RelayCommand]
    private void IncrementParameter()
    {
        if (Parameter is null) return;
        var next = ParameterValue + 1;
        if (next > Parameter.Max) return;
        ParameterValue = next;
    }
}
