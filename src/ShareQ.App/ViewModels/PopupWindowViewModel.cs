using System.Collections.ObjectModel;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

public sealed partial class PopupWindowViewModel : ObservableObject
{
    private readonly IItemStore _items;
    private long _previewLoadToken;

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
            ApplyPreview(PreviewKind.None, null, null, null, null, null);
            return;
        }

        var record = await _items.GetByIdAsync(row.Id, CancellationToken.None).ConfigureAwait(true);
        if (token != System.Threading.Interlocked.Read(ref _previewLoadToken)) return;
        if (record is null) { ApplyPreview(PreviewKind.None, null, null, null, null, null); return; }

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

public enum PreviewKind { None, Text, Html, Rtf, Image }
