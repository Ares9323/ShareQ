using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Opens a file or folder with its default OS-registered application — same effect as a
/// double-click in Explorer. Config: <c>path</c> (required, %ENV% expanded). For files this
/// goes through the shell, so PDFs land in the PDF reader, .txt in Notepad, etc. For folders
/// it opens an Explorer window. Distinct from <see cref="LaunchAppTask"/> which always treats
/// the target as an executable to spawn directly.
/// </summary>
public sealed class OpenFileTask : IPipelineTask
{
    public const string TaskId = "shareq.open-file";

    private readonly ILogger<OpenFileTask> _logger;

    public OpenFileTask(ILogger<OpenFileTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Open file or folder";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var rawPath = (string?)config?["path"];
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            _logger.LogWarning("OpenFileTask: no path configured; skipping");
            return Task.CompletedTask;
        }
        var path = Environment.ExpandEnvironmentVariables(rawPath).Trim();

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            _logger.LogInformation("OpenFileTask: opened {Path}", path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "OpenFileTask: failed to open {Path}", path);
        }
        return Task.CompletedTask;
    }
}
