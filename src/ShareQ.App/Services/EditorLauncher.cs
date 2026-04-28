using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ShareQ.Editor.Persistence;
using ShareQ.Editor.Rendering;
using ShareQ.Editor.ViewModels;
using ShareQ.Editor.Views;
using ShareQ.Storage.Items;

namespace ShareQ.App.Services;

public sealed class EditorLauncher
{
    private readonly IServiceProvider _services;
    private readonly IItemStore _items;
    private readonly ColorRecentsStore _recentsStore;
    private readonly EditorDefaultsStore _defaultsStore;
    private readonly ILogger<EditorLauncher> _logger;

    public EditorLauncher(
        IServiceProvider services,
        IItemStore items,
        ColorRecentsStore recentsStore,
        EditorDefaultsStore defaultsStore,
        ILogger<EditorLauncher> logger)
    {
        _services = services;
        _items = items;
        _recentsStore = recentsStore;
        _defaultsStore = defaultsStore;
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
        vm.ResetStepCounter();
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();

        await _defaultsStore.SaveAsync(
            new EditorDefaults(vm.OutlineColor, vm.FillColor, vm.StrokeWidth, vm.CurrentTool, vm.CurrentTextStyle),
            CancellationToken.None).ConfigureAwait(false);

        if (!window.Saved) return;

        var canvasHost = (Grid)window.FindName("CanvasHost")!;
        var bytes = CanvasPngExporter.Export(canvasHost, canvasHost.ActualWidth, canvasHost.ActualHeight);
        await _items.UpdatePayloadAsync(itemId, bytes, bytes.LongLength, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("EditorLauncher: saved {Bytes} bytes back to item {Id}", bytes.Length, itemId);
    }

    /// <summary>
    /// Open the editor on raw PNG bytes (no <c>ItemStore</c> round-trip). Used by the pipeline's
    /// "Open editor before upload" step so the user can annotate the capture before subsequent
    /// steps (upload, copy-image, save) see it. Returns the edited PNG bytes on save, or null
    /// when the user cancelled — in that case the caller keeps the original bytes.
    /// </summary>
    public async Task<byte[]?> EditAsync(byte[] sourcePngBytes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourcePngBytes);
        if (sourcePngBytes.Length == 0) return null;

        var recents = await _recentsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        ColorSwatchButton.CurrentRecents = recents;
        ColorSwatchButton.OnColorPicked = c => _ = _recentsStore.PushAsync(c, CancellationToken.None);

        var defaults = await _defaultsStore.LoadAsync(cancellationToken).ConfigureAwait(false);

        // Editor is WPF — must be created and shown on the UI thread. The pipeline runs on a
        // background thread, so dispatch.
        var dispatcher = System.Windows.Application.Current.Dispatcher;
        var resultTcs = new TaskCompletionSource<byte[]?>();

        await dispatcher.InvokeAsync(() =>
        {
            var window = _services.GetRequiredService<EditorWindow>();
            var vm = (EditorViewModel)window.DataContext;
            vm.SourcePngBytes = sourcePngBytes;
            vm.EditingItemId = 0; // synthetic — there's no DB item to write back to
            vm.OutlineColor = defaults.Outline;
            vm.FillColor = defaults.Fill;
            vm.StrokeWidth = defaults.StrokeWidth;
            vm.CurrentTool = defaults.Tool;
            vm.CurrentTextStyle = defaults.TextStyle;
            vm.ResetStepCounter();
            window.Owner = System.Windows.Application.Current.MainWindow;
            window.ShowDialog();

            _ = _defaultsStore.SaveAsync(
                new EditorDefaults(vm.OutlineColor, vm.FillColor, vm.StrokeWidth, vm.CurrentTool, vm.CurrentTextStyle),
                CancellationToken.None);

            if (!window.Saved)
            {
                resultTcs.SetResult(null);
                return;
            }

            var canvasHost = (Grid)window.FindName("CanvasHost")!;
            var edited = CanvasPngExporter.Export(canvasHost, canvasHost.ActualWidth, canvasHost.ActualHeight);
            _logger.LogInformation("EditorLauncher.EditAsync: returning {Bytes} edited bytes", edited.Length);
            resultTcs.SetResult(edited);
        }).Task.ConfigureAwait(false);

        return await resultTcs.Task.ConfigureAwait(false);
    }
}
