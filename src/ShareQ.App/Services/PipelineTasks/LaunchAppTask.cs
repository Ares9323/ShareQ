using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Launches an executable / shortcut / batch file. Config keys: <c>path</c> (required, the
/// target — accepts %ENV% expansion), <c>args</c> (optional command-line), <c>workingDir</c>
/// (optional, defaults to the path's directory). Uses <c>UseShellExecute=true</c> so .exe,
/// .lnk, .bat, .cmd, and even URL protocols all resolve through the shell. The MaxLaunchpad
/// equivalent — but composable into any ShareQ workflow.
/// </summary>
public sealed class LaunchAppTask : IPipelineTask
{
    public const string TaskId = "shareq.launch-app";

    private readonly ILogger<LaunchAppTask> _logger;

    public LaunchAppTask(ILogger<LaunchAppTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Launch app";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var rawPath = (string?)config?["path"];
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            _logger.LogWarning("LaunchAppTask: no path configured; skipping");
            return Task.CompletedTask;
        }
        var path = Environment.ExpandEnvironmentVariables(rawPath).Trim();
        var args = Environment.ExpandEnvironmentVariables((string?)config?["args"] ?? string.Empty);
        var workingDir = Environment.ExpandEnvironmentVariables((string?)config?["workingDir"] ?? string.Empty);
        if (string.IsNullOrEmpty(workingDir))
        {
            // Default the working directory to the target's parent so the launched app finds its
            // own resources (matches how Explorer would launch it on double-click).
            try { workingDir = Path.GetDirectoryName(path) ?? string.Empty; }
            catch { workingDir = string.Empty; }
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = workingDir,
            };
            Process.Start(psi);
            _logger.LogInformation("LaunchAppTask: launched {Path} {Args}", path, args);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LaunchAppTask: failed to launch {Path}", path);
        }
        return Task.CompletedTask;
    }
}
