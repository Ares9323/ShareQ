using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using ShareQ.App.Services;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

public sealed partial class PopupWindowViewModel : ObservableObject, IDisposable
{
    private readonly IItemStore _items;
    private readonly IServiceProvider _services;
    private long _previewLoadToken;
    private string? _selectedItemBlobRef;

    public PopupWindowViewModel(IItemStore items, IServiceProvider services)
    {
        _items = items;
        _services = services;
        Rows = [];
        _items.ItemsChanged += OnItemsChanged;
    }

    public void Dispose() => _items.ItemsChanged -= OnItemsChanged;

    private void OnItemsChanged(object? sender, ItemsChangedEventArgs e)
    {
        // Marshal to UI thread; Refresh updates the ObservableCollection.
        Application.Current?.Dispatcher.InvokeAsync(() => _ = RefreshAsync(CancellationToken.None));
    }

    public ObservableCollection<ItemRowViewModel> Rows { get; }

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ItemRowViewModel? _selectedRow;

    [ObservableProperty]
    private ItemKind? _kindFilter;

    public bool IsAllFilter => KindFilter is null;
    public bool IsTextFilter => KindFilter == ItemKind.Text;
    public bool IsImageFilter => KindFilter == ItemKind.Image;

    partial void OnKindFilterChanged(ItemKind? value)
    {
        OnPropertyChanged(nameof(IsAllFilter));
        OnPropertyChanged(nameof(IsTextFilter));
        OnPropertyChanged(nameof(IsImageFilter));
        _ = RefreshAsync(CancellationToken.None);
    }

    // Preview state for the side panel. Code-behind on PopupWindow watches Rtf/Html bytes since
    // those formats can't be data-bound directly into RichTextBox / WebBrowser.
    [ObservableProperty] private PreviewKind _previewKind = PreviewKind.None;
    [ObservableProperty] private string? _previewText;
    [ObservableProperty] private byte[]? _previewImageBytes;
    [ObservableProperty] private byte[]? _previewRtfBytes;
    [ObservableProperty] private string? _previewHtml;
    [ObservableProperty] private string? _previewMeta;

    public bool IsTextPreview => PreviewKind == PreviewKind.Text;
    public bool IsHtmlPreview => PreviewKind == PreviewKind.Html;
    public bool IsRtfPreview => PreviewKind == PreviewKind.Rtf;
    public bool IsImagePreview => PreviewKind == PreviewKind.Image;
    public bool HasPreview => PreviewKind != PreviewKind.None;

    partial void OnPreviewKindChanged(PreviewKind value)
    {
        OnPropertyChanged(nameof(IsTextPreview));
        OnPropertyChanged(nameof(IsHtmlPreview));
        OnPropertyChanged(nameof(IsRtfPreview));
        OnPropertyChanged(nameof(IsImagePreview));
        OnPropertyChanged(nameof(HasPreview));
    }

    partial void OnSelectedRowChanged(ItemRowViewModel? value)
    {
        // Cancel any in-flight load and start fresh.
        var token = System.Threading.Interlocked.Increment(ref _previewLoadToken);
        _ = LoadPreviewAsync(value, token);
    }

    private async Task LoadPreviewAsync(ItemRowViewModel? row, long token)
    {
        if (row is null)
        {
            _selectedItemBlobRef = null;
            ApplyPreview(PreviewKind.None, null, null, null, null, null);
            NotifyCommandsCanExecuteChanged();
            return;
        }

        var record = await _items.GetByIdAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
        if (token != System.Threading.Interlocked.Read(ref _previewLoadToken)) return;
        if (record is null) { ApplyPreview(PreviewKind.None, null, null, null, null, null); return; }
        _selectedItemBlobRef = record.BlobRef;

        var payload = record.Payload;
        var meta = $"{record.Kind} · {row.SourceProcess} · {row.Age}";
        switch (record.Kind)
        {
            case ItemKind.Text:
                ApplyPreview(PreviewKind.Text, Encoding.UTF8.GetString(payload.Span), null, null, null, meta);
                break;
            case ItemKind.Html:
                ApplyPreview(PreviewKind.Html, null, null, null, Encoding.UTF8.GetString(payload.Span), meta);
                break;
            case ItemKind.Rtf:
                ApplyPreview(PreviewKind.Rtf, null, null, payload.ToArray(), null, meta);
                break;
            case ItemKind.Image:
                ApplyPreview(PreviewKind.Image, null, payload.ToArray(), null, null, meta);
                break;
            case ItemKind.Files:
                ApplyPreview(PreviewKind.Text, Encoding.UTF8.GetString(payload.Span), null, null, null, meta);
                break;
            default:
                ApplyPreview(PreviewKind.None, null, null, null, null, meta);
                break;
        }
        NotifyCommandsCanExecuteChanged();
    }

