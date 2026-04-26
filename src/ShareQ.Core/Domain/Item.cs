namespace ShareQ.Core.Domain;

public sealed record Item(
    long Id,
    ItemKind Kind,
    ItemSource Source,
    DateTimeOffset CreatedAt,
    long PayloadSize,
    bool Pinned = false,
    DateTimeOffset? DeletedAt = null,
    string? SourceProcess = null,
    string? SourceWindow = null,
    string? BlobRef = null,
    string? UploadedUrl = null,
    string? UploaderId = null);
