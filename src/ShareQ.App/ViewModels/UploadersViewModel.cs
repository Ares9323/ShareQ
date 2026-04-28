using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShareQ.App.Services.Plugins;
using ShareQ.PluginContracts;

namespace ShareQ.App.ViewModels;

/// <summary>
/// Backs the Settings → Uploaders tab. Three categorized lists (Image / File / Text / Video) of
/// uploaders the user can multi-select via checkbox; selection persists through the
/// <see cref="PluginRegistry"/>'s settings-store-backed selection methods.
/// </summary>
public sealed partial class UploadersViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;

    public UploadersViewModel(PluginRegistry registry)
    {
        _registry = registry;
        ImageUploaders = [];
        FileUploaders  = [];
        TextUploaders  = [];
        VideoUploaders = [];
        _ = LoadAsync();
    }

    public ObservableCollection<UploaderSelectionItemViewModel> ImageUploaders { get; }
    public ObservableCollection<UploaderSelectionItemViewModel> FileUploaders  { get; }
    public ObservableCollection<UploaderSelectionItemViewModel> TextUploaders  { get; }
    public ObservableCollection<UploaderSelectionItemViewModel> VideoUploaders { get; }

    private async Task LoadAsync()
    {
        await PopulateAsync(UploaderCapabilities.Image, ImageUploaders).ConfigureAwait(true);
        await PopulateAsync(UploaderCapabilities.File,  FileUploaders).ConfigureAwait(true);
        await PopulateAsync(UploaderCapabilities.Text,  TextUploaders).ConfigureAwait(true);
        await PopulateAsync(UploaderCapabilities.Video, VideoUploaders).ConfigureAwait(true);
    }

    private async Task PopulateAsync(UploaderCapabilities category, ObservableCollection<UploaderSelectionItemViewModel> target)
    {
        target.Clear();
        var selected = await _registry.GetSelectedIdsAsync(category, CancellationToken.None).ConfigureAwait(true);
        foreach (var uploader in _registry.AllUploaders)
        {
            if ((uploader.Capabilities & category) == 0) continue;
            var isSelected = selected.Contains(uploader.Id);
            target.Add(new UploaderSelectionItemViewModel(
                uploader.Id, uploader.DisplayName, isSelected,
                (item, value) => _ = OnItemToggledAsync(category, target, item, value)));
        }
    }

    private async Task OnItemToggledAsync(
        UploaderCapabilities category,
        ObservableCollection<UploaderSelectionItemViewModel> list,
        UploaderSelectionItemViewModel _,
        bool __)
    {
        // Persist the full ordered list of currently-selected ids for this category.
        var ids = list.Where(i => i.IsSelected).Select(i => i.Id).ToList();
        await _registry.SetSelectedIdsAsync(category, ids, CancellationToken.None).ConfigureAwait(true);
    }
}
