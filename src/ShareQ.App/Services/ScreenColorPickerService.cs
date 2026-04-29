using System.Windows;
using Microsoft.Extensions.Logging;
using ShareQ.App.Views;

namespace ShareQ.App.Services;

/// <summary>Triggered by a global hotkey, opens a full-screen magnifier overlay so the user can
/// pick a pixel precisely. The hex is copied to clipboard and announced via toast.</summary>
public sealed class ScreenColorPickerService
{
    private readonly IToastNotifier _notifier;
    private readonly ILogger<ScreenColorPickerService> _logger;
    private bool _busy;

    public ScreenColorPickerService(IToastNotifier notifier, ILogger<ScreenColorPickerService> logger)
    {
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>Open the overlay and wait for the user to click a pixel. Idempotent — a second hotkey
    /// press while already picking is a no-op. Auto-copies hex to clipboard + shows a toast — use
    /// this for the standalone tray / hotkey flow where there's no pipeline downstream.</summary>
    public string? PickAtCursor()
    {
        var hex = SampleAtCursor();
        if (hex is null) return null;
        CopyHexToClipboard(hex);
        _notifier.Show("Color picked", $"{hex} copied to clipboard");
        return hex;
    }

    /// <summary>Lower-level variant: opens the overlay and returns the picked hex (or null on
    /// cancel) WITHOUT touching the clipboard or showing a toast. Pipeline tasks call this so
    /// they can stash the colour in <see cref="ShareQ.Core.Pipeline.PipelineBagKeys.Color"/> and
    /// let downstream <c>shareq.copy-color-*</c> steps decide what format to emit.</summary>
    public string? SampleAtCursor()
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            string? hex = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var overlay = new ScreenColorPickerOverlay();
                if (overlay.ShowDialog() == true) hex = overlay.PickedHex;
            });
            if (hex is null) { _logger.LogInformation("ScreenColorPicker: cancelled"); return null; }
            _logger.LogInformation("ScreenColorPicker: picked {Hex}", hex);
            return hex;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ScreenColorPicker failed");
            return null;
        }
        finally { _busy = false; }
    }

    private static void CopyHexToClipboard(string hex)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            try { System.Windows.Clipboard.SetText(hex); }
            catch (System.Runtime.InteropServices.COMException) { /* clipboard occasionally locked; ignore */ }
        });
    }
}
