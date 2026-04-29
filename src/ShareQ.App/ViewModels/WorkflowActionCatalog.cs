using System.Text.Json.Nodes;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.ViewModels;

/// <summary>Declarative description of a single integer-valued config parameter the user can edit
/// inline on a step row. Today only used for <c>shareq.paste-history-item</c>'s <c>index</c>; the
/// shape lets us add similar one-knob parameters (delay ms, retry count, …) without bespoke UI.</summary>
public sealed record IntParameter(string Key, string Label, int DefaultValue, int Min, int Max);

/// <summary>One entry in the "+ Add step" picker for workflows. Maps a pipeline task id to
/// human-readable metadata + a default config to apply when the user adds the action.</summary>
public sealed record WorkflowActionDescriptor(
    string TaskId,
    string DisplayName,
    string Description,
    string Category,
    /// <summary>True for steps the executor needs to run but aren't user-pickable (e.g. URL persistence
    /// after upload). They're hidden from the workflow editor and from the Add picker.</summary>
    bool IsPlumbing = false,
    /// <summary>JSON config string applied as default when the user adds this action via the picker.</summary>
    string? DefaultConfigJson = null,
    /// <summary>If set, the editor renders an inline integer input on the step row, bound to the
    /// matching key in <see cref="System.Text.Json.Nodes.JsonNode"/> step config.</summary>
    IntParameter? IntParameter = null);

public static class WorkflowActionCatalog
{
    public static readonly IReadOnlyList<WorkflowActionDescriptor> All =
    [
        new("shareq.capture-region",
            "Capture region",
            "Show the region selection overlay and capture the chosen rectangle. Skipped automatically when a payload is already in the bag (e.g. fullscreen / monitor entry-points).",
            "Capture"),

        new("shareq.record-screen",
            "Start/stop screen recording (mp4)",
            "Toggle FFmpeg-driven screen recording in mp4 format. First invocation starts, second stops and produces the file.",
            "Capture",
            DefaultConfigJson: "{\"format\":\"mp4\"}"),

        new("shareq.record-screen",
            "Start/stop screen recording (gif)",
            "Toggle FFmpeg-driven screen recording in animated GIF format. First invocation starts, second stops and produces the file.",
            "Capture",
            DefaultConfigJson: "{\"format\":\"gif\"}"),

        new("shareq.color-picker",
            "Screen color picker",
            "Open the magnifier-style picker at the cursor; the chosen color is copied to the clipboard.",
            "Capture"),

        new("shareq.open-editor-before-upload",
            "Open editor",
            "Pause the pipeline and open the annotation editor on the captured bytes. On save, subsequent steps see the edited image.",
            "Editor"),

        new("shareq.save-to-file",
            "Save to file",
            "Write the current bytes to disk under the configured capture folder (Settings → Capture).",
            "I/O"),

        new("shareq.add-to-history",
            "Add to clipboard history",
            "Index the item in ShareQ's history so it shows up in Win+V.",
            "I/O"),

        new("shareq.copy-image-to-clipboard",
            "Copy image to clipboard",
            "Place the bitmap on the clipboard (overwritten by later text-to-clipboard steps).",
            "I/O"),

        new("shareq.paste-history-item",
            "Paste history item",
            "Auto-paste the N-th most recent clipboard history item into the foreground window. Index is 1-based (1 = most recent), same ordering as the popup's Ctrl+1..9 shortcuts. The history snapshot is frozen at the start of the workflow run so chained paste steps target the items the user expects, not whatever just got re-ingested.",
            "Clipboard",
            DefaultConfigJson: "{\"index\":1}",
            IntParameter: new IntParameter(Key: "index", Label: "Index (1 = most recent)", DefaultValue: 1, Min: 1, Max: 99)),

        new("shareq.press-key",
            "Press Enter",
            "Send a single Enter keystroke to the foreground window. Useful as a separator between paste steps when chaining clipboard items onto consecutive lines.",
            "Clipboard",
            DefaultConfigJson: "{\"key\":\"enter\"}"),

        new("shareq.press-key",
            "Press Tab",
            "Send a single Tab keystroke to the foreground window — handy for moving between fields between paste steps.",
            "Clipboard",
            DefaultConfigJson: "{\"key\":\"tab\"}"),

        new("shareq.delay",
            "Delay",
            "Pause the workflow for the configured number of milliseconds. Useful between paste / press-key steps when the target window is slow to process keystrokes.",
            "Flow",
            DefaultConfigJson: "{\"ms\":250}",
            IntParameter: new IntParameter(Key: "ms", Label: "Milliseconds", DefaultValue: 250, Min: 0, Max: 60000)),

        new("shareq.copy-text-to-clipboard",
            "Copy URL to clipboard",
            "Replace the current clipboard content with the upload URL(s) returned by the upload step.",
            "I/O",
            DefaultConfigJson: "{\"template\":\"{bag.upload_urls}\"}"),

        new("shareq.upload",
            "Upload to selected image uploaders",
            "Run every uploader the user has selected for the image category (Settings → Plugins → image).",
            "Upload",
            DefaultConfigJson: "{\"category\":\"image\"}"),

        new("shareq.upload",
            "Upload to selected file uploaders",
            "Run every uploader the user has selected for the file category (Settings → Plugins → file).",
            "Upload",
            DefaultConfigJson: "{\"category\":\"file\"}"),

        new("shareq.update-item-url",
            "Update item URL",
            "Persists the upload URL on the history item so the popup shows it. Auto-injected after upload — not user-picked.",
            "I/O",
            IsPlumbing: true),

        new("shareq.notify-toast",
            "Show toast notification",
            "Display a Windows toast confirming the operation. Click opens the URL when present.",
            "Notify",
            DefaultConfigJson: "{\"title\":\"ShareQ\",\"message\":\"Done.\"}"),

        new("shareq.open-popup",
            "Show clipboard popup",
            "Open the ShareQ clipboard popup window (the Win+V replacement).",
            "Tools"),

        new("shareq.toggle-incognito",
            "Toggle incognito mode",
            "Flip incognito on/off — when on, clipboard items aren't captured into history.",
            "Tools"),

        new("shareq.open-screenshot-folder",
            "Open screenshot folder",
            "Open the configured capture folder (Settings → Capture) in Windows Explorer.",
            "Tools"),

        new("shareq.show-in-explorer",
            "Show file in Explorer",
            "Open Windows Explorer with the just-saved file pre-selected. Requires a preceding Save to file step.",
            "I/O"),

        new("shareq.save-as",
            "Save image as…",
            "Open a Save File dialog so the user picks the destination + filename. The chosen path becomes the new local_path for subsequent steps.",
            "I/O"),

        new("shareq.open-url",
            "Open URL in browser",
            "Launch the default browser on the upload URL (or an explicit URL via config). Useful right after an Upload step.",
            "Notify"),

        new("shareq.show-qr-code",
            "Show QR code",
            "Generate a QR code from the upload URL (or explicit text) and pop a small window. Handy for scanning the link on a phone.",
            "Notify"),

        new("shareq.pin-to-screen",
            "Pin image to screen",
            "Show the captured image in an always-on-top window. Drag to move, wheel to zoom, right-click or Esc to close.",
            "Tools"),
    ];

