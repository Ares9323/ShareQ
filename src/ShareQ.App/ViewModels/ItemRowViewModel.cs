using System.Text;
using ShareQ.Core.Domain;
using ShareQ.Storage.Items;

namespace ShareQ.App.ViewModels;

public sealed class ItemRowViewModel
{
    public ItemRowViewModel(ItemRecord record, int displayIndex)
    {
        Id = record.Id;
        Kind = record.Kind;
        CapturedAt = record.CreatedAt;
        Pinned = record.Pinned;
        SourceProcess = record.SourceProcess ?? string.Empty;
        Preview = BuildPreview(record);
        Thumbnail = record.Thumbnail?.ToArray();
        DisplayIndex = displayIndex;
    }

    public long Id { get; }
    public ItemKind Kind { get; }
    public DateTimeOffset CapturedAt { get; }
    public bool Pinned { get; }
    public string SourceProcess { get; }
    public string Preview { get; }
    /// <summary>Pre-generated PNG thumbnail bytes for image items, null otherwise.</summary>
    public byte[]? Thumbnail { get; }
    public bool HasThumbnail => Thumbnail is { Length: > 0 };

    /// <summary>0-based position in the popup. Used to render a Ctrl+N hint badge for rows 0..8.</summary>
    public int DisplayIndex { get; }
    public string IndexBadge => DisplayIndex < 9 ? (DisplayIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
    public bool HasIndexBadge => DisplayIndex < 9;

    public string KindLabel => Kind.ToString();
    public string Age => FormatAge(DateTimeOffset.UtcNow - CapturedAt);

    private static string BuildPreview(ItemRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.SearchText))
        {
            // Older rows (pre-fix) stored raw CF_HTML / RTF text into SearchText. Sanitize at display
            // time so the popup/list don't show "Version:0.9 StartHTML:..." gibberish.
            return Truncate(SanitizeSearchText(record.SearchText!, record.Kind), 200);
        }
        if (record.Kind is ItemKind.Image) return "[image]";
        if (record.Kind is ItemKind.Video) return "[video]";
        if (record.Kind is ItemKind.Files) return "[files]";
        if (record.Payload.Length == 0) return string.Empty;
        try { return Truncate(Encoding.UTF8.GetString(record.Payload.Span), 200); }
        catch { return "[binary]"; }
    }

    private static string SanitizeSearchText(string text, ItemKind kind)
    {
        if (kind == ItemKind.Html && text.Contains("StartHTML:", StringComparison.OrdinalIgnoreCase))
        {
            var firstTag = text.IndexOf('<');
            if (firstTag > 0) text = text[firstTag..];
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }
        else if (kind == ItemKind.Rtf && text.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\\'[0-9a-fA-F]{2}", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\\[a-zA-Z]+-?\d* ?", " ");
            text = text.Replace("{", " ").Replace("}", " ");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        }
        return text;
    }

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max] + "…";

    private static string FormatAge(TimeSpan delta)
    {
        if (delta.TotalSeconds < 60) return $"{(int)delta.TotalSeconds}s";
        if (delta.TotalMinutes < 60) return $"{(int)delta.TotalMinutes}m";
        if (delta.TotalHours < 24) return $"{(int)delta.TotalHours}h";
        return $"{(int)delta.TotalDays}d";
    }
}
