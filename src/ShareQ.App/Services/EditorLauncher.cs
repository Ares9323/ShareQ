using System.Globalization;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShareQ.Core.Imaging;
using ShareQ.Editor.Persistence;
using ShareQ.Editor.Rendering;
using ShareQ.Editor.ViewModels;
using ShareQ.Editor.Views;
using ShareQ.Storage.Items;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services;

public sealed class EditorLauncher
{
    private const string ImageFormatKey = "capture.image_format";
    private const string JpegQualityKey = "capture.jpeg_quality";

    private readonly IServiceProvider _services;
    private readonly IItemStore _items;
    private readonly ColorRecentsStore _recentsStore;
    private readonly EditorDefaultsStore _defaultsStore;
    private readonly ISettingsStore _settings;
    private readonly IImageEncoder _encoder;
    private readonly ILogger<EditorLauncher> _logger;

    public EditorLauncher(
        IServiceProvider services,
        IItemStore items,
        ColorRecentsStore recentsStore,
        EditorDefaultsStore defaultsStore,
        ISettingsStore settings,
        IImageEncoder encoder,
        ILogger<EditorLauncher> logger)
    {
        _services = services;
        _items = items;
        _recentsStore = recentsStore;
        _defaultsStore = defaultsStore;
        _settings = settings;
        _encoder = encoder;
        _logger = logger;
    }

