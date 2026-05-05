using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShareQ.App.Services.Qr;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>One-shot QR-as-PNG export. Source text is config.text → bag.upload_url →
/// (last resort) the bag's PayloadBytes interpreted as UTF-8. On success writes the PNG
/// either to <c>config.path</c> (auto-save mode) or via a SaveFileDialog (interactive),
/// then stores the chosen path in <c>bag.local_path</c> so downstream "Show in Explorer"
/// or upload steps see the newly-created file.</summary>
public sealed class SaveQrCodeAsImageTask : IPipelineTask
{
    public const string TaskId = "shareq.save-qr-as-image";

    private readonly ILogger<SaveQrCodeAsImageTask> _logger;
    private readonly QrCodeService _qr;

    public SaveQrCodeAsImageTask(ILogger<SaveQrCodeAsImageTask> logger, QrCodeService qr)
    {
        _logger = logger;
        _qr = qr;
    }

    public string Id => TaskId;
    public string DisplayName => "Save QR as image";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        var text = ResolveText(context, config);
        if (string.IsNullOrEmpty(text)) { _logger.LogWarning("SaveQrCodeAsImageTask: no text resolved; skipping"); return; }

        var bytes = _qr.TryRenderPng(text);
        if (bytes is null) return;

        // Auto-save mode: a config.path is provided → write straight there. Used by silent
        // workflows (e.g. "every clipboard URL gets a QR saved into Pictures\QRs\"). When the
        // path is missing or whitespace, we fall back to interactive picker so the task is
        // also useful as a manual action.
        var configPath = (string?)config?["path"];
        var picked = !string.IsNullOrWhiteSpace(configPath)
            ? Environment.ExpandEnvironmentVariables(configPath)
            : await PickPathAsync().ConfigureAwait(false);
        if (string.IsNullOrEmpty(picked)) { _logger.LogDebug("SaveQrCodeAsImageTask: user cancelled"); return; }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(picked) ?? ".");
            await File.WriteAllBytesAsync(picked, bytes, cancellationToken).ConfigureAwait(false);
            context.Bag[PipelineBagKeys.LocalPath] = picked;
            context.Bag[PipelineBagKeys.FileExtension] = "png";
            _logger.LogDebug("SaveQrCodeAsImageTask: wrote {Bytes} bytes to {Path}", bytes.Length, picked);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SaveQrCodeAsImageTask: failed to write {Path}", picked);
        }
    }

    private static async Task<string?> PickPathAsync()
    {
        return await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var dlg = new SaveFileDialog
            {
                Title = "Save QR as PNG",
                Filter = "PNG image|*.png|All files|*.*",
                FileName = "qr.png",
                DefaultExt = ".png",
                AddExtension = true,
            };
            return dlg.ShowDialog() == true ? dlg.FileName : null;
        });
    }

    /// <summary>Resolution order: explicit config.text → bag.upload_url (the most common
    /// driver, e.g. "share a link via QR") → bag.payload_bytes interpreted as UTF-8 (lets a
    /// Copy-text-to-clipboard step feed straight into the QR generator).</summary>
    private static string? ResolveText(PipelineContext context, JsonNode? config)
    {
        if (config?["text"] is { } textNode && textNode.GetValueKind() == System.Text.Json.JsonValueKind.String)
            return textNode.GetValue<string>();
        if (context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var u) && u is string url) return url;
        if (context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var p) && p is byte[] bytes)
            return System.Text.Encoding.UTF8.GetString(bytes);
        return null;
    }
}
