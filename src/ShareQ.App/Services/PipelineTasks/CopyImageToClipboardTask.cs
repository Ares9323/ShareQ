using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Clipboard;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

public sealed class CopyImageToClipboardTask : IPipelineTask
{
    public const string TaskId = "shareq.copy-image-to-clipboard";

    private readonly ILogger<CopyImageToClipboardTask> _logger;
    private readonly IClipboardListener _listener;

    public CopyImageToClipboardTask(ILogger<CopyImageToClipboardTask> logger, IClipboardListener listener)
    {
        _logger = logger;
        _listener = listener;
    }

    public string Id => TaskId;
    public string DisplayName => "Copy image to clipboard";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var raw) || raw is not byte[] pngBytes)
        {
            _logger.LogWarning("CopyImageToClipboardTask: bag key '{Key}' missing or not byte[]; skipping", PipelineBagKeys.PayloadBytes);
            return Task.CompletedTask;
        }

        Application.Current.Dispatcher.Invoke(() =>
        {
            // Tell the listener to ignore the WM_CLIPBOARDUPDATE we are about to cause —
            // otherwise our own write would re-ingest into the on-clipboard pipeline.
            _listener.SuppressNext();
            // Use the PNG-aware publisher so semi-transparent shadows / cut-outs paste with
            // alpha intact in Telegram / Discord / browsers. The legacy SetImage path was
            // turning a soft Shadow glow into a hard neon shape on paste because CF_BITMAP
            // alone strips alpha for most modern consumers.
            ClipboardImagePublisher.SetPng(pngBytes);
        });

        _logger.LogDebug("CopyImageToClipboardTask: image placed on clipboard ({Bytes} bytes)", pngBytes.Length);
        return Task.CompletedTask;
    }
}
