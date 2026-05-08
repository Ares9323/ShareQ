using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using AresToys.App.Services.Qr;
using AresToys.Clipboard;
using AresToys.Core.Pipeline;

namespace AresToys.App.Services.PipelineTasks;

/// <summary>Generate a QR from the resolved source text and place the PNG on the Windows
/// clipboard. SVG-to-clipboard isn't a thing on Windows (no first-class CF for vector
/// formats), so we keep this PNG-only — the user pastes a raster QR into Discord, Office,
/// whatever. The clipboard listener gets a SuppressNext so our own write doesn't re-ingest
/// into the on-clipboard pipeline as a fresh capture.</summary>
public sealed class CopyQrCodeToClipboardTask : IPipelineTask
{
    public const string TaskId = "arestoys.copy-qr-to-clipboard";

    private readonly ILogger<CopyQrCodeToClipboardTask> _logger;
    private readonly QrCodeService _qr;
    private readonly IClipboardListener _listener;

    public CopyQrCodeToClipboardTask(ILogger<CopyQrCodeToClipboardTask> logger, QrCodeService qr, IClipboardListener listener)
    {
        _logger = logger;
        _qr = qr;
        _listener = listener;
    }

    public string Id => TaskId;
    public string DisplayName => "Copy QR to clipboard";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var text = ResolveText(context, config);
        if (string.IsNullOrEmpty(text)) { _logger.LogWarning("CopyQrCodeToClipboardTask: no text resolved; skipping"); return Task.CompletedTask; }

        var bitmap = _qr.TryRenderBitmap(text);
        if (bitmap is null) return Task.CompletedTask;

        Application.Current.Dispatcher.Invoke(() =>
        {
            _listener.SuppressNext();
            try { System.Windows.Clipboard.SetImage(bitmap); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "CopyQrCodeToClipboardTask: clipboard SetImage failed");
            }
        });
        _logger.LogDebug("CopyQrCodeToClipboardTask: QR placed on clipboard");
        return Task.CompletedTask;
    }

    private static string? ResolveText(PipelineContext context, JsonNode? config)
    {
        if (config?["text"] is { } textNode && textNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return textNode.GetValue<string>();
        if (context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var u) && u is string url) return url;
        if (context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var p) && p is byte[] bytes)
            return System.Text.Encoding.UTF8.GetString(bytes);
        return null;
    }
}
