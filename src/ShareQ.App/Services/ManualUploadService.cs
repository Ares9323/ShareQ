using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Domain;
using ShareQ.Core.Pipeline;
using ShareQ.Pipeline;
using ShareQ.Pipeline.Profiles;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

/// <summary>
/// Drives the upload pipeline from sources other than capture (file picker, current clipboard,
/// pasted text/URL). Reuses the same <c>UploadTask</c> + <c>NotifyToastTask</c> chain as region
/// capture but with a different default profile that skips disk-save / image-clipboard steps.
/// </summary>
public sealed class ManualUploadService
{
    private readonly PipelineExecutor _executor;
    private readonly IPipelineProfileStore _profiles;
    private readonly IServiceProvider _services;
    private readonly ILogger<ManualUploadService> _logger;

    public ManualUploadService(
        PipelineExecutor executor,
        IPipelineProfileStore profiles,
        IServiceProvider services,
        ILogger<ManualUploadService> logger)
    {
        _executor = executor;
        _profiles = profiles;
        _services = services;
        _logger = logger;
    }

    public async Task UploadFileAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) { _logger.LogWarning("UploadFile: '{Path}' not found", path); return; }
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(ext)) ext = "bin";
        var fileName = Path.GetFileName(path);

        await RunPipelineAsync(
            bytes: bytes,
            extension: ext,
            kind: KindForExtension(ext),
            source: ItemSource.Manual,
            searchText: fileName,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadCurrentClipboardAsync(CancellationToken cancellationToken)
    {
        // Snapshot clipboard contents on the UI thread; pick the richest representation available.
        var snapshot = await Application.Current.Dispatcher.InvokeAsync(ReadClipboardSnapshot).Task.ConfigureAwait(false);
        if (snapshot is null) { _logger.LogInformation("UploadFromClipboard: clipboard is empty or unsupported"); return; }

        await RunPipelineAsync(
            bytes: snapshot.Bytes,
            extension: snapshot.Extension,
            kind: snapshot.Kind,
            source: ItemSource.Manual,
            searchText: snapshot.SearchText,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task UploadTextAsync(string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(text)) return;
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        await RunPipelineAsync(
            bytes: bytes,
            extension: "txt",
            kind: ItemKind.Text,
            source: ItemSource.Manual,
            searchText: text.Length <= 200 ? text : text[..200],
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RunPipelineAsync(
        byte[] bytes, string extension, ItemKind kind, ItemSource source, string searchText, CancellationToken cancellationToken)
    {
        var profile = await _profiles.GetAsync(DefaultPipelineProfiles.ManualUploadId, cancellationToken).ConfigureAwait(false);
        if (profile is null)
        {
            _logger.LogWarning("manual-upload profile not found; aborting");
            return;
        }

        var ctx = new PipelineContext(_services);
        ctx.Bag[PipelineBagKeys.PayloadBytes] = bytes;
        ctx.Bag[PipelineBagKeys.FileExtension] = extension;
        ctx.Bag[PipelineBagKeys.NewItem] = new NewItem(
            Kind: kind,
            Source: source,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: bytes,
            PayloadSize: bytes.LongLength,
            SearchText: searchText);

        await _executor.RunAsync(profile, ctx, cancellationToken).ConfigureAwait(false);
    }

    private ClipboardSnapshot? ReadClipboardSnapshot()
    {
        try
        {
            if (System.Windows.Clipboard.ContainsImage())
            {
                var img = System.Windows.Clipboard.GetImage();
                if (img is null) return null;
                using var ms = new MemoryStream();
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(img));
                encoder.Save(ms);
                return new ClipboardSnapshot(ms.ToArray(), "png", ItemKind.Image, "Clipboard image");
            }
            if (System.Windows.Clipboard.ContainsFileDropList())
            {
                var files = System.Windows.Clipboard.GetFileDropList();
                if (files.Count == 0) return null;
                var first = files[0]!;
                if (!File.Exists(first)) return null;
                var bytes = File.ReadAllBytes(first);
                var ext = Path.GetExtension(first).TrimStart('.').ToLowerInvariant();
                if (string.IsNullOrEmpty(ext)) ext = "bin";
                return new ClipboardSnapshot(bytes, ext, KindForExtension(ext), Path.GetFileName(first));
            }
            if (System.Windows.Clipboard.ContainsText())
            {
                var text = System.Windows.Clipboard.GetText();
                if (string.IsNullOrEmpty(text)) return null;
                var bytes = System.Text.Encoding.UTF8.GetBytes(text);
                return new ClipboardSnapshot(bytes, "txt", ItemKind.Text, text.Length <= 200 ? text : text[..200]);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadClipboardSnapshot failed");
        }
        return null;
    }

    private static ItemKind KindForExtension(string ext) => ext switch
    {
        "png" or "jpg" or "jpeg" or "gif" or "bmp" or "webp" => ItemKind.Image,
        "mp4" or "mov" or "webm" or "mkv" => ItemKind.Video,
        "txt" or "md" or "log" or "csv" or "json" or "xml" or "yml" or "yaml" => ItemKind.Text,
        "html" or "htm" => ItemKind.Html,
        "rtf" => ItemKind.Rtf,
        _ => ItemKind.Files,
    };

    private sealed record ClipboardSnapshot(byte[] Bytes, string Extension, ItemKind Kind, string SearchText);
}
