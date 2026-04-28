using System.Text.Json.Nodes;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.ViewModels;

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
    string? DefaultConfigJson = null);

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
        foreach (var key in new[] { "uploader", "category", "format" })
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
