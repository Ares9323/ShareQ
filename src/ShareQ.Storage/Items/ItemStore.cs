using Microsoft.Data.Sqlite;
using ShareQ.Core.Domain;
using ShareQ.Storage.Database;

namespace ShareQ.Storage.Items;

public sealed class ItemStore : IItemStore
{
    private readonly IShareQDatabase _database;
    private readonly ItemSerializer _serializer;

    public ItemStore(IShareQDatabase database, ItemSerializer serializer)
    {
        _database = database;
        _serializer = serializer;
    }

    public async Task<long> AddAsync(NewItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var conn = _database.GetOpenConnection();
        var encoded = _serializer.Encode(item.Payload);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO items
                (kind, source, created_at, pinned, source_process, source_window,
                 payload, payload_size, blob_ref, uploaded_url, uploader_id, search_text)
            VALUES
                ($kind, $source, $created_at, $pinned, $source_process, $source_window,
                 $payload, $payload_size, $blob_ref, $uploaded_url, $uploader_id, $search_text);
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$kind", item.Kind.ToString());
        cmd.Parameters.AddWithValue("$source", item.Source.ToString());
        cmd.Parameters.AddWithValue("$created_at", item.CreatedAt.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$pinned", item.Pinned ? 1 : 0);
        cmd.Parameters.AddWithValue("$source_process", (object?)item.SourceProcess ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$source_window", (object?)item.SourceWindow ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$payload", encoded);
        cmd.Parameters.AddWithValue("$payload_size", item.PayloadSize);
        cmd.Parameters.AddWithValue("$blob_ref", (object?)item.BlobRef ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$uploaded_url", (object?)item.UploadedUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$uploader_id", (object?)item.UploaderId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$search_text", (object?)item.SearchText ?? DBNull.Value);

        var newId = (long)(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!;
        return newId;
    }

    public async Task<ItemRecord?> GetByIdAsync(long id, CancellationToken cancellationToken)
    {
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT * FROM items WHERE id = $id LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) return null;
        return Map(reader);
    }

    public async Task<IReadOnlyList<ItemRecord>> ListAsync(ItemQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var conn = _database.GetOpenConnection();

        // When the caller doesn't need the payload (e.g. popup list), skip its column entirely so we
        // don't pay the per-row DPAPI decryption cost — for 200 image rows that's the difference
        // between instant and several seconds.
        var payloadColumn = query.IncludePayload ? "payload" : "NULL AS payload";
        var sql = new System.Text.StringBuilder($"SELECT items.id, items.kind, items.source, items.created_at, items.payload_size, items.pinned, items.deleted_at, items.source_process, items.source_window, items.blob_ref, items.uploaded_url, items.uploader_id, items.search_text, {payloadColumn} FROM items");
        var hasFts = !string.IsNullOrWhiteSpace(query.Search);
        if (hasFts)
        {
            sql.Append(" JOIN items_fts ON items_fts.rowid = items.id");
        }

        sql.Append(" WHERE 1=1");
        if (!query.IncludeDeleted) sql.Append(" AND items.deleted_at IS NULL");
        if (query.Kind is not null) sql.Append(" AND items.kind = $kind");
        if (query.Pinned is not null) sql.Append(" AND items.pinned = $pinned");
        if (hasFts) sql.Append(" AND items_fts.search_text MATCH $search");

        sql.Append(" ORDER BY items.created_at DESC LIMIT $limit OFFSET $offset;");

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql.ToString();
        if (query.Kind is not null) cmd.Parameters.AddWithValue("$kind", query.Kind.Value.ToString());
        if (query.Pinned is not null) cmd.Parameters.AddWithValue("$pinned", query.Pinned.Value ? 1 : 0);
        if (hasFts) cmd.Parameters.AddWithValue("$search", query.Search!);
        cmd.Parameters.AddWithValue("$limit", query.Limit);
        cmd.Parameters.AddWithValue("$offset", query.Offset);

        var results = new List<ItemRecord>(query.Limit);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            results.Add(Map(reader));
        }
        return results;
    }

    public Task<bool> SetPinnedAsync(long id, bool pinned, CancellationToken cancellationToken)
        => UpdateScalarAsync("UPDATE items SET pinned = $val WHERE id = $id;",
            id, pinned ? 1 : 0, cancellationToken);

    public async Task<bool> SetUploadedUrlAsync(long id, string uploaderId, string url, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uploaderId);
        ArgumentNullException.ThrowIfNull(url);
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET uploaded_url = $url, uploader_id = $uid WHERE id = $id;";
        cmd.Parameters.AddWithValue("$url", url);
        cmd.Parameters.AddWithValue("$uid", uploaderId);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows == 1;
    }

