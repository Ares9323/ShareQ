using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Pipeline;

namespace ShareQ.Pipeline.Tasks;

public sealed class SaveToFileTask : IPipelineTask
{
    public const string TaskId = "shareq.save-to-file";
    private const string DefaultFolder = "%USERPROFILE%\\Pictures\\ShareQ";

    private readonly ILogger<SaveToFileTask> _logger;

    public SaveToFileTask(ILogger<SaveToFileTask> logger)
    {
        _logger = logger;
    }

    public string Id => TaskId;
    public string DisplayName => "Save to file";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var rawBytes) || rawBytes is not byte[] bytes)
        {
            _logger.LogWarning("SaveToFileTask: bag key '{Key}' missing or not byte[]; skipping", PipelineBagKeys.PayloadBytes);
            return;
        }

        var extension = context.Bag.TryGetValue(PipelineBagKeys.FileExtension, out var rawExt) && rawExt is string ext
            ? ext
            : "bin";

        var folderTemplate = (string?)config?["folder"] ?? DefaultFolder;
        var folder = Environment.ExpandEnvironmentVariables(folderTemplate);
        Directory.CreateDirectory(folder);

        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        var titleSlug = context.Bag.TryGetValue(PipelineBagKeys.WindowTitle, out var rawTitle) && rawTitle is string title
            ? SanitizeForFilename(title)
            : string.Empty;
        var fileName = string.IsNullOrEmpty(titleSlug)
            ? $"shareq-{stamp}.{extension.TrimStart('.')}"
            : $"shareq-{titleSlug}-{stamp}.{extension.TrimStart('.')}";
        var fullPath = Path.Combine(folder, fileName);

        await File.WriteAllBytesAsync(fullPath, bytes, cancellationToken).ConfigureAwait(false);

        context.Bag[PipelineBagKeys.LocalPath] = fullPath;
        _logger.LogDebug("SaveToFileTask: wrote {Bytes} bytes to {Path}", bytes.Length, fullPath);
    }

    private static string SanitizeForFilename(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder(title.Length);
        foreach (var c in title)
        {
            if (Array.IndexOf(invalid, c) >= 0 || c == '-' || c == ' ') sb.Append('_');
            else sb.Append(c);
        }
        var s = sb.ToString().Trim('_');
        return s.Length > 40 ? s[..40] : s;
    }
}
