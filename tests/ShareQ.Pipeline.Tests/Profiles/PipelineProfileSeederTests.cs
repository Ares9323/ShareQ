using Microsoft.Extensions.Logging.Abstractions;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Profiles;
using ShareQ.Pipeline.Tests.Fixtures;
using Xunit;

namespace ShareQ.Pipeline.Tests.Profiles;

public class PipelineProfileSeederTests
{
    private static (PipelineProfileSeeder Seeder, IPipelineProfileStore Store) Build(TempPipelineDatabaseFixture fx)
    {
        var store = new SqlitePipelineProfileStore(fx.Database);
        var seeder = new PipelineProfileSeeder(store, NullLogger<PipelineProfileSeeder>.Instance);
        return (seeder, store);
    }

    [Fact]
    public async Task SeedAsync_OnEmptyStore_AddsAllDefaultProfiles()
    {
        await using var fx = await new TempPipelineDatabaseFixture().InitializeAsync();
        var (seeder, store) = Build(fx);

        await seeder.SeedAsync(CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(DefaultPipelineProfiles.All.Count, list.Count);
        foreach (var defaultProfile in DefaultPipelineProfiles.All)
        {
            Assert.Contains(list, loaded => loaded.Id == defaultProfile.Id);
        }
    }

    [Fact]
    public async Task SeedAsync_IsIdempotent()
    {
        await using var fx = await new TempPipelineDatabaseFixture().InitializeAsync();
        var (seeder, store) = Build(fx);

        await seeder.SeedAsync(CancellationToken.None);
        await seeder.SeedAsync(CancellationToken.None);

        var list = await store.ListAsync(CancellationToken.None);
        Assert.Equal(DefaultPipelineProfiles.All.Count, list.Count);
    }

    [Fact]
    public async Task SeedAsync_PreservesUserCustomizations()
    {
        // Now that the user can reorder pipeline steps from Settings, the profile in DB is the
        // source of truth. Seeder must not clobber existing entries on each run.
        await using var fx = await new TempPipelineDatabaseFixture().InitializeAsync();
        var (seeder, store) = Build(fx);
        var customised = new PipelineProfile(
            DefaultPipelineProfiles.OnClipboardId,
            "User-renamed",
            "event:clipboard",
            new[] { new PipelineStep("user.custom.task") });
        await store.UpsertAsync(customised, CancellationToken.None);

        await seeder.SeedAsync(CancellationToken.None);

        var loaded = await store.GetAsync(DefaultPipelineProfiles.OnClipboardId, CancellationToken.None);
        Assert.Equal("User-renamed", loaded!.DisplayName);
        Assert.Single(loaded.Steps);
        Assert.Equal("user.custom.task", loaded.Steps[0].TaskId);
    }

    [Fact]
    public async Task ResetToDefaultsAsync_ReplacesExistingProfile()
    {
        await using var fx = await new TempPipelineDatabaseFixture().InitializeAsync();
        var (seeder, store) = Build(fx);
        var customised = new PipelineProfile(
            DefaultPipelineProfiles.OnClipboardId, "Old", "event:clipboard",
            new[] { new PipelineStep("legacy") });
        await store.UpsertAsync(customised, CancellationToken.None);

        await seeder.ResetToDefaultsAsync(DefaultPipelineProfiles.OnClipboardId, CancellationToken.None);

        var loaded = await store.GetAsync(DefaultPipelineProfiles.OnClipboardId, CancellationToken.None);
        var expected = DefaultPipelineProfiles.All.Single(p => p.Id == DefaultPipelineProfiles.OnClipboardId);
        Assert.Equal(expected.DisplayName, loaded!.DisplayName);
        Assert.Equal(expected.Steps.Count, loaded.Steps.Count);
    }
}
