using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services.PipelineTasks;

public sealed class NotifyToastTask : IPipelineTask
{
    public const string TaskId = "shareq.notify-toast";

    private readonly IToastNotifier _notifier;
    private readonly EditorLauncher _editorLauncher;
    private readonly ILogger<NotifyToastTask> _logger;

    public NotifyToastTask(IToastNotifier notifier, EditorLauncher editorLauncher, ILogger<NotifyToastTask> logger)
    {
        _notifier = notifier;
        _editorLauncher = editorLauncher;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Notify (toast)";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var title = (string?)config?["title"] ?? "ShareQ";
        var template = (string?)config?["message"] ?? "Done.";
        var message = ExpandPlaceholders(template, context);

        Action? onClick = null;
        if (context.Bag.TryGetValue(PipelineBagKeys.ItemId, out var rawId) && rawId is long itemId
            && context.Bag.TryGetValue(PipelineBagKeys.NewItem, out var rawItem) && rawItem is NewItem item
            && item.Kind == ItemKind.Image)
        {
            onClick = () =>
            {
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    try { await _editorLauncher.OpenAsync(itemId, CancellationToken.None).ConfigureAwait(true); }
                    catch (Exception ex) { _logger.LogError(ex, "Toast→editor open failed for item {Id}", itemId); }
                });
            };
        }

        _notifier.Show(title, message, onClick);
        return Task.CompletedTask;
    }

    private static string ExpandPlaceholders(string template, PipelineContext context)
    {
        if (!template.Contains("{bag.", StringComparison.Ordinal)) return template;

        var sb = new System.Text.StringBuilder(template.Length);
        var i = 0;
        while (i < template.Length)
        {
            if (template[i] == '{' && template.AsSpan(i).StartsWith("{bag.", StringComparison.Ordinal))
            {
                var end = template.IndexOf('}', i);
                if (end < 0) { sb.Append(template, i, template.Length - i); break; }
                var key = template.Substring(i + 5, end - (i + 5));
                if (context.Bag.TryGetValue(key, out var value)) sb.Append(value?.ToString());
                i = end + 1;
            }
            else
            {
                sb.Append(template[i]);
                i++;
            }
        }
        return sb.ToString();
    }
}
