using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Opens <c>bag.upload_url</c> (or a custom URL via config) in the default browser. Typical use:
/// drop after an Upload step to auto-launch the result. Skips silently when no URL is available.
/// </summary>
public sealed class OpenUrlTask : IPipelineTask
{
    public const string TaskId = "shareq.open-url";

    private readonly ILogger<OpenUrlTask> _logger;

    public OpenUrlTask(ILogger<OpenUrlTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Open URL";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // Order: explicit config.url > bag.upload_url. Either lets the user wire the task from
        // a non-upload pipeline (e.g. a workflow that just opens a static URL).
        var url = (string?)config?["url"]
                  ?? (context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var raw) && raw is string u ? u : null);
        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogWarning("OpenUrlTask: no URL in config or bag; skipping");
            return Task.CompletedTask;
        }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenUrlTask: failed to launch {Url}", url);
        }
        return Task.CompletedTask;
    }
}
