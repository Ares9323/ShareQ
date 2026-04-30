using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using QRCoder;
using ShareQ.App.Views;
using ShareQ.Core.Pipeline;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Generates a QR code from <c>bag.upload_url</c> (or <c>config.text</c>) and pops up a small
/// window showing it. Typical use: drop after an Upload step so the user can scan the result on
/// a phone. Doesn't write anywhere — pure display task.
/// </summary>
public sealed class QrCodeTask : IPipelineTask
{
    public const string TaskId = "shareq.show-qr-code";

    private readonly ILogger<QrCodeTask> _logger;

    public QrCodeTask(ILogger<QrCodeTask> logger) { _logger = logger; }

    public string Id => TaskId;
    public string DisplayName => "Show QR code";
    public PipelineTaskKind Kind => PipelineTaskKind.Both;

    public Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        // Source text: explicit config wins, otherwise pick the upload URL out of the bag. No
        // text → log a warning and bail.
        var text = (string?)config?["text"]
                   ?? (context.Bag.TryGetValue(PipelineBagKeys.UploadUrl, out var raw) && raw is string u ? u : null);
        if (string.IsNullOrEmpty(text))
        {
            _logger.LogWarning("QrCodeTask: no text in config or upload_url in bag; skipping");
            return Task.CompletedTask;
        }

        BitmapSource? bitmap;
        try
        {
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            // PngByteQRCode keeps QRCoder fully managed (no System.Drawing dependency for the QR
            // generation itself); we then decode the PNG bytes into a WPF BitmapSource.
            using var png = new PngByteQRCode(data);
            var pngBytes = png.GetGraphic(pixelsPerModule: 12);
            using var ms = new MemoryStream(pngBytes);
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.StreamSource = ms;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            bitmap = bmp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QrCodeTask: failed to generate QR for {Len}-char string", text.Length);
            return Task.CompletedTask;
        }

        // Show the window on the UI thread without awaiting — workflow continues immediately.
        // No Owner: when the workflow is triggered from tray / hotkey the MainWindow is usually
        // hidden, and an Owner-bound child gets positioned relative to that hidden window
        // (typically off-screen). Independent + Topmost + CenterScreen + Activate keeps the QR
        // visible even when nothing else of ShareQ is on screen.
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var window = new QrCodeWindow(bitmap, text);
            window.Show();
            window.Activate();
        });
        return Task.CompletedTask;
    }
}
