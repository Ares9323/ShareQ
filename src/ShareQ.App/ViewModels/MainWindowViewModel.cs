using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;
using MessageBoxResult = System.Windows.MessageBoxResult;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.App.Services;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

public sealed partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IItemStore _items;
    private readonly IServiceProvider _services;
    private readonly DispatcherTimer _searchDebounce;

    public MainWindowViewModel(IItemStore items, IServiceProvider services)
    {
        _items = items;
        _services = services;
        Items = [];
        _searchDebounce = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(220) };
        _searchDebounce.Tick += (_, _) => { _searchDebounce.Stop(); _ = ReloadAsync(); };

        items.ItemsChanged += OnItemsChanged;
        // First load on construction; UI thread is fine here (called from MainWindow.DataContext setup).
        _ = ReloadAsync();
    }

    public ObservableCollection<ItemRowViewModel> Items { get; }

    [ObservableProperty]
    private string _title = "ShareQ";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ItemKind? _kindFilter;

    [ObservableProperty]
    private ItemRowViewModel? _selectedItem;

    [ObservableProperty]
    private byte[]? _selectedItemPayload;

    [ObservableProperty]
    private string _selectedItemPreviewText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>BlobRef of the currently-selected item (file path on disk for capture-saved items).</summary>
    [ObservableProperty]
    private string? _selectedItemBlobRef;

    public bool IsTextFilter => KindFilter == ItemKind.Text;
    public bool IsImageFilter => KindFilter == ItemKind.Image;
    public bool IsAllFilter => KindFilter is null;

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    partial void OnKindFilterChanged(ItemKind? value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsTextFilter));
        OnPropertyChanged(nameof(IsImageFilter));
        _ = ReloadAsync();
    }

    partial void OnSelectedItemChanged(ItemRowViewModel? value)
    {
        SelectedItemPayload = null;
        SelectedItemPreviewText = string.Empty;
        SelectedItemBlobRef = null;
        if (value is null) return;
        _ = LoadSelectedItemAsync(value.Id);
    }

    [RelayCommand]
    private void SetFilterAll() => KindFilter = null;
    [RelayCommand]
    private void SetFilterText() => KindFilter = ItemKind.Text;
    [RelayCommand]
    private void SetFilterImage() => KindFilter = ItemKind.Image;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task TogglePin()
    {
        if (SelectedItem is null) return;
        await _items.SetPinnedAsync(SelectedItem.Id, !SelectedItem.Pinned, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelected()
    {
        if (SelectedItem is null) return;
        // Remember position so the next reload restores the cursor to the same place rather than
        // jumping to the top of the list.
        _selectionFallbackIndex = Items.IndexOf(SelectedItem);
        await _items.SoftDeleteAsync(SelectedItem.Id, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Index to restore on the next reload when the previously-selected item disappears
    /// (used by Delete to keep the keyboard cursor near the deleted row).</summary>
    private int _selectionFallbackIndex = -1;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PasteSelected()
    {
        if (SelectedItem is null) return;
        var paster = _services.GetRequiredService<AutoPaster>();
        await paster.PasteAsync(SelectedItem.Id, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(IsImageSelection))]
    private async Task OpenInEditor()
    {
        if (SelectedItem is null || SelectedItem.Kind != ItemKind.Image) return;
        var launcher = _services.GetRequiredService<EditorLauncher>();
        await launcher.OpenAsync(SelectedItem.Id, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand]
    private async Task ClearAllHistory()
    {
        var result = MessageBox.Show(
            "Delete every non-pinned item from history? Pinned items will be kept. This can be undone manually but is irreversible from the UI.",
            "Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;
        var n = await _items.ClearAllExceptPinnedAsync(CancellationToken.None).ConfigureAwait(false);
        StatusText = $"Cleared {n} item{(n == 1 ? "" : "s")}";
    }

    [RelayCommand(CanExecute = nameof(HasFileOnDisk))]
    private void OpenInExplorer()
    {
        var path = SelectedItemBlobRef;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        // explorer.exe /select,"<path>" opens the folder and highlights the file.
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    private bool HasSelection() => SelectedItem is not null;
    private bool IsImageSelection() => SelectedItem?.Kind == ItemKind.Image;
    private bool HasFileOnDisk() => !string.IsNullOrEmpty(SelectedItemBlobRef);

    private async Task ReloadAsync()
    {
        var search = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        var query = new ItemQuery(Limit: 500, Search: search, Kind: KindFilter, IncludePayload: false);
        var rows = await _items.ListAsync(query, CancellationToken.None).ConfigureAwait(true);

        var previousId = SelectedItem?.Id;
        Items.Clear();
        for (var i = 0; i < rows.Count; i++) Items.Add(new ItemRowViewModel(rows[i], displayIndex: i));

        StatusText = $"{Items.Count} item{(Items.Count == 1 ? "" : "s")}";
        // Keep the user's selection across reloads when possible.
        if (previousId is { } id) SelectedItem = Items.FirstOrDefault(i => i.Id == id);
        if (SelectedItem is null && _selectionFallbackIndex >= 0 && Items.Count > 0)
        {
            // Previous item was deleted — pick the same index (clamped) instead of jumping to top.
            var idx = Math.Min(_selectionFallbackIndex, Items.Count - 1);
            SelectedItem = Items[idx];
        }
        _selectionFallbackIndex = -1;
        SelectedItem ??= Items.FirstOrDefault();
        TogglePinCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        PasteSelectedCommand.NotifyCanExecuteChanged();
        OpenInEditorCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadSelectedItemAsync(long id)
    {
        var record = await _items.GetByIdAsync(id, CancellationToken.None).ConfigureAwait(true);
        if (record is null || record.Id != (SelectedItem?.Id ?? -1)) return;
        SelectedItemBlobRef = record.BlobRef;
        if (record.Kind == ItemKind.Image)
        {
            SelectedItemPayload = record.Payload.ToArray();
        }
        else if (record.Kind == ItemKind.Text)
        {
            try { SelectedItemPreviewText = System.Text.Encoding.UTF8.GetString(record.Payload.Span); }
            catch { SelectedItemPreviewText = "[binary]"; }
        }
        TogglePinCommand.NotifyCanExecuteChanged();
        OpenInEditorCommand.NotifyCanExecuteChanged();
        OpenInExplorerCommand.NotifyCanExecuteChanged();
    }

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e)
    {
        // Marshal to UI thread; ItemStore raises from whichever thread executed the SQL.
        Application.Current.Dispatcher.InvokeAsync(() => _ = ReloadAsync());
    }

    public void Dispose()
    {
        _items.ItemsChanged -= OnItemsChanged;
        _searchDebounce.Stop();
    }
}
