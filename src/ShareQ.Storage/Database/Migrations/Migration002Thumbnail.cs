using Microsoft.Data.Sqlite;

namespace ShareQ.Storage.Database.Migrations;

/// <summary>Adds an inline `thumbnail` BLOB column to items so the popup/timeline can render previews
/// without decrypting the full DPAPI-encrypted payload for every row.</summary>
public sealed class Migration002Thumbnail : IMigration
{
    public int TargetVersion => 2;

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE items ADD COLUMN thumbnail BLOB;
            INSERT INTO schema_version (version) VALUES (2);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
