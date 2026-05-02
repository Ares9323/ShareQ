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

        // If an upload succeeded, prefer the upload-specific template + use the URL as the
        // click target. Falls back to the generic "message" template (e.g. "Saved {bag.local_path}")
        // when no URL is available.
        var hasUploadUrl = context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var rawUrl)
            && rawUrl is string uploadUrl
            && !string.IsNullOrEmpty(uploadUrl);

        string message;
        Action? onClick = null;

        if (hasUploadUrl)
        {
            var url = (string)context.Bag[PipelineBagKeys.UploadUrl]!;
            var uploadTemplate = (string?)config?["uploadMessage"] ?? "Link ready: {bag.upload_url}";
            message = ExpandPlaceholders(uploadTemplate, context);
            onClick = () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true,
                    });
                }
                catch (Exception ex) { _logger.LogError(ex, "Toast→browser open failed for {Url}", url); }
            };
        }
        else
        {
            var template = (string?)config?["message"] ?? "Done.";
            message = ExpandPlaceholders(template, context);

            if (context.Bag.TryGetValue(PipelineBagKeys.ItemId, out var rawId) && rawId is long itemId
                && context.Bag.TryGetValue(PipelineBagKeys.NewItem, out var rawItem) && rawItem is NewItem item)
            {
                if (item.Kind == ItemKind.Image)
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
                else if (item.Kind == ItemKind.Text)
                {
                    // Text payload: if it parses as an http(s) URL — typical for QR-decoded
                    // links — clicking the toast opens it in the default browser. Anything else
                    // (plain text, a hash, an SSID, a TOTP secret) leaves the toast non-clickable;
                    // the text is already on the clipboard so there's no useful "open editor"
                    // affordance for raw strings.
                    var textBytes = item.Payload.ToArray();
                    var maybeUrl = textBytes.Length > 0 && textBytes.Length < 4096
                        ? System.Text.Encoding.UTF8.GetString(textBytes).Trim()
                        : string.Empty;
                    if (Uri.TryCreate(maybeUrl, UriKind.Absolute, out var parsed)
                        && (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps))
                    {
                        onClick = () =>
                        {
                            try
                            {
                                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = parsed.AbsoluteUri,
                                    UseShellExecute = true,
                                });
                            }
                            catch (Exception ex) { _logger.LogError(ex, "Toast→browser open failed for {Url}", parsed.AbsoluteUri); }
                        };
                    }
                }
            }
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
