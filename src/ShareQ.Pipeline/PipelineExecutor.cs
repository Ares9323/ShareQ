using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Registry;

namespace ShareQ.Pipeline;

public sealed class PipelineExecutor
{
    private readonly IPipelineTaskRegistry? _registry;
    private readonly ILogger<PipelineExecutor> _logger;

    public PipelineExecutor()
    {
        _registry = null;
        _logger = NullLogger<PipelineExecutor>.Instance;
    }

    public PipelineExecutor(ILogger<PipelineExecutor> logger)
    {
        _registry = null;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PipelineExecutor(IPipelineTaskRegistry registry, ILogger<PipelineExecutor> logger)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
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

    public async Task RunAsync(
        PipelineProfile profile,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        if (_registry is null)
            throw new InvalidOperationException("Profile-based RunAsync requires a registry; construct PipelineExecutor with one.");

        for (var index = 0; index < profile.Steps.Count; index++)
        {
            if (context.Aborted) break;
            cancellationToken.ThrowIfCancellationRequested();

            var step = profile.Steps[index];
            if (!step.Enabled)
            {
                _logger.LogDebug("Pipeline profile {Profile} step {Index} ({TaskId}) skipped (disabled)",
                    profile.Id, index, step.TaskId);
                continue;
            }

            var task = _registry.Resolve(step.TaskId);
            if (task is null)
            {
                _logger.LogWarning("Pipeline profile {Profile} step {Index}: task {TaskId} not registered, skipping",
                    profile.Id, index, step.TaskId);
                if (step.AbortOnError) context.Abort($"task {step.TaskId} not registered");
                continue;
            }

            try
            {
                _logger.LogDebug("Pipeline profile {Profile} step {Index}: {TaskId}", profile.Id, index, task.Id);
                await task.ExecuteAsync(context, step.Config, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Pipeline profile {Profile} step {Index} ({TaskId}) threw", profile.Id, index, task.Id);
                if (step.AbortOnError) context.Abort($"task {task.Id} threw: {ex.Message}");
            }
        }
    }
}
