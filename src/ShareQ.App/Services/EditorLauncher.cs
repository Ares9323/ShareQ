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
        window.Owner = System.Windows.Application.Current.MainWindow;
        window.ShowDialog();

        await _defaultsStore.SaveAsync(
            new EditorDefaults(vm.OutlineColor, vm.FillColor, vm.StrokeWidth, vm.CurrentTool),
            CancellationToken.None).ConfigureAwait(false);

        if (!window.Saved) return;

        var canvasHost = (Grid)window.FindName("CanvasHost")!;
        var bytes = CanvasPngExporter.Export(canvasHost, canvasHost.ActualWidth, canvasHost.ActualHeight);
        await _items.UpdatePayloadAsync(itemId, bytes, bytes.LongLength, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("EditorLauncher: saved {Bytes} bytes back to item {Id}", bytes.Length, itemId);
    }
}
