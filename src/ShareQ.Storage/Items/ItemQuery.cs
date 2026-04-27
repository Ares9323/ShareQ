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
    bool IncludeThumbnail = true);
