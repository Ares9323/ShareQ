using ShareQ.Core.Pipeline;
using ShareQ.Pipeline.Tasks;

namespace ShareQ.Pipeline.Profiles;

public static class DefaultPipelineProfiles
{
    // Profile / workflow ids
    public const string OnClipboardId       = "on-clipboard";
    public const string RegionCaptureId     = "region-capture";
    public const string ManualUploadId      = "manual-upload";
    public const string UploadClipboardTextId = "upload-clipboard-text";
    public const string ShowPopupId         = "show-popup";
    public const string ToggleIncognitoId   = "toggle-incognito";
    public const string ColorSamplerId      = "color-sampler";
    public const string ColorPickerId       = "color-picker";
    public const string RecordScreenMp4Id   = "record-screen";
    public const string RecordScreenGifId   = "record-screen-gif";
    public const string OpenScreenshotFolderId = "open-screenshot-folder";
    public const string OpenLauncherId         = "open-launcher";

    // Task IDs whose implementations live in ShareQ.App (resolved at runtime by the registry).
    public const string CopyImageToClipboardTaskId = "shareq.copy-image-to-clipboard";
    public const string CopyTextToClipboardTaskId  = "shareq.copy-text-to-clipboard";
    public const string NotifyToastTaskId          = "shareq.notify-toast";
    public const string OpenEditorBeforeUploadTaskId = "shareq.open-editor-before-upload";
    public const string OpenPopupTaskId            = "shareq.open-popup";
    public const string ToggleIncognitoTaskId      = "shareq.toggle-incognito";
    public const string ColorSamplerTaskId         = "shareq.color-sampler";
    public const string ColorPickerTaskId          = "shareq.color-picker";
    public const string CopyColorAsHexTaskId       = "shareq.copy-color-hex";
    public const string CaptureRegionTaskId        = "shareq.capture-region";
    public const string RecordScreenTaskId         = "shareq.record-screen";
    public const string OpenScreenshotFolderTaskId = "shareq.open-screenshot-folder";
    public const string OpenLauncherMenuTaskId     = "shareq.open-launcher-menu";
    public const string UploadClipboardTextTaskId  = "shareq.upload-clipboard-text";

    // Task IDs from ShareQ.Plugins.
    public const string UploadTaskId = "shareq.upload";

