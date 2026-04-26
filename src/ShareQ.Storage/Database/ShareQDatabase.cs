using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using ShareQ.Storage.Database.Migrations;
using ShareQ.Storage.Paths;

namespace ShareQ.Storage.Database;

public sealed class ShareQDatabase : IShareQDatabase
{
    private readonly IStoragePathResolver _paths;
    private readonly MigrationRunner _migrationRunner;
    private readonly ILogger<ShareQDatabase> _logger;
    private SqliteConnection? _connection;
    private bool _initialized;

    public ShareQDatabase(
        IStoragePathResolver paths,
        MigrationRunner migrationRunner,
        ILogger<ShareQDatabase> logger)
    {
        _paths = paths;
        _migrationRunner = migrationRunner;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;

        var dbPath = _paths.ResolveDatabasePath();
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        };

        _connection = new SqliteConnection(builder.ConnectionString);
        await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using (var pragma = _connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await _migrationRunner.ApplyAsync(_connection, cancellationToken).ConfigureAwait(false);
        _initialized = true;
        _logger.LogInformation("ShareQ database initialized at {Path}", dbPath);
    }

    public SqliteConnection GetOpenConnection()
    {
        if (!_initialized || _connection is null)
            throw new InvalidOperationException("Database not initialized. Call InitializeAsync first.");
        return _connection;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
        _initialized = false;
    }
}