    private void NotifyCommandsCanExecuteChanged()
    {
        TogglePinSelectedCommand.NotifyCanExecuteChanged();
        DeleteSelectedCommand.NotifyCanExecuteChanged();
        PasteSelectedCommand.NotifyCanExecuteChanged();
        OpenInEditorCommand.NotifyCanExecuteChanged();
        OpenInExplorerCommand.NotifyCanExecuteChanged();
    }

    private void ApplyPreview(PreviewKind kind, string? text, byte[]? image, byte[]? rtf, string? html, string? meta)
    {
        PreviewKind = kind;
        PreviewText = text;
        PreviewImageBytes = image;
        PreviewRtfBytes = rtf;
        PreviewHtml = html;
        PreviewMeta = meta;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        // Skip payload decryption for the list view — only metadata + SearchText is needed for the
        // row preview. Payload (decrypted via DPAPI) is fetched on-demand via GetByIdAsync when the
        // user actually pastes / opens an item.
        var query = new ItemQuery(
            Limit: 500,
            Search: NormalizeSearch(SearchText),
            Kind: KindFilter,
            IncludePayload: false);
        var previousId = SelectedRow?.Id;
        var loaded = await _items.ListAsync(query, cancellationToken).ConfigureAwait(false);
        Rows.Clear();
        for (var i = 0; i < loaded.Count; i++)
        {
            Rows.Add(new ItemRowViewModel(loaded[i], displayIndex: i));
        }
        // Preserve selection across reloads when the same id is still present.
        if (previousId is { } id) SelectedRow = Rows.FirstOrDefault(r => r.Id == id);
        SelectedRow ??= Rows.FirstOrDefault();
        NotifyCommandsCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task TogglePinSelectedAsync()
    {
        if (SelectedRow is not { } row) return;
        var keepId = row.Id;
        await _items.SetPinnedAsync(row.Id, !row.Pinned, CancellationToken.None).ConfigureAwait(true);
        // ItemsChanged event will refresh; re-select the same id.
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        SelectedRow = Rows.FirstOrDefault(r => r.Id == keepId) ?? Rows.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedRow is not { } row) return;
        var idx = Rows.IndexOf(row);
        await _items.SoftDeleteAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
        await RefreshAsync(CancellationToken.None).ConfigureAwait(true);
        // Restore selection near the deleted row's position rather than jumping to top.
        if (Rows.Count > 0)
        {
            var newIdx = Math.Min(idx, Rows.Count - 1);
            if (newIdx >= 0) SelectedRow = Rows[newIdx];
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task PasteSelectedAsync()
    {
        if (SelectedRow is not { } row) return;
        var paster = _services.GetRequiredService<AutoPaster>();
        await paster.PasteAsync(row.Id, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(IsImageSelection))]
    private async Task OpenInEditorAsync()
    {
        if (SelectedRow is null || SelectedRow.Kind != ItemKind.Image) return;
        var launcher = _services.GetRequiredService<EditorLauncher>();
        await launcher.OpenAsync(SelectedRow.Id, CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand(CanExecute = nameof(HasFileOnDisk))]
    private void OpenInExplorer()
    {
        var path = _selectedItemBlobRef;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{path}\"",
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private async Task ClearAllAsync()
    {
        var result = MessageBox.Show(
            "Delete every non-pinned item from history? Pinned items will be kept.",
            "Clear history",
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        if (result != MessageBoxResult.OK) return;
        await _items.ClearAllExceptPinnedAsync(CancellationToken.None).ConfigureAwait(false);
    }

    [RelayCommand]
    private void SetFilterAll() => KindFilter = null;
    [RelayCommand]
    private void SetFilterText() => KindFilter = ItemKind.Text;
    [RelayCommand]
    private void SetFilterImage() => KindFilter = ItemKind.Image;

    private bool HasSelection() => SelectedRow is not null;
    private bool IsImageSelection() => SelectedRow?.Kind == ItemKind.Image;
    private bool HasFileOnDisk() => !string.IsNullOrEmpty(_selectedItemBlobRef);

    private static string? NormalizeSearch(string text)
    {
        text = text.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

public enum PreviewKind { None, Text, Html, Rtf, Image }