    public async Task OpenAsync(long itemId, CancellationToken cancellationToken)
    {
        var record = await _items.GetByIdAsync(itemId, cancellationToken).ConfigureAwait(false);
        if (record is null) return;
        if (record.Kind is not ShareQ.Core.Domain.ItemKind.Image)
        {
            _logger.LogInformation("EditorLauncher: skipping non-image item {Id}", itemId);
            return;
        }
        // Defensive: legacy items recorded BEFORE we had ItemKind.Video are stored as Image but
        // contain mp4/gif bytes. Detect via BlobRef extension and bail out instead of crashing
        // BitmapImage decode on non-image content.
        if (!string.IsNullOrEmpty(record.BlobRef))
        {
            var ext = System.IO.Path.GetExtension(record.BlobRef).ToLowerInvariant();
            if (ext is ".mp4" or ".webm" or ".mkv" or ".gif" or ".webp" or ".mov")
            {
                _logger.LogInformation("EditorLauncher: item {Id} has video/animation extension {Ext}; skipping", itemId, ext);
                return;
            }
        }

        var recents = await _recentsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ColorSwatchButton.CurrentRecents = recents;
        ColorSwatchButton.OnColorPicked = c =>
        {
            _ = _recentsStore.PushAsync(c, CancellationToken.None);
        };

        var defaults = await _defaultsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        var window = _services.GetRequiredService<EditorWindow>();
        var vm = (EditorViewModel)window.DataContext;
        vm.SourcePngBytes = record.Payload.ToArray();
        vm.EditingItemId = itemId;
        vm.OutlineColor = defaults.Outline;
        vm.FillColor = defaults.Fill;
        vm.StrokeWidth = defaults.StrokeWidth;
        vm.CurrentTool = defaults.Tool;
        vm.CurrentTextStyle = defaults.TextStyle;
        vm.FreehandSmoothDefault = defaults.FreehandSmooth;
        vm.FreehandEndArrowDefault = defaults.FreehandEndArrow;
        vm.ResetStepCounter();
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();

        await _defaultsStore.SaveAsync(
            new EditorDefaults(vm.OutlineColor, vm.FillColor, vm.StrokeWidth, vm.CurrentTool, vm.CurrentTextStyle,
                vm.FreehandSmoothDefault, vm.FreehandEndArrowDefault),
            CancellationToken.None).ConfigureAwait(false);

        if (!window.Saved) return;

        var canvasHost = (Grid)window.FindName("CanvasHost")!;
        var (exportW, exportH) = ResolveExportPixels(canvasHost);
        var pngBytes = CanvasPngExporter.Export(canvasHost, exportW, exportH);
        var bytes = await EncodeForGlobalFormatAsync(pngBytes, cancellationToken).ConfigureAwait(false);
        await _items.UpdatePayloadAsync(itemId, bytes, bytes.LongLength, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("EditorLauncher: saved {Bytes} bytes back to item {Id}", bytes.Length, itemId);
    }

    /// <summary>
    /// Open the editor on raw PNG bytes (no <c>ItemStore</c> round-trip). Used by the pipeline's
    /// "Open editor before upload" step so the user can annotate the capture before subsequent
    /// steps (upload, copy-image, save) see it. Returns the edited PNG bytes on save, or null
    /// when the user cancelled — in that case the caller keeps the original bytes.
    /// </summary>
    public Task<byte[]?> EditAsync(byte[] sourcePngBytes, CancellationToken cancellationToken)
        => EditAsync(sourcePngBytes, fullscreen: false, defaultTool: null, cancellationToken);

    /// <summary>Same flow as the simple <see cref="EditAsync(byte[], CancellationToken)"/>,
    /// plus optional pipeline knobs: <paramref name="fullscreen"/> places the editor on the
    /// active monitor and forces fit-to-viewport, and <paramref name="defaultTool"/> preselects
    /// a specific drawing tool ("Crop", "Rectangle", …) on open — winning over whatever the
    /// user last left selected. <paramref name="defaultTool"/> = null / empty falls back to
    /// the persisted <see cref="EditorDefaults"/> ("last used" semantics).</summary>
    public async Task<byte[]?> EditAsync(byte[] sourcePngBytes, bool fullscreen, string? defaultTool, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePngBytes);
        if (sourcePngBytes.Length == 0) return null;

        var recents = await _recentsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ColorSwatchButton.CurrentRecents = recents;
        ColorSwatchButton.OnColorPicked = c => _ = _recentsStore.PushAsync(c, CancellationToken.None);

        var defaults = await _defaultsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        // Workflow override wins over the last-used persisted default. Bad / unknown enum
        // values fall through silently — the editor keeps the persisted last-used so a stale
        // workflow config doesn't lock the user out of a usable tool.
        var resolvedTool = defaults.Tool;
        if (!string.IsNullOrWhiteSpace(defaultTool)
            && Enum.TryParse<ShareQ.Editor.Tools.EditorTool>(defaultTool, ignoreCase: true, out var parsedTool))
        {
            resolvedTool = parsedTool;
        }
        _logger.LogInformation("EditorLauncher.EditAsync: opening with tool={Tool} (lastUsed={LastUsed}, override='{Override}')",
            resolvedTool, defaults.Tool, defaultTool ?? "(none)");

        // Editor is WPF — must be created and shown on the UI thread. The pipeline runs on a
        // background thread, so we marshal the show + the post-show snapshot of editor state
        // through the dispatcher; the persistence call afterwards happens off the dispatcher
        // thread (it's just SQLite IO via SettingsStore).
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        EditorDefaults? snapshot = null;
        bool wasSaved = false;
        byte[]? exportedPng = null;
        int exportW = 0, exportH = 0;

        await dispatcher.InvokeAsync(() =>
        {
            var window = _services.GetRequiredService<EditorWindow>();
            var vm = (EditorViewModel)window.DataContext;
            vm.SourcePngBytes = sourcePngBytes;
            vm.EditingItemId = 0; // synthetic — there's no DB item to write back to
            vm.OutlineColor = defaults.Outline;
            vm.FillColor = defaults.Fill;
            vm.StrokeWidth = defaults.StrokeWidth;
            vm.CurrentTool = resolvedTool;
            vm.CurrentTextStyle = defaults.TextStyle;
            vm.FreehandSmoothDefault = defaults.FreehandSmooth;
            vm.FreehandEndArrowDefault = defaults.FreehandEndArrow;
            vm.ResetStepCounter();
            window.Owner = System.Windows.Application.Current.MainWindow;
            if (fullscreen)
            {
                // Active monitor = the one currently under the cursor. Falls back to the primary
                // when the cursor sits in a multi-monitor gap (rare). EnableFullscreen positions
                // + maximises the editor and flips its initial fit pass to "fit-to-viewport".
                var monitor = ShareQ.Capture.MonitorEnumeration.GetMonitorUnderCursor();
                if (monitor is not null)
                    window.EnableFullscreen(monitor.X, monitor.Y, monitor.Width, monitor.Height);
            }
            window.ShowDialog();

            // Snapshot whatever the user left selected. Done INSIDE the dispatcher tick because
            // CurrentTool / *Color etc. are DependencyProperty-adjacent and reading them from a
            // pool thread would race the WPF binding system.
            snapshot = new EditorDefaults(vm.OutlineColor, vm.FillColor, vm.StrokeWidth, vm.CurrentTool, vm.CurrentTextStyle,
                vm.FreehandSmoothDefault, vm.FreehandEndArrowDefault);
            wasSaved = window.Saved;
            if (wasSaved)
            {
                var canvasHost = (Grid)window.FindName("CanvasHost")!;
                (exportW, exportH) = ResolveExportPixels(canvasHost);
                exportedPng = CanvasPngExporter.Export(canvasHost, exportW, exportH);
            }
        }).Task.ConfigureAwait(false);

        // Persist the editor defaults BEFORE returning so a subsequent EditAsync call (or any
        // other surface that reads via LoadAsync) sees the new value. The previous fire-and-
        // forget meant a quick re-open could read the old tool while the SQLite write was
        // still in flight.
        if (snapshot is not null)
        {
            await _defaultsStore.SaveAsync(snapshot, CancellationToken.None).ConfigureAwait(false);
            _logger.LogInformation("EditorLauncher.EditAsync: persisted tool={Tool} on close", snapshot.Tool);
        }

        if (!wasSaved || exportedPng is null) return null;
        // EditAsync feeds the pipeline (upload / save-to-file / etc.), so the bytes need to
        // match the globally-chosen format the same way capture does — otherwise a JPEG user
        // would see PNGs come out of "Open editor before upload" while every other capture
        // path produces JPEGs.
        var edited = await EncodeForGlobalFormatAsync(exportedPng, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("EditorLauncher.EditAsync: returning {Bytes} edited bytes ({W}×{H})", edited.Length, exportW, exportH);
        return edited;
    }

    /// <summary>Re-encode the editor's PNG export into the user's globally-configured image
    /// format. Mirrors <see cref="CaptureCoordinator"/>'s post-capture step so a screenshot
    /// goes through the same pipeline regardless of whether it was edited in between. PNG is
    /// the short-circuit (default) — everything else round-trips through <see cref="IImageEncoder"/>;
    /// failures fall back to the original PNG so a misconfiguration doesn't break the save flow.</summary>
    private async Task<byte[]> EncodeForGlobalFormatAsync(byte[] pngBytes, CancellationToken cancellationToken)
    {
        var rawFormat = await _settings.GetAsync(ImageFormatKey, cancellationToken).ConfigureAwait(false);
        var format = ImageFormatExtensions.TryParse(rawFormat) ?? ImageFormat.Png;
        if (format == ImageFormat.Png) return pngBytes;

        var rawQuality = await _settings.GetAsync(JpegQualityKey, cancellationToken).ConfigureAwait(false);
        var quality = int.TryParse(rawQuality, NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
            ? Math.Clamp(q, 1, 100) : 90;
        try
        {
            return _encoder.Encode(pngBytes, format, quality);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "EditorLauncher: re-encode to {Format} failed — keeping PNG", format);
            return pngBytes;
        }
    }

    /// <summary>Find the source bitmap inside the editor's <c>CanvasHost</c> Grid and return its
    /// pixel dimensions — the canonical export size. Falls back to the host's ActualWidth/Height
    /// only when the source can't be located (defensive, shouldn't happen in normal flow).</summary>
    private static (int W, int H) ResolveExportPixels(System.Windows.Controls.Grid canvasHost)
    {
        foreach (var child in canvasHost.Children)
        {
            if (child is System.Windows.Controls.Image img &&
                img.Source is System.Windows.Media.Imaging.BitmapSource src)
            {
                return (src.PixelWidth, src.PixelHeight);
            }
        }
        return ((int)Math.Round(canvasHost.ActualWidth), (int)Math.Round(canvasHost.ActualHeight));
    }
}
