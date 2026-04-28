using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.PluginContracts;

namespace ShareQ.Plugins.Tasks;

/// <summary>
/// Pipeline step that uploads <c>bag.payload_bytes</c> via the configured uploader and writes the
/// resulting URL into <c>bag.upload_url</c>. Config: <c>{"uploader":"catbox"}</c> (defaults to
/// Catbox if unspecified). Uploaders are resolved through <see cref="IUploaderResolver"/> so
/// disabled plugins are transparently filtered out.
/// </summary>
public sealed class UploadTask : IPipelineTask
{
    public const string TaskId = "shareq.upload";

    private readonly IUploaderResolver _resolver;
    private readonly ILogger<UploadTask> _logger;

    public UploadTask(IUploaderResolver resolver, ILogger<UploadTask> logger)
    {
        _resolver = resolver;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Upload";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var rawBytes) || rawBytes is not byte[] bytes)
        {
            _logger.LogWarning("UploadTask: bag key '{Key}' missing or not byte[]; skipping", PipelineBagKeys.PayloadBytes);
            return;
        }
        if (bytes.Length == 0) return;

        var uploaderId = (string?)config?["uploader"] ?? "catbox";
        var uploader = await _resolver.ResolveAsync(uploaderId, cancellationToken).ConfigureAwait(false);
        if (uploader is null)
        {
            _logger.LogError("UploadTask: uploader '{Id}' is not available (missing or disabled).", uploaderId);
            return;
        }

        var ext = context.Bag.TryGetValue(PipelineBagKeys.FileExtension, out var rawExt) && rawExt is string e ? e : "bin";
        var contentType = ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "gif" => "image/gif",
            "mp4" => "video/mp4",
            "txt" => "text/plain",
            _ => "application/octet-stream",
        };
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var fileName = $"shareq-{stamp}.{ext.TrimStart('.')}";

        var result = await uploader.UploadAsync(new UploadRequest(bytes, fileName, contentType), cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            _logger.LogError("UploadTask: '{Id}' failed: {Error}", uploaderId, result.ErrorMessage);
            return;
        }

        context.Bag[PipelineBagKeys.UploadUrl] = result.Url!;
        context.Bag[PipelineBagKeys.UploaderId] = uploaderId;
        _logger.LogInformation("UploadTask: '{Id}' uploaded {Bytes} bytes → {Url}", uploaderId, bytes.Length, result.Url);
    }
}
