using Microsoft.Data.Sqlite;
using ShareQ.Storage.Blobs;
using ShareQ.Storage.Database;

namespace ShareQ.Storage.Rotation;

public sealed class RotationService : IRotationService
{
    private readonly IShareQDatabase _database;
    private readonly IBlobStore _blobs;

    public RotationService(IShareQDatabase database, IBlobStore blobs)
    {
        _database = database;
        _blobs = blobs;
    }

    public async Task<RotationResult> RunAsync(RotationPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var conn = _database.GetOpenConnection();
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ageCutoffMs = DateTimeOffset.UtcNow.Subtract(policy.MaxAge).ToUnixTimeMilliseconds();
        var hardCutoffMs = DateTimeOffset.UtcNow.Subtract(policy.SoftDeleteGracePeriod).ToUnixTimeMilliseconds();

        var softDeleted = 0;
        softDeleted += await SoftDeleteOverCountAsync(conn, policy.MaxItems, nowMs, cancellationToken).ConfigureAwait(false);
        softDeleted += await SoftDeleteOverAgeAsync(conn, ageCutoffMs, nowMs, cancellationToken).ConfigureAwait(false);

        var hardDeleted = await HardDeleteAsync(conn, hardCutoffMs, cancellationToken).ConfigureAwait(false);

        var orphans = await CleanupOrphanBlobsAsync(conn, cancellationToken).ConfigureAwait(false);

        return new RotationResult(softDeleted, hardDeleted, orphans);
    }

    private static async Task<int> SoftDeleteOverCountAsync(SqliteConnection conn, int maxItems, long nowMs, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE items
            SET deleted_at = $now
            WHERE id IN (
                SELECT id FROM items
                WHERE deleted_at IS NULL AND pinned = 0
                ORDER BY created_at DESC
                LIMIT -1 OFFSET $max
            );
            """;
        cmd.Parameters.AddWithValue("$now", nowMs);
        cmd.Parameters.AddWithValue("$max", maxItems);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> SoftDeleteOverAgeAsync(SqliteConnection conn, long cutoffMs, long nowMs, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE items
            SET deleted_at = $now
            WHERE deleted_at IS NULL AND pinned = 0 AND created_at < $cutoff;
            """;
        cmd.Parameters.AddWithValue("$now", nowMs);
        cmd.Parameters.AddWithValue("$cutoff", cutoffMs);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<int> HardDeleteAsync(SqliteConnection conn, long cutoffMs, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            DELETE FROM items
            WHERE deleted_at IS NOT NULL
              AND deleted_at < $cutoff
              AND pinned = 0;
            """;
        cmd.Parameters.AddWithValue("$cutoff", cutoffMs);
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task<int> CleanupOrphanBlobsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var alive = new HashSet<string>(StringComparer.Ordinal);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT blob_ref FROM items WHERE blob_ref IS NOT NULL;";
            await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                alive.Add(reader.GetString(0));
            }
        }

        var removed = 0;
        await foreach (var blobRef in _blobs.EnumerateAllAsync(ct).ConfigureAwait(false))
        {
            if (alive.Contains(blobRef)) continue;
            if (await _blobs.DeleteAsync(blobRef, ct).ConfigureAwait(false))
            {
                removed++;
            }
        }
        return removed;
    }
}
