using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Views;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// First step of the webpage-capture workflow. Renders the supplied URL in a hidden WebView2
/// (via <see cref="WebpageCaptureService"/>) and stuffs the resulting full-page PNG into the bag
/// so the rest of the workflow (save / history / upload / …) is identical to the screenshot
/// pipelines.
///
/// URL resolution:
/// <list type="bullet">
///   <item>If <c>config.url</c> is set → use it directly (lets the user wire a "capture example.com
///         every Monday" workflow without a prompt).</item>
///   <item>Otherwise → show the <see cref="WebpageUrlDialog"/> to ask the user. Cancel aborts.</item>
/// </list>
///
/// Per-step config (optional):
/// <list type="bullet">
///   <item><c>url</c>: string — pre-filled or fully-automated URL.</item>
/// </list>
/// </summary>
public sealed class CaptureWebpageTask : IPipelineTask
{
    public const string TaskId = "shareq.capture-webpage";

    private readonly WebpageCaptureService _capture;
    private readonly ILogger<CaptureWebpageTask> _logger;

    public CaptureWebpageTask(WebpageCaptureService capture, ILogger<CaptureWebpageTask> logger)
    {
        _capture = capture;
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Capture webpage";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Bag.ContainsKey(PipelineBagKeys.PayloadBytes))
        {
            _logger.LogDebug("CaptureWebpageTask: payload already in bag; skipping capture");
            return;
        }

        var configUrl = (string?)config?["url"];
        var url = string.IsNullOrWhiteSpace(configUrl)
            ? await PromptForUrlAsync().ConfigureAwait(false)
            : configUrl.Trim();

        if (string.IsNullOrWhiteSpace(url))
        {
            _logger.LogDebug("CaptureWebpageTask: user cancelled URL prompt");
            context.Abort("webpage capture cancelled");
            return;
        }

        var bytes = await _capture.CaptureAsync(url, cancellationToken).ConfigureAwait(false);
        if (bytes is null || bytes.Length == 0)
        {
            _logger.LogWarning("CaptureWebpageTask: capture returned no bytes for {Url}", url);
            context.Abort("webpage capture failed");
            return;
        }

        context.Bag[PipelineBagKeys.PayloadBytes] = bytes;
        context.Bag[PipelineBagKeys.FileExtension] = "png";
        // Stash the URL in the window-title slot so save-to-file can use a meaningful name and
        // toast / template substitution can show what was captured.
        context.Bag[PipelineBagKeys.WindowTitle] = url;
        context.Bag[PipelineBagKeys.NewItem] = new NewItem(
            Kind: ItemKind.Image,
            Source: ItemSource.CaptureWebpage,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: bytes,
            PayloadSize: bytes.LongLength,
            SearchText: $"Webpage {url}");

        _logger.LogInformation("CaptureWebpageTask: captured {Url} → {Bytes} bytes", url, bytes.Length);
    }

    private static Task<string?> PromptForUrlAsync()
    {
        // Marshalled to the UI thread because the workflow may run on a background scheduler and
        // ShowDialog() requires the dispatcher.
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dialog = new WebpageUrlDialog();
            return dialog.ShowDialog() == true ? dialog.Url : null;
        }).Task;
    }
}
