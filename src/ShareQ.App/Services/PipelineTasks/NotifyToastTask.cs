using System.Text.Json.Nodes;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

public sealed class NotifyToastTask : IPipelineTask
{
    public const string TaskId = "shareq.notify-toast";

    private readonly IToastNotifier _notifier;

    public NotifyToastTask(IToastNotifier notifier)
    {
        _notifier = notifier;
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

        _notifier.Show(title, message);
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
