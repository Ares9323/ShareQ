using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.PluginContracts;

namespace ShareQ.Plugins.Tasks;

/// <summary>
/// Pipeline step that uploads <c>bag.payload_bytes</c>. Two modes:
/// <list type="bullet">
///   <item>Single uploader by id: <c>{"uploader":"onedrive"}</c></item>
///   <item>Category (user's selection): <c>{"category":"image"}</c> — runs every uploader the user
///         selected for that category and concatenates the URLs (one per line) onto the clipboard.</item>
/// </list>
/// On success populates: <c>bag.upload_url</c> = first URL, <c>bag.upload_urls</c> = newline-joined
/// list, <c>bag.uploader_id</c> = first uploader's id.
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

        var uploaders = await ResolveUploadersAsync(config, cancellationToken).ConfigureAwait(false);
        if (uploaders.Count == 0)
        {
            _logger.LogWarning("UploadTask: no uploader available (none configured/enabled for this step).");
            return;
        }

        var ext = context.Bag.TryGetValue(PipelineBagKeys.FileExtension, out var rawExt) && rawExt is string e ? e : "bin";
        var contentType = ContentTypeFor(ext);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var fileName = $"shareq-{stamp}.{ext.TrimStart('.')}";

        var urls = new List<string>(uploaders.Count);
        string? firstId = null;
        foreach (var uploader in uploaders)
        {
            var result = await uploader.UploadAsync(new UploadRequest(bytes, fileName, contentType), cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                _logger.LogError("UploadTask: '{Id}' failed: {Error}", uploader.Id, result.ErrorMessage);
                continue;
            }
            urls.Add(result.Url!);
            firstId ??= uploader.Id;
            _logger.LogInformation("UploadTask: '{Id}' uploaded {Bytes} bytes → {Url}", uploader.Id, bytes.Length, result.Url);
        }

        if (urls.Count == 0) return;

        context.Bag[PipelineBagKeys.UploadUrl] = urls[0];
        context.Bag[PipelineBagKeys.UploadUrls] = string.Join('\n', urls);
        context.Bag[PipelineBagKeys.UploaderId] = firstId!;
    }

    private async Task<IReadOnlyList<IUploader>> ResolveUploadersAsync(JsonNode? config, CancellationToken cancellationToken)
    {
        // Category mode wins when set: routes through the user's per-category selection.
        var category = (string?)config?["category"];
        if (!string.IsNullOrEmpty(category) && TryParseCategory(category, out var caps))
        {
            return await _resolver.ResolveCategoryAsync(caps, cancellationToken).ConfigureAwait(false);
        }

        // Fallback: single-uploader-by-id (legacy, used by region-capture before the selection UI).
        var uploaderId = (string?)config?["uploader"];
        if (!string.IsNullOrEmpty(uploaderId))
        {
            var uploader = await _resolver.ResolveAsync(uploaderId, cancellationToken).ConfigureAwait(false);
            return uploader is null ? [] : [uploader];
        }
        return [];
    }

    private static bool TryParseCategory(string raw, out UploaderCapabilities category)
    {
        switch (raw.ToLowerInvariant())
        {
            case "image": category = UploaderCapabilities.Image; return true;
            case "file":  category = UploaderCapabilities.File;  return true;
            case "text":  category = UploaderCapabilities.Text;  return true;
            case "video": category = UploaderCapabilities.Video; return true;
            default:      category = UploaderCapabilities.None;  return false;
        }
    }

    private static string ContentTypeFor(string ext) => ext switch
    {
        "png" => "image/png",
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "mp4" => "video/mp4",
        "txt" => "text/plain",
        _ => "application/octet-stream",
    };
}