    public Task<bool> SoftDeleteAsync(long id, CancellationToken cancellationToken)
    {
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return UpdateScalarAsync("UPDATE items SET deleted_at = $val WHERE id = $id AND deleted_at IS NULL;",
            id, nowMs, cancellationToken);
    }

    public Task<bool> RestoreAsync(long id, CancellationToken cancellationToken)
        => UpdateScalarAsync("UPDATE items SET deleted_at = NULL WHERE id = $id;", id, DBNull.Value, cancellationToken);

    public async Task<int> HardDeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken)
    {
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM items
            WHERE deleted_at IS NOT NULL
              AND deleted_at < $cutoff
              AND pinned = 0;
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
        return await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdatePayloadAsync(long id, ReadOnlyMemory<byte> newPayload, long newPayloadSize, CancellationToken cancellationToken)
    {
        var conn = _database.GetOpenConnection();
        var encoded = _serializer.Encode(newPayload);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE items SET payload = $payload, payload_size = $size WHERE id = $id;";
        cmd.Parameters.AddWithValue("$payload", encoded);
        cmd.Parameters.AddWithValue("$size", newPayloadSize);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows == 1;
    }

    private async Task<bool> UpdateScalarAsync(string sql, long id, object value, CancellationToken cancellationToken)
    {
        var conn = _database.GetOpenConnection();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("$val", value);
        cmd.Parameters.AddWithValue("$id", id);
        var rows = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        return rows == 1;
    }

    private ItemRecord Map(SqliteDataReader reader)
    {
        var payloadOrd = reader.GetOrdinal("payload");
        ReadOnlyMemory<byte> plaintext = ReadOnlyMemory<byte>.Empty;
        if (!reader.IsDBNull(payloadOrd))
        {
            var encryptedPayload = (byte[])reader["payload"];
            plaintext = _serializer.Decode(encryptedPayload);
        }
        return new ItemRecord(
            Id: reader.GetInt64(reader.GetOrdinal("id")),
            Kind: Enum.Parse<ItemKind>(reader.GetString(reader.GetOrdinal("kind"))),
            Source: Enum.Parse<ItemSource>(reader.GetString(reader.GetOrdinal("source"))),
            CreatedAt: DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("created_at"))),
            PayloadSize: reader.GetInt64(reader.GetOrdinal("payload_size")),
            Pinned: reader.GetInt32(reader.GetOrdinal("pinned")) == 1,
            DeletedAt: reader.IsDBNull(reader.GetOrdinal("deleted_at")) ? null
                : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(reader.GetOrdinal("deleted_at"))),
            SourceProcess: NullableString(reader, "source_process"),
            SourceWindow: NullableString(reader, "source_window"),
            BlobRef: NullableString(reader, "blob_ref"),
            UploadedUrl: NullableString(reader, "uploaded_url"),
            UploaderId: NullableString(reader, "uploader_id"),
            Payload: plaintext,
            SearchText: NullableString(reader, "search_text"));
    }

    private static string? NullableString(SqliteDataReader reader, string column)
    {
        var ord = reader.GetOrdinal(column);
        return reader.IsDBNull(ord) ? null : reader.GetString(ord);
    }
}
