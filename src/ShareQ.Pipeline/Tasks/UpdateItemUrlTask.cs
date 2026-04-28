using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.Pipeline.Tasks;

/// <summary>
/// Persists <c>bag.upload_url</c> + <c>bag.uploader_id</c> onto the item identified by
/// <c>bag.item_id</c>. Runs after AddToHistoryTask and an upload step have populated the bag.
/// </summary>
public sealed class UpdateItemUrlTask : IPipelineTask
{
    public const string TaskId = "shareq.update-item-url";

    private readonly IItemStore _items;
    private readonly ILogger<UpdateItemUrlTask> _logger;

    public UpdateItemUrlTask(IItemStore items, ILogger<UpdateItemUrlTask> logger)
    {
        _items = items;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Save uploaded URL on item";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Bag.TryGetValue(PipelineBagKeys.ItemId, out var rawId) || rawId is not long itemId) return;
        if (!context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var rawUrl) || rawUrl is not string url) return;
        var uploaderId = context.Bag.TryGetValue(PipelineBagKeys.UploaderId, out var rawUploader) && rawUploader is string up
            ? up : "unknown";

        var ok = await _items.SetUploadedUrlAsync(itemId, uploaderId, url, cancellationToken).ConfigureAwait(false);
        if (!ok) _logger.LogWarning("UpdateItemUrlTask: SetUploadedUrlAsync({Id}) returned false", itemId);
    }
}
