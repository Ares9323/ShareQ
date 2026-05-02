using System.Text;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>Pulls the current text from the system clipboard, encodes it as UTF-8, and stuffs it
/// into the pipeline bag as <c>payload_bytes</c> + <c>file_extension = "txt"</c>. Mirror of
/// <see cref="CaptureRegionTask"/> but for text — it's the only piece that was missing for the
/// "upload text from clipboard" flow to work end-to-end. Downstream <see cref="UploadTask"/>
/// (with <c>category=text</c>) routes the result to whichever Text uploaders the user has
/// selected (paste.rs, Pastebin, Gist, or any AnyFile destination).
///
/// Skips silently when the clipboard has no text (rather than failing the whole workflow) so
/// hotkey misfires are forgiving — the user can press the bind again after copying something.</summary>
public sealed class UploadClipboardTextTask : IPipelineTask
{
    public const string TaskId = "shareq.upload-clipboard-text";

    private readonly ILogger<UploadClipboardTextTask> _logger;

    public UploadClipboardTextTask(ILogger<UploadClipboardTextTask> logger) => _logger = logger;

    public string Id => TaskId;
    public string DisplayName => "Upload clipboard text";
    public PipelineTaskKind Kind => PipelineTaskKind.PostClipboard;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Clipboard.GetText must run on the UI/STA thread. Invoke synchronously because the rest
        // of the pipeline expects payload_bytes already populated when we return.
        string? text = null;
        Application.Current.Dispatcher.Invoke(() =>
        {
            try { if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText(); }
            catch (Exception ex) { _logger.LogWarning(ex, "UploadClipboardTextTask: clipboard read failed"); }
        });

        if (string.IsNullOrEmpty(text))
        {
            _logger.LogInformation("UploadClipboardTextTask: clipboard has no text — skipping upload");
            return Task.CompletedTask;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        context.Bag[PipelineBagKeys.PayloadBytes] = bytes;
        context.Bag[PipelineBagKeys.FileExtension] = "txt";
        _logger.LogDebug("UploadClipboardTextTask: queued {Bytes} bytes of text for upload", bytes.Length);
        return Task.CompletedTask;
    }
}
