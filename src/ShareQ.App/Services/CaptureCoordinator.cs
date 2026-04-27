using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Windows;
using ShareQ.Capture;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline;
using ShareQ.Pipeline.Profiles;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

public sealed class CaptureCoordinator
{
    private readonly ICaptureSource _captureSource;
    private readonly PipelineExecutor _executor;
    private readonly IPipelineProfileStore _profiles;
    private readonly IServiceProvider _services;
    private readonly ILogger<CaptureCoordinator> _logger;

    public CaptureCoordinator(
        ICaptureSource captureSource,
        PipelineExecutor executor,
        IPipelineProfileStore profiles,
        IServiceProvider services,
        ILogger<CaptureCoordinator> logger)
    {
        _captureSource = captureSource;
        _executor = executor;
        _profiles = profiles;
        _services = services;
        _logger = logger;
    }

    public async Task CaptureRegionAsync(CancellationToken cancellationToken)
    {
        var region = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var overlay = new RegionOverlayWindow();
            return overlay.PickRegion();
        }).Task.ConfigureAwait(false);

        if (region is null)
        {
            _logger.LogDebug("Capture cancelled (no region)");
            return;
        }

        var captured = await _captureSource.CaptureAsync(region, cancellationToken).ConfigureAwait(false);

        var profile = await _profiles.GetAsync(DefaultPipelineProfiles.RegionCaptureId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("region-capture profile not found; aborting");
            return;
        }

        var ctx = new PipelineContext(_services);
        ctx.Bag[PipelineBagKeys.PayloadBytes] = captured.PngBytes;
        ctx.Bag[PipelineBagKeys.FileExtension] = "png";
        if (!string.IsNullOrEmpty(region.WindowTitle))
        {
            ctx.Bag[PipelineBagKeys.WindowTitle] = region.WindowTitle;
        }
        var searchTextPrefix = string.IsNullOrEmpty(region.WindowTitle) ? "Region" : region.WindowTitle;
        ctx.Bag[PipelineBagKeys.NewItem] = new NewItem(
            Kind: ItemKind.Image,
            Source: ItemSource.CaptureRegion,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: captured.PngBytes,
            PayloadSize: captured.PngBytes.LongLength,
            SearchText: $"{searchTextPrefix} {captured.Width}×{captured.Height}");

        await _executor.RunAsync(profile, ctx, cancellationToken).ConfigureAwait(false);
    }
}
