using CommunityToolkit.Mvvm.ComponentModel;

namespace ShareQ.App.ViewModels;

/// <summary>One uploader row inside a category section (Image / File / Text / Video) in
/// Settings → Uploaders. Toggling <see cref="IsSelected"/> updates the persisted selection list
/// for that category through the parent <see cref="UploadersViewModel"/>.</summary>
public sealed partial class UploaderSelectionItemViewModel : ObservableObject
{
    private readonly Action<UploaderSelectionItemViewModel, bool> _onSelectionChanged;

    public UploaderSelectionItemViewModel(
        string id, string displayName, bool initiallySelected,
        Action<UploaderSelectionItemViewModel, bool> onSelectionChanged)
    {
        Id = id;
        DisplayName = displayName;
        _onSelectionChanged = onSelectionChanged;
        _isSelected = initiallySelected;
    }

    public string Id { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value) => _onSelectionChanged(this, value);
}
