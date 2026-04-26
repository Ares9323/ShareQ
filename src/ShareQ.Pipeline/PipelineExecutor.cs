using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShareQ.Core.Pipeline;

namespace ShareQ.Pipeline;

public sealed class PipelineExecutor
{
    private readonly ILogger<PipelineExecutor> _logger;

    public PipelineExecutor() : this(NullLogger<PipelineExecutor>.Instance) { }

    public PipelineExecutor(ILogger<PipelineExecutor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(
        IReadOnlyList<IPipelineTask> tasks,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(context);

        for (var index = 0; index < tasks.Count; index++)
        {
            if (context.Aborted) break;
            cancellationToken.ThrowIfCancellationRequested();

            var task = tasks[index];
            _logger.LogDebug("Pipeline task {Index}: {TaskId}", index, task.Id);
            await task.ExecuteAsync(context, config: null, cancellationToken).ConfigureAwait(false);
        }
    }
}
