namespace ShareQ.Core.Pipeline;

/// <summary>Well-known string keys used in <see cref="PipelineContext.Bag"/> by baked tasks.</summary>
public static class PipelineBagKeys
{
    /// <summary>The <c>NewItem</c> instance to insert via add-to-history. Type: <c>ShareQ.Storage.Items.NewItem</c>.</summary>
    public const string NewItem = "new_item";

    /// <summary>Item id assigned after add-to-history. Type: <c>long</c>.</summary>
    public const string ItemId = "item_id";

    /// <summary>Raw payload bytes a task may write to disk. Type: <c>byte[]</c>.</summary>
    public const string PayloadBytes = "payload_bytes";

    /// <summary>File extension to use when save-to-file builds a path (e.g. "png", "txt"). Type: <c>string</c>.</summary>
    public const string FileExtension = "file_extension";

    /// <summary>Absolute path written by save-to-file. Type: <c>string</c>.</summary>
    public const string LocalPath = "local_path";

    /// <summary>First URL produced by an upload task (or the only one when a single uploader was
    /// selected). Type: <c>string</c>.</summary>
    public const string UploadUrl = "upload_url";

    /// <summary>All URLs produced by the upload task, joined by newline. Useful when multiple
    /// uploaders are selected for a category. Type: <c>string</c>.</summary>
    public const string UploadUrls = "upload_urls";

    /// <summary>Id of the (first) uploader that produced <see cref="UploadUrl"/>. Type: <c>string</c>.</summary>
    public const string UploaderId = "uploader_id";

    /// <summary>Title of the window that was snap-captured (when applicable). Type: <c>string</c>.</summary>
    public const string WindowTitle = "window_title";

    /// <summary>Top-left of the captured region in physical screen pixels — set by the region/
    /// monitor/fullscreen capture tasks so downstream tasks (notably <c>shareq.pin-to-screen</c>)
    /// can reproduce the exact on-screen origin. Type: <c>(int X, int Y)</c>.</summary>
    public const string CaptureScreenPos = "capture_screen_pos";

    /// <summary>Colour produced by the color-sampler / color-picker steps. Downstream
    /// <c>shareq.copy-color-*</c> tasks read this and emit the colour in their respective format
    /// to the clipboard, letting users compose "pick → format" pipelines without per-format
    /// hardcoding inside the sampler. Type: <c>ShareQ.Editor.Model.ShapeColor</c>.</summary>
    public const string Color = "color";
}
