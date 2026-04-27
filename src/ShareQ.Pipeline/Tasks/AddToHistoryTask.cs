using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.Pipeline.Tasks;

public sealed class AddToHistoryTask : IPipelineTask
{
    public const string TaskId = "shareq.add-to-history";

    private readonly IItemStore _items;
    private readonly ILogger<AddToHistoryTask> _logger;

    public AddToHistoryTask(IItemStore items, ILogger<AddToHistoryTask> logger)
    {
        _items = items;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Add to history";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!context.Bag.TryGetValue(PipelineBagKeys.NewItem, out var raw) || raw is not NewItem newItem)
        {
            _logger.LogWarning("AddToHistoryTask: bag key '{Key}' missing or not a NewItem; skipping", PipelineBagKeys.NewItem);
            return;
        }

        var id = await _items.AddAsync(newItem, cancellationToken).ConfigureAwait(false);
        context.Bag[PipelineBagKeys.ItemId] = id;
        _logger.LogDebug("AddToHistoryTask: stored item {Id} ({Kind})", id, newItem.Kind);
    }
}
