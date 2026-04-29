using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ShareQ.App.Windows;
using ShareQ.Capture;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services;

/// <summary>
/// Tray-launchable "Pin to screen" feature, mirroring ShareX. Asks the user where the image
/// should come from (screen region / clipboard / file), then opens a <see cref="PinnedImageWindow"/>
/// with the chosen content. The "from screen" path leaves the captured rectangle pinned at its
/// original on-screen coordinates so it visually replaces what was there.
/// </summary>
public sealed class PinToScreenLauncher
{
    private readonly ICaptureSource _captureSource;
    private readonly ISettingsStore _settings;
    private readonly EditorLauncher _editor;
    private readonly ILogger<PinToScreenLauncher> _logger;

    public PinToScreenLauncher(
        ICaptureSource captureSource,
        ISettingsStore settings,
        EditorLauncher editor,
        ILogger<PinToScreenLauncher> logger)
    {
        _captureSource = captureSource;
        _settings = settings;
        _editor = editor;
        _logger = logger;
    }

    /// <summary>Show the chooser and dispatch to the chosen source. UI-thread only.</summary>
    public async Task ShowAsync(CancellationToken cancellationToken)
    {
        var chooser = new PinSourceChooserWindow();
        var ok = chooser.ShowDialog();
        if (ok != true) return;

        switch (chooser.Result)
        {
            case PinSource.Screen:    await FromScreenAsync(cancellationToken).ConfigureAwait(true); break;
            case PinSource.Clipboard: await FromClipboardAsync(cancellationToken).ConfigureAwait(true); break;
            case PinSource.File:      await FromFileAsync(cancellationToken).ConfigureAwait(true); break;
        }
    }

    private async Task FromScreenAsync(CancellationToken cancellationToken)
    {
        // Use the same overlay the region-capture workflow uses. Returns null on Esc / empty drag.
        var overlay = new RegionOverlayWindow();
        var region = overlay.PickRegion();
        if (region is null || region.IsEmpty) return;

        try
        {
            var captured = await _captureSource.CaptureAsync(region, cancellationToken).ConfigureAwait(true);
            var bitmap = DecodePng(captured.PngBytes);
            if (bitmap is null) return;
            // Read sticky border BEFORE construction so the window can place itself synchronously
            // in the constructor (matches ShareX's flow — Options known up front, Location set
            // once before Show, no async race).
            var border = await PinnedImageWindow.LoadStickyBorderAsync(_settings, cancellationToken).ConfigureAwait(true);
            var w = new PinnedImageWindow(bitmap, initialScreenPos: (region.X, region.Y),
                settings: _settings, editor: _editor, initialBorderThickness: border);
            w.Show();
            w.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PinToScreenLauncher: failed to capture region");
        }
    }

    private async Task FromClipboardAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsImage()) return;
            var bmp = System.Windows.Clipboard.GetImage();
            if (bmp is null) return;
            bmp.Freeze();
            var border = await PinnedImageWindow.LoadStickyBorderAsync(_settings, cancellationToken).ConfigureAwait(true);
            var w = new PinnedImageWindow(bmp, settings: _settings, editor: _editor, initialBorderThickness: border);
            w.Show();
            w.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PinToScreenLauncher: failed to read clipboard image");
        }
    }

    private async Task FromFileAsync(CancellationToken cancellationToken)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick image to pin",
            Filter = "Image files (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var bytes = File.ReadAllBytes(dlg.FileName);
            var bitmap = DecodePng(bytes);
            if (bitmap is null) return;
            var border = await PinnedImageWindow.LoadStickyBorderAsync(_settings, cancellationToken).ConfigureAwait(true);
            var w = new PinnedImageWindow(bitmap, settings: _settings, editor: _editor, initialBorderThickness: border);
            w.Show();
            w.Activate();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PinToScreenLauncher: failed to load file {Path}", dlg.FileName);
        }
    }

    /// <summary>Decode arbitrary image bytes (PNG / JPG / BMP / GIF / TIFF — anything WIC handles).
    /// Frozen so the bitmap can be assigned across threads / shown by long-lived windows.</summary>
    private static BitmapSource? DecodePng(byte[] bytes)
    {
        if (bytes.Length == 0) return null;
        using var ms = new MemoryStream(bytes);
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = ms;
        bmp.EndInit();
        bmp.Freeze();
        return bmp;
    }
}
