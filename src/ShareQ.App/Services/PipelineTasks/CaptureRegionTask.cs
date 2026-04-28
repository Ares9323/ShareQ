using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Windows;
using ShareQ.Capture;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// First step of capture-style workflows: opens the region overlay, captures the selected pixels
/// to a PNG and populates the bag (<c>payload_bytes</c>, <c>file_extension</c>, <c>new_item</c>) so
/// subsequent steps (save, history, upload, …) operate on the captured image.
/// Cancelling the overlay aborts the pipeline via <see cref="PipelineContext.Abort"/>.
/// </summary>
public sealed class CaptureRegionTask : IPipelineTask
{
    public const string TaskId = "shareq.capture-region";

    private readonly ICaptureSource _captureSource;
    private readonly ILogger<CaptureRegionTask> _logger;

    public CaptureRegionTask(ICaptureSource captureSource, ILogger<CaptureRegionTask> logger)
    {
        _captureSource = captureSource;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Capture region";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        // If the entry-point already filled the bag (tray Fullscreen / Monitor / Last region pre-fill
        // payload_bytes before invoking the profile) we skip the overlay so the same workflow can
        // serve both hotkey-driven region picks and pre-captured flows.
        if (context.Bag.ContainsKey(PipelineBagKeys.PayloadBytes))
        {
            _logger.LogDebug("CaptureRegionTask: payload already in bag; skipping overlay");
            return;
        }

        var region = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var overlay = new RegionOverlayWindow();
            return overlay.PickRegion();
        }).Task.ConfigureAwait(false);

        if (region is null)
        {
            _logger.LogDebug("CaptureRegionTask: user cancelled the overlay; aborting pipeline");
            context.Abort("region capture cancelled");
            return;
        }

        var captured = await _captureSource.CaptureAsync(region, cancellationToken).ConfigureAwait(false);

        context.Bag[PipelineBagKeys.PayloadBytes] = captured.PngBytes;
        context.Bag[PipelineBagKeys.FileExtension] = "png";
        if (!string.IsNullOrEmpty(region.WindowTitle))
        {
            context.Bag[PipelineBagKeys.WindowTitle] = region.WindowTitle;
        }
        var searchTextPrefix = string.IsNullOrEmpty(region.WindowTitle) ? "Region" : region.WindowTitle;
        context.Bag[PipelineBagKeys.NewItem] = new NewItem(
            Kind: ItemKind.Image,
            Source: ItemSource.CaptureRegion,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: captured.PngBytes,
            PayloadSize: captured.PngBytes.LongLength,
            SearchText: $"{searchTextPrefix} {captured.Width}×{captured.Height}");
    }
}
