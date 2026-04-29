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

        _logger.LogInformation("Pipeline starting ({Count} steps)", tasks.Count);
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        for (var index = 0; index < tasks.Count; index++)
        {
            if (context.Aborted)
            {
                _logger.LogInformation("Pipeline aborted at step {Index} ({Why})", index, context.AbortReason ?? "no reason");
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();

            var task = tasks[index];
            _logger.LogInformation("→ Step {Index}/{Total}: {TaskId}", index + 1, tasks.Count, task.Id);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await task.ExecuteAsync(context, config: null, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("✓ Step {Index} {TaskId} done in {Ms} ms", index + 1, task.Id, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "✗ Step {Index} {TaskId} threw after {Ms} ms", index + 1, task.Id, sw.ElapsedMilliseconds);
                throw;
            }
        }
        totalSw.Stop();
        _logger.LogInformation("Pipeline done in {Ms} ms", totalSw.ElapsedMilliseconds);
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

        _logger.LogInformation("Workflow '{Profile}' starting ({Count} steps)", profile.Id, profile.Steps.Count);
        var totalSw = System.Diagnostics.Stopwatch.StartNew();
        var ran = 0;
        for (var index = 0; index < profile.Steps.Count; index++)
        {
            if (context.Aborted)
            {
                _logger.LogInformation("Workflow '{Profile}' aborted at step {Index}: {Why}",
                    profile.Id, index, context.AbortReason ?? "no reason");
                break;
            }
            cancellationToken.ThrowIfCancellationRequested();

            var step = profile.Steps[index];
            if (!step.Enabled)
            {
                _logger.LogDebug("Workflow '{Profile}' step {Index} ({TaskId}) skipped (disabled)",
                    profile.Id, index, step.TaskId);
                continue;
            }

            var task = _registry.Resolve(step.TaskId);
            if (task is null)
            {
                _logger.LogWarning("Workflow '{Profile}' step {Index}: task {TaskId} not registered, skipping",
                    profile.Id, index, step.TaskId);
                if (step.AbortOnError) context.Abort($"task {step.TaskId} not registered");
                continue;
            }

            ran++;
            _logger.LogInformation("→ '{Profile}' step {Index}: {TaskId}", profile.Id, index + 1, task.Id);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                await task.ExecuteAsync(context, step.Config, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                _logger.LogInformation("✓ '{Profile}' step {Index} {TaskId} done in {Ms} ms",
                    profile.Id, index + 1, task.Id, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "✗ '{Profile}' step {Index} ({TaskId}) threw after {Ms} ms",
                    profile.Id, index + 1, task.Id, sw.ElapsedMilliseconds);
                if (step.AbortOnError) context.Abort($"task {task.Id} threw: {ex.Message}");
            }
        }
        totalSw.Stop();
        _logger.LogInformation("Workflow '{Profile}' done — {Ran} step(s) ran in {Ms} ms",
            profile.Id, ran, totalSw.ElapsedMilliseconds);
    }
}
