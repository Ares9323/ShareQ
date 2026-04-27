using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Tasks;
using Xunit;

namespace ShareQ.Pipeline.Tests.Tasks;

public class SaveToFileTaskTests
{
    private static SaveToFileTask Create() => new(NullLogger<SaveToFileTask>.Instance);

    private static PipelineContext NewContext() => new(new ServiceCollection().BuildServiceProvider());

    [Fact]
    public async Task ExecuteAsync_WritesPayloadToFolderAndPopulatesBag()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ShareQ.SaveTask", Guid.NewGuid().ToString("N"));
        try
        {
            var task = Create();
            var ctx = NewContext();
            var bytes = new byte[] { 1, 2, 3, 4, 5 };
            ctx.Bag[PipelineBagKeys.PayloadBytes] = bytes;
            ctx.Bag[PipelineBagKeys.FileExtension] = "bin";
            var config = JsonNode.Parse($"{{\"folder\":\"{folder.Replace("\\", "\\\\")}\"}}");

            await task.ExecuteAsync(ctx, config, CancellationToken.None);

            var path = (string)ctx.Bag[PipelineBagKeys.LocalPath];
            Assert.True(File.Exists(path));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            Assert.StartsWith(folder, path);
            Assert.EndsWith(".bin", path);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }

    [Fact]
    public async Task ExecuteAsync_NoPayloadInBag_Skips()
    {
        var task = Create();
        var ctx = NewContext();

        await task.ExecuteAsync(ctx, config: null, CancellationToken.None);

        Assert.False(ctx.Bag.ContainsKey(PipelineBagKeys.LocalPath));
    }

    [Fact]
    public async Task ExecuteAsync_DefaultExtension_IsBin_WhenNotInBag()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ShareQ.SaveTask.NoExt", Guid.NewGuid().ToString("N"));
        try
        {
            var task = Create();
            var ctx = NewContext();
            ctx.Bag[PipelineBagKeys.PayloadBytes] = new byte[] { 0xAA };
            var config = JsonNode.Parse($"{{\"folder\":\"{folder.Replace("\\", "\\\\")}\"}}");

            await task.ExecuteAsync(ctx, config, CancellationToken.None);

            var path = (string)ctx.Bag[PipelineBagKeys.LocalPath];
            Assert.EndsWith(".bin", path);
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { /* best effort */ }
        }
    }
}
