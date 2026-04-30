using Microsoft.Data.Sqlite;

namespace ShareQ.Storage.Database.Migrations;

/// <summary>Adds user-defined categories (CopyQ-style "tabs") so clipboard items can be
/// organised into named buckets ("Clipboard", "Code", "URLs", …) instead of living in one
/// flat list. Each item gets a `category` column that defaults to the built-in "Clipboard"
/// bucket; existing rows migrate cleanly into that bucket. The companion `categories` table
/// stores per-category metadata: display order, icon glyph, optional retention caps.</summary>
public sealed class Migration003Categories : IMigration
{
    public int TargetVersion => 3;

    public async Task ApplyAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            ALTER TABLE items ADD COLUMN category TEXT NOT NULL DEFAULT 'Clipboard';

            CREATE INDEX idx_items_category
                ON items(category, created_at DESC)
                WHERE deleted_at IS NULL;

            CREATE TABLE categories (
                name              TEXT PRIMARY KEY,
                icon              TEXT,
                sort_order        INTEGER NOT NULL DEFAULT 0,
                max_items         INTEGER NOT NULL DEFAULT 0,
                auto_cleanup_days INTEGER NOT NULL DEFAULT 0
            );

            INSERT INTO categories (name, icon, sort_order) VALUES ('Clipboard', '📋', 0);

            INSERT INTO schema_version (version) VALUES (3);
            """;
        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
