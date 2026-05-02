using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Capture;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// First step of the active-monitor workflow: captures the monitor currently under the mouse
/// cursor. Companion to <see cref="CaptureActiveWindowTask"/> — same delay handling and bag
/// conventions, just a different "what region do I grab" rule. The single-monitor case still
/// works (the cursor is always on the only monitor) so this profile doubles as a "fullscreen
/// of whichever screen is in front of me" hotkey.
///
/// Per-step config (optional):
/// <list type="bullet">
///   <item><c>delay_seconds</c>: int — overrides the global delay.</item>
/// </list>
/// </summary>
public sealed class CaptureActiveMonitorTask : IPipelineTask
{
    public const string TaskId = "shareq.capture-active-monitor";

    private readonly ICaptureSource _captureSource;
    private readonly ISettingsStore _settings;
    private readonly ILogger<CaptureActiveMonitorTask> _logger;

    public CaptureActiveMonitorTask(ICaptureSource captureSource, ISettingsStore settings, ILogger<CaptureActiveMonitorTask> logger)
    {
        _captureSource = captureSource;
        _settings = settings;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Capture active monitor";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Bag.ContainsKey(PipelineBagKeys.PayloadBytes))
        {
            _logger.LogDebug("CaptureActiveMonitorTask: payload already in bag; skipping capture");
            return;
        }

        var delaySeconds = (int?)config?["delay_seconds"] ?? await ReadGlobalDelayAsync(cancellationToken).ConfigureAwait(false);
        if (delaySeconds > 0)
        {
            _logger.LogDebug("CaptureActiveMonitorTask: waiting {Seconds}s before capture", delaySeconds);
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 30)), cancellationToken).ConfigureAwait(false);
        }

        var monitor = MonitorEnumeration.GetMonitorUnderCursor();
        if (monitor is null)
        {
            _logger.LogWarning("CaptureActiveMonitorTask: no monitors detected; aborting workflow");
            context.Abort("no monitors detected");
            return;
        }

        var region = new CaptureRegion(monitor.X, monitor.Y, monitor.Width, monitor.Height, $"Monitor {monitor.Name}");
        var captured = await _captureSource.CaptureAsync(region, cancellationToken).ConfigureAwait(false);

        context.Bag[PipelineBagKeys.PayloadBytes] = captured.PngBytes;
        context.Bag[PipelineBagKeys.FileExtension] = "png";
        context.Bag[PipelineBagKeys.CaptureScreenPos] = (region.X, region.Y);
        context.Bag[PipelineBagKeys.WindowTitle] = region.WindowTitle!;
        context.Bag[PipelineBagKeys.NewItem] = new NewItem(
            Kind: ItemKind.Image,
            Source: ItemSource.CaptureMonitor,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: captured.PngBytes,
            PayloadSize: captured.PngBytes.LongLength,
            SearchText: $"{region.WindowTitle} {captured.Width}×{captured.Height}");

        _logger.LogInformation("CaptureActiveMonitorTask: captured monitor '{Name}' at ({X}, {Y}) {W}×{H} px",
            monitor.Name, region.X, region.Y, region.Width, region.Height);
    }

    private async Task<int> ReadGlobalDelayAsync(CancellationToken cancellationToken)
    {
        var raw = await _settings.GetAsync("capture.delay_seconds", cancellationToken).ConfigureAwait(false);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }
}
