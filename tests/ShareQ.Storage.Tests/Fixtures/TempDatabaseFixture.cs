using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShareQ.Storage.Database;
using ShareQ.Storage.Database.Migrations;
using ShareQ.Storage.Options;
using ShareQ.Storage.Paths;

namespace ShareQ.Storage.Tests.Fixtures;

/// <summary>
/// Per-test fixture that creates a unique temp directory + database and disposes both at end-of-life.
/// Each fixture instance is independent → tests using it are parallel-safe by default.
/// </summary>
public sealed class TempDatabaseFixture : IAsyncDisposable
{
    public string RootDirectory { get; }
    public ShareQDatabase Database { get; }
    public IStoragePathResolver Paths { get; }
    public StorageOptions Options { get; }

    public TempDatabaseFixture()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "ShareQ.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);

        Options = new StorageOptions { RootDirectoryOverride = RootDirectory };
        Paths = new StoragePathResolver(Microsoft.Extensions.Options.Options.Create(Options));

        var migrations = new IMigration[] { new Migration001InitialSchema() };
        Database = new ShareQDatabase(Paths, new MigrationRunner(migrations), NullLogger<ShareQDatabase>.Instance);
    }

    public async Task<TempDatabaseFixture> InitializeAsync(CancellationToken ct = default)
    {
        await Database.InitializeAsync(ct).ConfigureAwait(false);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync().ConfigureAwait(false);
        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Some OS file locks may linger briefly — ignore. Temp cleanup runs eventually.
        }
    }
}
