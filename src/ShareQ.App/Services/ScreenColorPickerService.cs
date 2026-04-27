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
    /// press while already picking is a no-op.</summary>
    public string? PickAtCursor()
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

            CopyHexToClipboard(hex);
            _notifier.Show("Color picked", $"{hex} copied to clipboard");
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
