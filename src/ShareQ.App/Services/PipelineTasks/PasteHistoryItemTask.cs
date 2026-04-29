using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Auto-pastes the N-th most recent clipboard history item into the foreground window — the
/// pipeline-step counterpart of the popup's <c>Ctrl+1..9</c> quick-paste shortcut.
/// Index is 1-based (<c>1</c> = most recent). Reads <c>config.index</c>; defaults to 1.
/// </summary>
public sealed class PasteHistoryItemTask : IPipelineTask
{
    public const string TaskId = "shareq.paste-history-item";

    private readonly IItemStore _items;
    private readonly AutoPaster _paster;
    private readonly ILogger<PasteHistoryItemTask> _logger;

    public PasteHistoryItemTask(IItemStore items, AutoPaster paster, ILogger<PasteHistoryItemTask> logger)
    {
        _items = items;
        _paster = paster;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Paste history item";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    /// <summary>Bag key holding the per-run snapshot (long[] of item ids) of the most-recent
    /// history items, taken at the first paste step. Subsequent paste steps in the same workflow
    /// run read from this snapshot so the indexes stay stable even when an in-flight paste gets
    /// re-ingested into history (which would otherwise shift everything down by one).</summary>
    private const string HistorySnapshotKey = "paste_history_snapshot";

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var index = (int?)config?["index"] ?? 1;
        if (index < 1) index = 1;

        var snapshot = await GetOrLoadSnapshotAsync(context, cancellationToken).ConfigureAwait(false);
        if (snapshot.Length < index)
        {
            _logger.LogWarning("PasteHistoryItemTask: only {Count} items in history snapshot, can't paste index {Index}", snapshot.Length, index);
            return;
        }

        var itemId = snapshot[index - 1];
        await _paster.PasteAsync(itemId, cancellationToken).ConfigureAwait(false);

        // AutoPaster sends Ctrl+V then returns immediately — Win32 doesn't surface a "paste
        // consumed" signal. If we let the next step run right away it can overwrite the clipboard
        // before the target window has processed our Ctrl+V, so the first paste actually inserts
        // the second step's content. ~250ms is enough for the target's message pump to drain on
        // every editor tested (VS Code, Word, browser inputs) without feeling sluggish.
        await Task.Delay(250, cancellationToken).ConfigureAwait(false);
    }

    private async Task<long[]> GetOrLoadSnapshotAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Bag.TryGetValue(HistorySnapshotKey, out var raw) && raw is long[] cached)
            return cached;

        // 99 covers the IntParameter max — bigger than any reasonable workflow's needs and still a
        // single bounded SQL query. Payload-less listing keeps DPAPI decryption out of the loop;
        // AutoPaster re-fetches the full record by id when actually pasting.
        var loaded = await _items.ListAsync(new ItemQuery(Limit: 99, IncludePayload: false), cancellationToken).ConfigureAwait(false);
        var snapshot = loaded.Select(i => i.Id).ToArray();
        context.Bag[HistorySnapshotKey] = snapshot;
        return snapshot;
    }
}
