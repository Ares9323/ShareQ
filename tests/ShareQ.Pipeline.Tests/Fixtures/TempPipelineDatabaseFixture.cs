using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ShareQ.Storage.Database;
using ShareQ.Storage.Database.Migrations;
using ShareQ.Storage.Options;
using ShareQ.Storage.Paths;

namespace ShareQ.Pipeline.Tests.Fixtures;

public sealed class TempPipelineDatabaseFixture : IAsyncDisposable
{
    public string RootDirectory { get; }
    public ShareQDatabase Database { get; }
    public IStoragePathResolver Paths { get; }
    public StorageOptions Options { get; }

    public TempPipelineDatabaseFixture()
    {
        RootDirectory = Path.Combine(Path.GetTempPath(), "ShareQ.PipelineTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootDirectory);

        Options = new StorageOptions { RootDirectoryOverride = RootDirectory };
        Paths = new StoragePathResolver(Microsoft.Extensions.Options.Options.Create(Options));

        var migrations = new IMigration[] { new Migration001InitialSchema() };
        Database = new ShareQDatabase(Paths, new MigrationRunner(migrations), NullLogger<ShareQDatabase>.Instance);
    }

    public async Task<TempPipelineDatabaseFixture> InitializeAsync(CancellationToken ct = default)
    {
        await Database.InitializeAsync(ct).ConfigureAwait(false);
        return this;
    }

    public async ValueTask DisposeAsync()
    {
        await Database.DisposeAsync().ConfigureAwait(false);
        try { Directory.Delete(RootDirectory, recursive: true); }
        catch (IOException) { /* best effort */ }
    }
}
