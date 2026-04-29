using System.Diagnostics;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Runs a shell command line via <c>cmd /c</c> so PATH lookups, pipes, redirects, and chained
/// commands all work the way they would in a terminal. Config: <c>command</c> (required, the
/// full command line, %ENV% expanded). Runs detached, no UI window — fire-and-forget. Logs
/// the exit code on completion. For interactive launches that need a console, use
/// <see cref="LaunchAppTask"/> with <c>path=cmd.exe</c> and <c>args=/k …</c>.
/// </summary>
public sealed class RunCommandTask : IPipelineTask
{
    public const string TaskId = "shareq.run-command";

    private readonly ILogger<RunCommandTask> _logger;

    public RunCommandTask(ILogger<RunCommandTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Run command";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var rawCommand = (string?)config?["command"];
        if (string.IsNullOrWhiteSpace(rawCommand))
        {
            _logger.LogWarning("RunCommandTask: no command configured; skipping");
            return Task.CompletedTask;
        }
        var command = Environment.ExpandEnvironmentVariables(rawCommand);

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc is not null)
            {
                // Hook exit just to log the result — we don't block the workflow on the command's
                // completion. Fire-and-forget keeps quick automations snappy; if you need to wait,
                // chain a Delay step or build a "run-and-wait" variant later.
                proc.EnableRaisingEvents = true;
                proc.Exited += (_, _) =>
                {
                    _logger.LogInformation("RunCommandTask: '{Command}' exited with code {Code}",
                        command, proc.ExitCode);
                    proc.Dispose();
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RunCommandTask: failed to start '{Command}'", command);
        }
        return Task.CompletedTask;
    }
}
