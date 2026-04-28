using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ShareQ.App.ViewModels;

public sealed partial class AfterCaptureItemViewModel : ObservableObject
{
    private readonly Action<AfterCaptureItemViewModel, bool> _onEnabledChanged;
    private readonly Action<AfterCaptureItemViewModel, int> _onMove;
    private bool _suppress;

    public AfterCaptureItemViewModel(
        string stepId, string displayName, string? description, bool initiallyEnabled,
        Action<AfterCaptureItemViewModel, bool> onEnabledChanged,
        Action<AfterCaptureItemViewModel, int> onMove)
    {
        StepId = stepId;
        DisplayName = displayName;
        Description = description;
        _onEnabledChanged = onEnabledChanged;
        _onMove = onMove;
        _suppress = true;
        IsEnabled = initiallyEnabled;
        _suppress = false;
    }

    public string StepId { get; }
    public string DisplayName { get; }
    public string? Description { get; }

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
}
