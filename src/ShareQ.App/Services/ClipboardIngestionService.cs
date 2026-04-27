using System.Text;
using Microsoft.Extensions.Logging;
using ShareQ.Clipboard;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline;
using ShareQ.Pipeline.Profiles;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

public sealed class ClipboardIngestionService : IDisposable
{
    private readonly IClipboardListener _listener;
    private readonly IClipboardReader _reader;
    private readonly IClipboardCaptureGate _gate;
    private readonly PipelineExecutor _executor;
    private readonly IPipelineProfileStore _profiles;
    private readonly IServiceProvider _services;
    private readonly ILogger<ClipboardIngestionService> _logger;
    private IntPtr _ownerHwnd;

    public ClipboardIngestionService(
        IClipboardListener listener,
        IClipboardReader reader,
        IClipboardCaptureGate gate,
        PipelineExecutor executor,
        IPipelineProfileStore profiles,
        IServiceProvider services,
        ILogger<ClipboardIngestionService> logger)
    {
        _listener = listener;
        _reader = reader;
        _gate = gate;
        _executor = executor;
        _profiles = profiles;
        _services = services;
        _logger = logger;
    }

    public void Start(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        _listener.Attach(ownerHwnd);
        _listener.ClipboardUpdated += OnClipboardUpdated;
    }

    private async void OnClipboardUpdated(object? sender, EventArgs e)
    {
        try
        {
            var decision = _gate.Evaluate();
            if (!decision.Allow)
            {
                _logger.LogInformation("Clipboard event dropped by gate: {Reason}", decision.Reason);
                return;
            }

            var change = _reader.ReadCurrent(_ownerHwnd);
            if (change is null)
            {
                _logger.LogInformation("Clipboard event dropped: reader returned null (no recognized format)");
                return;
            }

            _logger.LogInformation("Clipboard event captured: format={Format} payloadBytes={Bytes} source={Source}",
                change.Format, change.Payload.Length, change.SourceProcess);

            var newItem = MapToNewItem(change);
            var profile = await _profiles.GetAsync(DefaultPipelineProfiles.OnClipboardId, CancellationToken.None).ConfigureAwait(false);
            if (profile is null)
            {
                _logger.LogWarning("on-clipboard profile missing; falling back to no-op");
                return;
            }

            var ctx = new PipelineContext(_services);
            ctx.Bag[PipelineBagKeys.NewItem] = newItem;
            await _executor.RunAsync(profile, ctx, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clipboard ingestion failed");
        }
    }

    private static NewItem MapToNewItem(ClipboardChange change)
    {
        var (kind, payload, payloadSize, searchText) = change.Format switch
        {
            ClipboardFormat.Text => (ItemKind.Text, change.Payload, (long)change.Payload.Length, change.PreviewText),
            ClipboardFormat.Html => (ItemKind.Html, change.Payload, (long)change.Payload.Length, change.PreviewText),
            ClipboardFormat.Rtf => (ItemKind.Rtf, change.Payload, (long)change.Payload.Length, change.PreviewText),
            ClipboardFormat.Image => (ItemKind.Image, change.Payload, (long)change.Payload.Length, change.PreviewText),
            ClipboardFormat.Files => (
                ItemKind.Files,
                (ReadOnlyMemory<byte>)Encoding.UTF8.GetBytes(string.Join('\n', change.FilePaths ?? [])),
                (long)(change.FilePaths?.Sum(p => p.Length) ?? 0),
                change.PreviewText),
            _ => (ItemKind.Text, change.Payload, (long)change.Payload.Length, change.PreviewText)
        };

        return new NewItem(
            Kind: kind,
            Source: ItemSource.Clipboard,
            CreatedAt: change.CapturedAt,
            Payload: payload,
            PayloadSize: payloadSize,
            SourceProcess: change.SourceProcess,
            SourceWindow: change.SourceWindow,
            SearchText: searchText);
    }

    public void Dispose()
    {
        _listener.ClipboardUpdated -= OnClipboardUpdated;
    }
}
