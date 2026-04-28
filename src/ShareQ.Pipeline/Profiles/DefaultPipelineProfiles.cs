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
    public const string OpenEditorBeforeUploadTaskId = "shareq.open-editor-before-upload";

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
                // Each toggleable step carries an Id so Settings → Capture → After capture tasks
                // can override its enabled flag per profile/step. Steps with null id are mandatory
                // (e.g. UpdateItemUrl is internal plumbing).
                new PipelineStep(OpenEditorBeforeUploadTaskId, Enabled: false, Id: "open-editor"),
                new PipelineStep(SaveToFileTask.TaskId, Id: "save"),
                new PipelineStep(AddToHistoryTask.TaskId, Id: "add-to-history"),
                new PipelineStep(CopyImageToClipboardTaskId, Id: "copy-image"),
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"image\"}"), Id: "upload"),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}"), Id: "copy-url"),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Saved {bag.local_path}\"}"), Id: "toast")
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
