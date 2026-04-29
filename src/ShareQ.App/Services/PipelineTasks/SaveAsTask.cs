using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Opens a Save File dialog letting the user pick destination + filename, then writes
/// <c>bag.payload_bytes</c> there. After success the dialog's chosen path is stored back to
/// <c>bag.local_path</c> so subsequent steps (Show in Explorer, etc.) operate on the new file.
/// User-cancel is a no-op (workflow continues with the original local_path, if any).
/// </summary>
public sealed class SaveAsTask : IPipelineTask
{
    public const string TaskId = "shareq.save-as";

    private readonly ILogger<SaveAsTask> _logger;

    public SaveAsTask(ILogger<SaveAsTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Save image as…";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        if (!context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var rawBytes) || rawBytes is not byte[] bytes)
        {
            _logger.LogWarning("SaveAsTask: bag.payload_bytes missing; skipping");
            return;
        }
        var ext = context.Bag.TryGetValue(PipelineBagKeys.FileExtension, out var rawExt) && rawExt is string e ? e.TrimStart('.') : "png";
        // Pre-fill the dialog with whatever a save-to-file step produced earlier; otherwise just
        // a "shareq" placeholder. The user can rename freely; the dialog enforces the extension.
        var initialPath = context.Bag.TryGetValue(PipelineBagKeys.LocalPath, out var rawPath) && rawPath is string p ? p : $"shareq.{ext}";

        var picked = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save image as",
                FileName = Path.GetFileName(initialPath),
                InitialDirectory = Path.GetDirectoryName(initialPath) ?? string.Empty,
                Filter = $"{ext.ToUpperInvariant()} files (*.{ext})|*.{ext}|All files (*.*)|*.*",
                DefaultExt = ext,
                AddExtension = true,
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });

        if (string.IsNullOrEmpty(picked))
        {
            _logger.LogDebug("SaveAsTask: user cancelled");
            return;
        }
        try
        {
            await File.WriteAllBytesAsync(picked, bytes, cancellationToken).ConfigureAwait(false);
            context.Bag[PipelineBagKeys.LocalPath] = picked;
            _logger.LogDebug("SaveAsTask: wrote {Bytes} bytes to {Path}", bytes.Length, picked);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SaveAsTask: failed to write {Path}", picked);
        }
    }
}
