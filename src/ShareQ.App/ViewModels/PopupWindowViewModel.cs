using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

public sealed partial class PopupWindowViewModel : ObservableObject
{
    private readonly IItemStore _items;

    public PopupWindowViewModel(IItemStore items)
    {
        _items = items;
        Rows = [];
    }

    public ObservableCollection<ItemRowViewModel> Rows { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ItemRowViewModel? _selectedRow;

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Skip payload decryption for the list view — only metadata + SearchText is needed for the
        // row preview. Payload (decrypted via DPAPI) is fetched on-demand via GetByIdAsync when the
        // user actually pastes / opens an item.
        var query = new ItemQuery(Limit: 200, Search: NormalizeSearch(SearchText), IncludePayload: false);
        var loaded = await _items.ListAsync(query, cancellationToken).ConfigureAwait(false);
        Rows.Clear();
        for (var i = 0; i < loaded.Count; i++)
        {
            Rows.Add(new ItemRowViewModel(loaded[i], displayIndex: i));
        }
        SelectedRow = Rows.FirstOrDefault();
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = RefreshAsync(CancellationToken.None);
    }

    [RelayCommand]
    private void MoveSelection(int delta)
    {
        if (Rows.Count == 0) return;
        var index = SelectedRow is null ? 0 : Rows.IndexOf(SelectedRow);
        index = Math.Clamp(index + delta, 0, Rows.Count - 1);
        SelectedRow = Rows[index];
    }

    [RelayCommand]
    private async Task TogglePinSelectedAsync()
    {
        if (SelectedRow is not { } row) return;
        var keepId = row.Id;
        await _items.SetPinnedAsync(row.Id, !row.Pinned, CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        // Pinning floats the row to the top of the pinned group; keep it selected so the user can
        // quickly unpin or paste.
        SelectedRow = Rows.FirstOrDefault(r => r.Id == keepId) ?? Rows.FirstOrDefault();
    }

    private static string? NormalizeSearch(string text)
    {
        text = text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
