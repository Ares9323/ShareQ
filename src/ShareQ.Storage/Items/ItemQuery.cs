using ShareQ.Core.Domain;

namespace ShareQ.Storage.Items;

public sealed record ItemQuery(
    int Limit = 100,
    int Offset = 0,
    ItemKind? Kind = null,
    bool? Pinned = null,
    bool IncludeDeleted = false,
    string? Search = null,
    bool IncludePayload = true,
    bool IncludeThumbnail = true,
    /// <summary>When set, restrict results to items in this category. null = all categories
    /// (the popup's "All" tab). Empty string is treated like null.</summary>
    string? Category = null);
