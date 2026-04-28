using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline;
using ShareQ.Pipeline.Profiles;

namespace ShareQ.App.Services;

/// <summary>
/// Runs a workflow (pipeline profile) by id from a fresh <see cref="PipelineContext"/>. Used by
/// the global hotkey hook and by the tray menu when it wants to invoke a workflow without
/// pre-populating any bag keys (steps capture / load their own input).
/// </summary>
public sealed class WorkflowRunner
{
    private readonly PipelineExecutor _executor;
    private readonly IPipelineProfileStore _profiles;
    private readonly IServiceProvider _services;
    private readonly ILogger<WorkflowRunner> _logger;

    public WorkflowRunner(
        PipelineExecutor executor,
        IPipelineProfileStore profiles,
        IServiceProvider services,
        ILogger<WorkflowRunner> logger)
    {
        _executor = executor;
        _profiles = profiles;
        _services = services;
        _logger = logger;
    }

    public async Task RunAsync(string workflowId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(workflowId);
        var profile = await _profiles.GetAsync(workflowId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("WorkflowRunner: workflow '{Id}' not found", workflowId);
            return;
        }
        var ctx = new PipelineContext(_services);
        await _executor.RunAsync(profile, ctx, cancellationToken).ConfigureAwait(false);
    }
}
