using System.IO;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using ShareQ.App.Views;
using ShareQ.Core.Pipeline;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.PipelineTasks;

/// <summary>
/// Pops a small always-on-top window with the captured image pinned to the screen — useful for
/// keeping a reference visible while working in another app. Reads <c>bag.payload_bytes</c>
/// (raw image bytes from a capture step). The window is independent of the workflow lifetime:
/// once shown it stays put until the user closes it.
/// </summary>
public sealed class PinToScreenTask : IPipelineTask
{
    public const string TaskId = "shareq.pin-to-screen";

    private readonly ISettingsStore _settings;
    private readonly EditorLauncher _editor;
    private readonly ILogger<PinToScreenTask> _logger;
    private readonly ILogger<PinnedImageWindow> _windowLogger;

    public PinToScreenTask(
        ISettingsStore settings,
        EditorLauncher editor,
        ILogger<PinToScreenTask> logger,
        ILogger<PinnedImageWindow> windowLogger)
    {
        _settings = settings;
        _editor = editor;
        _logger = logger;
        _windowLogger = windowLogger;
    }

    public string Id => TaskId;
    public string DisplayName => "Pin to screen";
    public PipelineTaskKind Kind => PipelineTaskKind.PostCapture;

    public async Task ExecuteAsync(PipelineContext context, JsonNode? config, CancellationToken cancellationToken)
    {
        if (!context.Bag.TryGetValue(PipelineBagKeys.PayloadBytes, out var raw) || raw is not byte[] bytes)
        {
            _logger.LogWarning("PinToScreenTask: bag.payload_bytes missing; skipping");
            return;
        }
        BitmapSource? bitmap;
        try
        {
            using var ms = new MemoryStream(bytes);
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
            _logger.LogWarning(ex, "PinToScreenTask: failed to decode payload as image");
            return;
        }

        // Read sticky border off the UI thread first; ctor expects it as a parameter so it can
        // place the window synchronously without an async race vs. WPF's first paint.
        var border = await PinnedImageWindow.LoadStickyBorderAsync(_settings, cancellationToken).ConfigureAwait(false);

        // Did a previous step (CaptureRegionTask, monitor capture, …) record where the bitmap
        // came from? If yes, replay the same on-screen origin so the pinned window covers the
        // captured pixels in place — the ShareX-style "stay where you grabbed" behaviour.
        (int X, int Y)? screenPos = null;
        if (context.Bag.TryGetValue(PipelineBagKeys.CaptureScreenPos, out var posObj) && posObj is ValueTuple<int, int> pos)
        {
            screenPos = pos;
            _logger.LogInformation("PinToScreen: bag has screen pos ({X}, {Y}) — pin will land there", pos.Item1, pos.Item2);
        }
        else
        {
            _logger.LogInformation("PinToScreen: no screen pos in bag — falling back to active-monitor centring");
        }

        _ = Application.Current.Dispatcher.BeginInvoke(() =>
        {
            // No Owner = independent top-level window. Topmost survives the workflow's lifetime.
            // Activate so it grabs focus even when triggered from tray (MainWindow hidden).
            var w = new PinnedImageWindow(
                bitmap,
                initialScreenPos: screenPos,
                settings: _settings,
                editor: _editor,
                initialBorderThickness: border,
                logger: _windowLogger);
            w.ShowAtCapturedPixel();
        });
    }
}
