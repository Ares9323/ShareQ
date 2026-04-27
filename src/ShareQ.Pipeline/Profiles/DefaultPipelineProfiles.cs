using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Tasks;

namespace ShareQ.Pipeline.Profiles;

public static class DefaultPipelineProfiles
{
    public const string OnClipboardId = "on-clipboard";
    public const string RegionCaptureId = "region-capture";

    // Task IDs whose implementations live in ShareQ.App (resolved at runtime by the registry).
    public const string CopyImageToClipboardTaskId = "shareq.copy-image-to-clipboard";
    public const string NotifyToastTaskId = "shareq.notify-toast";

    public static IReadOnlyList<PipelineProfile> All { get; } = BuildAll();

    private static IReadOnlyList<PipelineProfile> BuildAll() =>
    [
        new PipelineProfile(
            Id: OnClipboardId,
            DisplayName: "On clipboard",
            Trigger: "event:clipboard",
            Steps:
            [
                new PipelineStep(AddToHistoryTask.TaskId)
            ]),

        new PipelineProfile(
            Id: RegionCaptureId,
            DisplayName: "Region capture",
            Trigger: "hotkey:region",
            Steps:
            [
                new PipelineStep(SaveToFileTask.TaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"folder\":\"%USERPROFILE%\\\\Pictures\\\\ShareQ\"}")),
                new PipelineStep(AddToHistoryTask.TaskId),
                new PipelineStep(CopyImageToClipboardTaskId),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Saved {bag.local_path}\"}"))
            ])
    ];
}
