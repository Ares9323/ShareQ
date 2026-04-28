using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Pipeline task that puts a templated string onto the system clipboard. Used by the upload
/// pipeline to replace the captured image on the clipboard with the resulting URL, so the user can
/// paste the link straight away. Config: <c>{"template":"{bag.upload_url}"}</c>.
/// </summary>
public sealed class CopyTextToClipboardTask : IPipelineTask
{
    public const string TaskId = "shareq.copy-text-to-clipboard";

    private readonly ILogger<CopyTextToClipboardTask> _logger;

    public CopyTextToClipboardTask(ILogger<CopyTextToClipboardTask> logger)
    {
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Copy text to clipboard";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var template = (string?)config?["template"] ?? string.Empty;
        var text = ExpandPlaceholders(template, context);
        if (string.IsNullOrEmpty(text)) return Task.CompletedTask;

        Application.Current.Dispatcher.Invoke(() =>
        {
            try { System.Windows.Clipboard.SetText(text); }
            catch (Exception ex) { _logger.LogError(ex, "CopyTextToClipboardTask: SetText failed"); }
        });
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
