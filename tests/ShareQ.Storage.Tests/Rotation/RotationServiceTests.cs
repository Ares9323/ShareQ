using System.Text;
using ShareQ.Core.Domain;
using ShareQ.Storage.Blobs;
using ShareQ.Storage.Items;
using ShareQ.Storage.Protection;
using ShareQ.Storage.Rotation;
using ShareQ.Storage.Tests.Fixtures;
using Xunit;

namespace ShareQ.Storage.Tests.Rotation;

public class RotationServiceTests
{
    private static (IItemStore Items, IBlobStore Blobs, IRotationService Rotation) Build(TempDatabaseFixture fx)
    {
        var protector = new DpapiPayloadProtector();
        var items = new ItemStore(fx.Database, new ItemSerializer(protector));
        var blobs = new FileSystemBlobStore(fx.Paths);
        var rotation = new RotationService(fx.Database, blobs);
        return (items, blobs, rotation);
    }

    private static NewItem TextItem(string text, DateTimeOffset created, bool pinned = false)
        => new(
            Kind: ItemKind.Text,
            Source: ItemSource.Clipboard,
            CreatedAt: created,
            Payload: Encoding.UTF8.GetBytes(text),
            PayloadSize: Encoding.UTF8.GetByteCount(text),
            Pinned: pinned,
            SearchText: text);

    [Fact]
    public async Task RunAsync_OverCountCap_SoftDeletesOldestNonPinned()
    {
        await using var fx = await new TempDatabaseFixture().InitializeAsync();
        var (items, _, rotation) = Build(fx);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 5; i++)
        {
            await items.AddAsync(TextItem($"item-{i}", now.AddMinutes(-i)), CancellationToken.None);
        }

        var result = await rotation.RunAsync(
            new RotationPolicy(MaxItems: 2, MaxAge: TimeSpan.FromDays(365), SoftDeleteGracePeriod: TimeSpan.FromHours(24)),
            CancellationToken.None);

        Assert.Equal(3, result.SoftDeleted);
        var alive = await items.ListAsync(new ItemQuery(), CancellationToken.None);
        Assert.Equal(2, alive.Count);
    }

    [Fact]
    public async Task RunAsync_NeverSoftDeletesPinned_EvenOverCap()
    {
        await using var fx = await new TempDatabaseFixture().InitializeAsync();
        var (items, _, rotation) = Build(fx);

        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            await items.AddAsync(TextItem($"a-{i}", now.AddMinutes(-i), pinned: true), CancellationToken.None);
        }

        var result = await rotation.RunAsync(
            new RotationPolicy(MaxItems: 1, MaxAge: TimeSpan.FromDays(365), SoftDeleteGracePeriod: TimeSpan.FromHours(24)),
            CancellationToken.None);

        Assert.Equal(0, result.SoftDeleted);
    }

    [Fact]
    public async Task RunAsync_OverAgeCap_SoftDeletesNonPinned()
    {
        await using var fx = await new TempDatabaseFixture().InitializeAsync();
        var (items, _, rotation) = Build(fx);

        var ancient = DateTimeOffset.UtcNow.AddDays(-100);
        await items.AddAsync(TextItem("ancient", ancient), CancellationToken.None);
        await items.AddAsync(TextItem("recent", DateTimeOffset.UtcNow), CancellationToken.None);

        var result = await rotation.RunAsync(
            new RotationPolicy(MaxItems: 100, MaxAge: TimeSpan.FromDays(30), SoftDeleteGracePeriod: TimeSpan.FromHours(24)),
            CancellationToken.None);

        Assert.Equal(1, result.SoftDeleted);
    }

    [Fact]
    public async Task RunAsync_HardDeletesPastGracePeriod()
    {
        await using var fx = await new TempDatabaseFixture().InitializeAsync();
        var (items, _, rotation) = Build(fx);

        var id = await items.AddAsync(TextItem("victim", DateTimeOffset.UtcNow.AddDays(-100)), CancellationToken.None);
        await items.SoftDeleteAsync(id, CancellationToken.None);
        var conn = fx.Database.GetOpenConnection();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "UPDATE items SET deleted_at = $past WHERE id = $id;";
            cmd.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddHours(-48).ToUnixTimeMilliseconds());
            cmd.Parameters.AddWithValue("$id", id);
            await cmd.ExecuteNonQueryAsync();
        }

        var result = await rotation.RunAsync(
            new RotationPolicy(MaxItems: 100, MaxAge: TimeSpan.FromDays(365), SoftDeleteGracePeriod: TimeSpan.FromHours(24)),
            CancellationToken.None);

        Assert.Equal(1, result.HardDeleted);
        Assert.Null(await items.GetByIdAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_RemovesOrphanBlobs()
    {
        await using var fx = await new TempDatabaseFixture().InitializeAsync();
        var (_, blobs, rotation) = Build(fx);

        await blobs.AddAsync(new byte[] { 1, 2 }, "bin", DateTimeOffset.UtcNow, CancellationToken.None);
        await blobs.AddAsync(new byte[] { 3, 4 }, "bin", DateTimeOffset.UtcNow, CancellationToken.None);

        var result = await rotation.RunAsync(
            new RotationPolicy(MaxItems: 100, MaxAge: TimeSpan.FromDays(365), SoftDeleteGracePeriod: TimeSpan.FromHours(24)),
            CancellationToken.None);

        Assert.Equal(2, result.OrphanBlobsRemoved);
    }
}
