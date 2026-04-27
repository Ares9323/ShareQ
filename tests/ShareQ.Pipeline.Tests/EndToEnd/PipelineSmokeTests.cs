using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline;
using ShareQ.Pipeline.DependencyInjection;
using ShareQ.Pipeline.Profiles;
using ShareQ.Storage.Database;
using ShareQ.Storage.DependencyInjection;
using ShareQ.Storage.Items;
using Xunit;

namespace ShareQ.Pipeline.Tests.EndToEnd;

public class PipelineSmokeTests
{
    [Fact]
    public async Task EndToEnd_OnClipboardProfile_StoresIncomingItem()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ShareQ.PipelineSmoke", Guid.NewGuid().ToString("N"));
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddShareQStorage(opts => opts.RootDirectoryOverride = tempRoot);
            services.AddShareQPipeline();
            await using var sp = services.BuildServiceProvider();

            var db = sp.GetRequiredService<IShareQDatabase>();
            await db.InitializeAsync(CancellationToken.None);

            var seeder = sp.GetRequiredService<PipelineProfileSeeder>();
            await seeder.SeedAsync(CancellationToken.None);

            var profileStore = sp.GetRequiredService<IPipelineProfileStore>();
            var profile = await profileStore.GetAsync(DefaultPipelineProfiles.OnClipboardId, CancellationToken.None);
            Assert.NotNull(profile);

            var executor = sp.GetRequiredService<PipelineExecutor>();
            var ctx = new PipelineContext(sp);
            var newItem = new NewItem(
                Kind: ItemKind.Text,
                Source: ItemSource.Clipboard,
                CreatedAt: DateTimeOffset.UtcNow,
                Payload: Encoding.UTF8.GetBytes("smoke text"),
                PayloadSize: 10,
                SearchText: "smoke text");
            ctx.Bag[PipelineBagKeys.NewItem] = newItem;

            await executor.RunAsync(profile!, ctx, CancellationToken.None);

            var id = Assert.IsType<long>(ctx.Bag[PipelineBagKeys.ItemId]);
            var items = sp.GetRequiredService<IItemStore>();
            var stored = await items.GetByIdAsync(id, CancellationToken.None);
            Assert.NotNull(stored);
            Assert.Equal("smoke text", Encoding.UTF8.GetString(stored!.Payload.Span));
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }
}
