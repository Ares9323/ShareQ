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
        var query = new ItemQuery(Limit: 200, Search: NormalizeSearch(SearchText));
        var loaded = await _items.ListAsync(query, cancellationToken).ConfigureAwait(false);
        Rows.Clear();
        foreach (var record in loaded)
        {
            Rows.Add(new ItemRowViewModel(record));
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

    private static string? NormalizeSearch(string text)
    {
        text = text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}
