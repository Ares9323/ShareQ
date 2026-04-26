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
    string? SearchText = null);
