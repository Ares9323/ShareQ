using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Tasks;

namespace ShareQ.Pipeline.Profiles;

public static class DefaultPipelineProfiles
{
    public const string OnClipboardId = "on-clipboard";
    public const string RegionCaptureId = "region-capture";
    public const string ManualUploadId = "manual-upload";

    // Task IDs whose implementations live in ShareQ.App (resolved at runtime by the registry).
    public const string CopyImageToClipboardTaskId = "shareq.copy-image-to-clipboard";
    public const string CopyTextToClipboardTaskId = "shareq.copy-text-to-clipboard";
    public const string NotifyToastTaskId = "shareq.notify-toast";

    // Task IDs from ShareQ.Plugins.
    public const string UploadTaskId = "shareq.upload";

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
                // Folder comes from Settings → Capture (capture.folder), with default fallback in SaveToFileTask.
                new PipelineStep(SaveToFileTask.TaskId),
                new PipelineStep(AddToHistoryTask.TaskId),
                // Image goes on the clipboard immediately as a fallback so the user has something to
                // paste even if the upload is slow/fails.
                new PipelineStep(CopyImageToClipboardTaskId),
                // Resolves to the user's selected uploaders for the "image" category from Settings.
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"image\"}")),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                // On successful upload, replace the image on the clipboard with the URL(s). When
                // multiple uploaders are selected, upload_urls contains all of them newline-joined.
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}")),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Saved {bag.local_path}\"}"))
            ]),

        new PipelineProfile(
            Id: ManualUploadId,
            DisplayName: "Manual upload",
            Trigger: "menu:upload",
            Steps:
            [
                // No save-to-file (the source is already on disk or clipboard) and no
                // copy-image-to-clipboard (would clobber whatever was already there).
                new PipelineStep(AddToHistoryTask.TaskId),
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"file\"}")),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}")),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Uploaded\"}"))
            ])
    ];
}
