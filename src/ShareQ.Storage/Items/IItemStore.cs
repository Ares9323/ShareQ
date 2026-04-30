using ShareQ.Core.Domain;

namespace ShareQ.Storage.Items;

public interface IItemStore
{
    Task<long> AddAsync(NewItem item, CancellationToken cancellationToken);
    Task<ItemRecord?> GetByIdAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<ItemRecord>> ListAsync(ItemQuery query, CancellationToken cancellationToken);
    Task<bool> SetPinnedAsync(long id, bool pinned, CancellationToken cancellationToken);
    Task<bool> SetUploadedUrlAsync(long id, string uploaderId, string url, CancellationToken cancellationToken);
    Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken);
    Task<bool> RestoreAsync(long id, CancellationToken cancellationToken);
    Task<int> HardDeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken);
    Task<bool> UpdatePayloadAsync(long id, ReadOnlyMemory<byte> newPayload, long newPayloadSize, CancellationToken cancellationToken);

    /// <summary>Soft-delete every non-pinned item. When <paramref name="category"/> is non-null
    /// the wipe is scoped to that single category bucket; otherwise it spans all categories.
    /// Pinned items are always preserved. Returns the count affected.</summary>
    Task<int> ClearAllExceptPinnedAsync(string? category, CancellationToken cancellationToken);

    /// <summary>Move an item into a different category bucket. Used by the popup's right-click
    /// "Move to → …" menu and by future auto-routing rules. Raises Updated when it changes.</summary>
    Task<bool> SetCategoryAsync(long id, string category, CancellationToken cancellationToken);

    /// <summary>Raised after any mutation (add / update / pin / soft-delete / restore). Subscribers
    /// must marshal to the UI thread themselves.</summary>
    event EventHandler<ItemsChangedEventArgs>? ItemsChanged;
}

public sealed record ItemsChangedEventArgs(ItemsChangeKind Kind, long ItemId);

public enum ItemsChangeKind
{
    Added,
    Updated,
    PinnedChanged,
    Deleted,
    Restored
}

public sealed record NewItem(
    ItemKind Kind,
    ItemSource Source,
    DateTimeOffset CreatedAt,
    ReadOnlyMemory<byte> Payload,
    long PayloadSize,
    bool Pinned = false,
    string? SourceProcess = null,
    string? SourceWindow = null,
    string? BlobRef = null,
    string? UploadedUrl = null,
    string? UploaderId = null,
    string? SearchText = null,
    string Category = "Clipboard");