    /// <summary>Only the user-pickable subset, grouped by category. Drives the "+ Add step" menu.</summary>
    public static IEnumerable<IGrouping<string, WorkflowActionDescriptor>> Pickable()
        => All.Where(a => !a.IsPlumbing).GroupBy(a => a.Category);

    /// <summary>Find the descriptor for a task id, ignoring config. When multiple descriptors share
    /// the same task id (variants like record-screen mp4 / gif) returns the first; for render-time
    /// disambiguation use <see cref="LookupForStep"/>.</summary>
    public static WorkflowActionDescriptor? Lookup(string taskId)
        => All.FirstOrDefault(a => a.TaskId == taskId);

    /// <summary>Find the descriptor that best matches an existing step within a given catalog
    /// snapshot. When multiple descriptors share the task id (variants like record-screen mp4/gif
    /// or upload-to-onedrive vs upload-to-catbox) prefer the one whose
    /// <see cref="WorkflowActionDescriptor.DefaultConfigJson"/> matches the step's config on a
    /// disambiguation key — checked in order: <c>uploader</c>, <c>category</c>, <c>format</c>.
    /// Falls back to the first match by task id.</summary>
    public static WorkflowActionDescriptor? LookupForStep(IReadOnlyList<WorkflowActionDescriptor> catalog, PipelineStep step)
    {
        var matches = catalog.Where(a => a.TaskId == step.TaskId).ToList();
        if (matches.Count == 0) return null;
        if (matches.Count == 1) return matches[0];
        foreach (var key in new[] { "uploader", "category", "format", "key" })
        {
            var stepValue = (string?)step.Config?[key];
            if (string.IsNullOrEmpty(stepValue)) continue;
            foreach (var candidate in matches)
            {
                if (string.IsNullOrEmpty(candidate.DefaultConfigJson)) continue;
                var candidateValue = (string?)JsonNode.Parse(candidate.DefaultConfigJson)?[key];
                if (string.Equals(stepValue, candidateValue, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
        }
        return matches[0];
    }

    /// <summary>Convenience overload that uses the static catalog (used by call sites that don't
    /// need the per-uploader dynamic entries).</summary>
    public static WorkflowActionDescriptor? LookupForStep(PipelineStep step) => LookupForStep(All, step);
}
