using ShareQ.Core.Pipeline;
using Xunit;

namespace ShareQ.Pipeline.Tests;

public class PipelineExecutorTests
{
    [Fact]
    public async Task RunAsync_WithNoTasks_CompletesSuccessfully()
    {
        var executor = new PipelineExecutor();
        var ctx = new PipelineContext(EmptyServices.Instance);

        await executor.RunAsync(tasks: [], ctx, CancellationToken.None);

        Assert.False(ctx.Aborted);
    }

    [Fact]
    public async Task RunAsync_InvokesEachTaskInOrder()
    {
        var calls = new List<string>();
        IPipelineTask a = new RecordingTask("a", calls);
        IPipelineTask b = new RecordingTask("b", calls);
        IPipelineTask c = new RecordingTask("c", calls);

        var executor = new PipelineExecutor();
        var ctx = new PipelineContext(EmptyServices.Instance);

        await executor.RunAsync(tasks: [a, b, c], ctx, CancellationToken.None);

        Assert.Equal(new[] { "a", "b", "c" }, calls);
    }

    [Fact]
    public async Task RunAsync_StopsAfterAbort()
    {
        var calls = new List<string>();
        IPipelineTask first = new RecordingTask("first", calls);
        IPipelineTask aborter = new AbortingTask("aborter");
        IPipelineTask never = new RecordingTask("never", calls);

        var executor = new PipelineExecutor();
        var ctx = new PipelineContext(EmptyServices.Instance);

        await executor.RunAsync(tasks: [first, aborter, never], ctx, CancellationToken.None);

        Assert.Equal(new[] { "first" }, calls);
        Assert.True(ctx.Aborted);
        Assert.Equal("intentional abort", ctx.AbortReason);
    }

    private sealed class RecordingTask(string id, List<string> calls) : IPipelineTask
    {
        public string Id => id;
        public string DisplayName => id;
        public PipelineTaskKind Kind => PipelineTaskKind.Both;
        public Task ExecuteAsync(PipelineContext context, System.Text.Json.Nodes.JsonNode? config, CancellationToken cancellationToken)
        {
            calls.Add(id);
            return Task.CompletedTask;
        }
    }

    private sealed class AbortingTask(string id) : IPipelineTask
    {
        public string Id => id;
        public string DisplayName => id;
        public PipelineTaskKind Kind => PipelineTaskKind.Both;
        public Task ExecuteAsync(PipelineContext context, System.Text.Json.Nodes.JsonNode? config, CancellationToken cancellationToken)
        {
            context.Abort("intentional abort");
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyServices : IServiceProvider
    {
        public static readonly EmptyServices Instance = new();
        public object? GetService(Type serviceType) => null;
    }
}
