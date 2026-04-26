using Microsoft.Data.Sqlite;
using ShareQ.Storage.Tests.Fixtures;
using Xunit;

namespace ShareQ.Storage.Tests.Database;

public class ShareQDatabaseTests
{
    [Fact]
    public async Task InitializeAsync_OnFreshDirectory_CreatesSchemaAtVersion1()
    {
        await using var fixture = await new TempDatabaseFixture().InitializeAsync();

        var connection = fixture.Database.GetOpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT MAX(version) FROM schema_version;";
        var version = (long)(await cmd.ExecuteScalarAsync())!;

        Assert.Equal(1, version);
    }

    [Fact]
    public async Task InitializeAsync_CreatesItemsTableWithExpectedColumns()
    {
        await using var fixture = await new TempDatabaseFixture().InitializeAsync();

        var columns = await ListColumnsAsync(fixture.Database.GetOpenConnection(), "items");

        Assert.Contains("id", columns);
        Assert.Contains("kind", columns);
        Assert.Contains("source", columns);
        Assert.Contains("created_at", columns);
        Assert.Contains("payload", columns);
        Assert.Contains("payload_size", columns);
        Assert.Contains("blob_ref", columns);
        Assert.Contains("uploaded_url", columns);
        Assert.Contains("search_text", columns);
    }

    [Fact]
    public async Task InitializeAsync_CreatesFtsVirtualTableAndTriggers()
    {
        await using var fixture = await new TempDatabaseFixture().InitializeAsync();
        var connection = fixture.Database.GetOpenConnection();

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE name = 'items_fts' AND type = 'table';";
        var ftsName = await cmd.ExecuteScalarAsync();

        Assert.Equal("items_fts", ftsName);

        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'trigger' AND name LIKE 'items_%';";
        var triggerCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(3, triggerCount);
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        await using var fixture = await new TempDatabaseFixture().InitializeAsync();

        await fixture.Database.InitializeAsync(CancellationToken.None);
        await fixture.Database.InitializeAsync(CancellationToken.None);

        var connection = fixture.Database.GetOpenConnection();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM schema_version;";
        var rowCount = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task GetOpenConnection_BeforeInitialize_Throws()
    {
        await using var fixture = new TempDatabaseFixture();

        Assert.Throws<InvalidOperationException>(() => fixture.Database.GetOpenConnection());
    }

    private static async Task<List<string>> ListColumnsAsync(SqliteConnection connection, string tableName)
    {
        var columns = new List<string>();
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({tableName});";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            columns.Add(reader.GetString(1));
        }
        return columns;
    }
}
