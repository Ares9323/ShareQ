using System.Text.Json.Nodes;
using ShareQ.Capture.Recording;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Toggles screen recording (start on first run, stop on second). The format (mp4 / gif) comes
/// from <c>config.format</c>. Used as the single step of the screen-recording workflows.
/// </summary>
public sealed class RecordScreenTask : IPipelineTask
{
    public const string TaskId = "shareq.record-screen";

    private readonly Services.Recording.RecordingCoordinator _recorder;

    public RecordScreenTask(Services.Recording.RecordingCoordinator recorder)
    {
        _recorder = recorder;
    }

    public string Id => TaskId;
    public string DisplayName => "Toggle screen recording";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var format = ((string?)config?["format"])?.ToLowerInvariant() switch
        {
            "gif" => RecordingFormat.Gif,
            _ => RecordingFormat.Mp4,
        };
        return _recorder.ToggleAsync(format, cancellationToken);
    }
}