    // Hotkey modifier flag values (mirrors ShareQ.Hotkeys.HotkeyModifiers; kept as raw ints so
    // ShareQ.Pipeline doesn't pull in the Hotkeys assembly).
    private const int Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

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
            ],
            IsBuiltIn: true),

        new PipelineProfile(
            Id: RegionCaptureId,
            DisplayName: "Region capture",
            Trigger: "hotkey:region",
            Steps:
            [
                new PipelineStep(CaptureRegionTaskId, Id: "capture-region"),
                new PipelineStep(OpenEditorBeforeUploadTaskId, Enabled: false, Id: "open-editor"),
                new PipelineStep(SaveToFileTask.TaskId, Id: "save"),
                new PipelineStep(AddToHistoryTask.TaskId, Id: "add-to-history"),
                new PipelineStep(CopyImageToClipboardTaskId, Id: "copy-image"),
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"image\"}"), Id: "upload"),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}"), Id: "copy-url"),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Saved {bag.local_path}\"}"), Id: "toast")
            ],
            Hotkey: new HotkeyBinding(Win | Shift, 0x53),  // Win+Shift+S
            IsBuiltIn: true),

        new PipelineProfile(
            Id: ManualUploadId,
            DisplayName: "Manual upload",
            Trigger: "menu:upload",
            Steps:
            [
                new PipelineStep(AddToHistoryTask.TaskId, Id: "add-to-history"),
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"file\"}"), Id: "upload"),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}"), Id: "copy-url"),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Uploaded\"}"), Id: "toast")
            ],
            IsBuiltIn: true),

        new PipelineProfile(
            Id: UploadClipboardTextId,
            DisplayName: "Upload clipboard text",
            Trigger: "hotkey:upload-clipboard-text",
            Steps:
            [
                // Mirrors region-capture but for text: pull text from clipboard → bag, upload to
                // text-category destinations (paste.rs / Pastebin / Gist / OneDrive / GoogleDrive
                // / Dropbox), copy resulting URL back to clipboard, toast. Lets the user grab any
                // selection (Ctrl+C anywhere) and turn it into a shareable link with one hotkey.
                new PipelineStep(UploadClipboardTextTaskId, Id: "read-clipboard-text"),
                new PipelineStep(AddToHistoryTask.TaskId, Id: "add-to-history"),
                new PipelineStep(UploadTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"category\":\"text\"}"), Id: "upload"),
                new PipelineStep(UpdateItemUrlTask.TaskId),
                new PipelineStep(CopyTextToClipboardTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"template\":\"{bag.upload_urls}\"}"), Id: "copy-url"),
                new PipelineStep(NotifyToastTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"title\":\"ShareQ\",\"message\":\"Uploaded text → {bag.upload_url}\"}"), Id: "toast")
            ],
            // No default hotkey — text upload is rarely a one-handed flow (you've usually got the
            // text already in the clipboard from elsewhere). User binds in Settings if desired.
            IsBuiltIn: true),

        new PipelineProfile(
            Id: ShowPopupId,
            DisplayName: "Show clipboard popup",
            Trigger: "hotkey:popup",
            Steps:
            [
                new PipelineStep(OpenPopupTaskId, Id: "show-popup")
            ],
            Hotkey: new HotkeyBinding(Win, 0x56),  // Win+V
            IsBuiltIn: true),

        new PipelineProfile(
            Id: ToggleIncognitoId,
            DisplayName: "Toggle incognito",
            Trigger: "hotkey:incognito",
            Steps:
            [
                new PipelineStep(ToggleIncognitoTaskId, Id: "toggle-incognito")
            ],
            Hotkey: new HotkeyBinding(Win | Shift, 0x4E),  // Win+Shift+N
            IsBuiltIn: true),

        new PipelineProfile(
            Id: ColorSamplerId,
            DisplayName: "Color sampler",
            Trigger: "hotkey:color-sampler",
            Steps:
            [
                // Sample → emit hex. Composable: user can swap CopyColorAsHex for any other
                // CopyColorAs* (RGB, FLinearColor, CMYK, …) without touching the sampler.
                new PipelineStep(ColorSamplerTaskId, Id: "color-sampler"),
                new PipelineStep(CopyColorAsHexTaskId, Id: "copy-as-hex")
            ],
            Hotkey: new HotkeyBinding(Ctrl, 0xDC),  // Ctrl+\
            IsBuiltIn: true),

        new PipelineProfile(
            Id: ColorPickerId,
            DisplayName: "Color picker",
            Trigger: "hotkey:color-picker",
            Steps:
            [
                new PipelineStep(ColorPickerTaskId, Id: "color-picker"),
                new PipelineStep(CopyColorAsHexTaskId, Id: "copy-as-hex")
            ],
            // No default hotkey — picker is mostly invoked via tray / workflow UI; the user can
            // bind one in Settings if they want a one-shot dialog launch.
            IsBuiltIn: true),

        new PipelineProfile(
            Id: RecordScreenMp4Id,
            DisplayName: "Screen recording (mp4)",
            Trigger: "hotkey:record",
            Steps:
            [
                new PipelineStep(RecordScreenTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"format\":\"mp4\"}"), Id: "record-screen")
            ],
            Hotkey: new HotkeyBinding(Shift, 0x2C),  // Shift+PrintScreen
            IsBuiltIn: true),

        new PipelineProfile(
            Id: RecordScreenGifId,
            DisplayName: "Screen recording (gif)",
            Trigger: "hotkey:record-gif",
            Steps:
            [
                new PipelineStep(RecordScreenTaskId, Config: System.Text.Json.Nodes.JsonNode.Parse("{\"format\":\"gif\"}"), Id: "record-screen-gif")
            ],
            Hotkey: new HotkeyBinding(Ctrl | Shift, 0x2C),  // Ctrl+Shift+PrintScreen
            IsBuiltIn: true),

        new PipelineProfile(
            Id: OpenScreenshotFolderId,
            DisplayName: "Open screenshot folder",
            Trigger: "hotkey:open-screenshot-folder",
            Steps:
            [
                new PipelineStep(OpenScreenshotFolderTaskId, Id: "open-screenshot-folder")
            ],
            // No default hotkey — user assigns one if desired.
            IsBuiltIn: true),

        new PipelineProfile(
            Id: OpenLauncherId,
            DisplayName: "Open launcher",
            Trigger: "hotkey:launcher",
            Steps:
            [
                new PipelineStep(OpenLauncherMenuTaskId, Id: "open-launcher-menu")
            ],
            // Win+Z — picked because the Z position is unused by stock Windows shortcuts and
            // sits where the right-hand stays naturally in muscle-memory keyboard navigation.
            // The task itself toggles open/close so a second press dismisses the launcher.
            Hotkey: new HotkeyBinding(Win, 0x5A),
            IsBuiltIn: true),
    ];
}
