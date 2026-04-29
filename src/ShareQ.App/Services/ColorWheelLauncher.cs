using Microsoft.Extensions.Logging;
using ShareQ.Editor.Model;
using ShareQ.Editor.Persistence;
using ShareQ.Editor.Views;

namespace ShareQ.App.Services;

/// <summary>Tray-launchable companion to <see cref="ScreenColorPickerService"/>: opens the full
/// HSB/RGB/CMYK colour picker dialog (the "wheel"-style — distinguished from the screen
/// "eyedropper") so the user can pick a colour numerically and have it land on the clipboard +
/// the recents list. The eyedropper exists for sampling pixels from arbitrary windows; this one
/// covers the case where you just want a colour and need to dial it in.</summary>
public sealed class ColorWheelLauncher
{
    private readonly ColorRecentsStore _recents;
    private readonly ILogger<ColorWheelLauncher> _logger;

    public ColorWheelLauncher(ColorRecentsStore recents, ILogger<ColorWheelLauncher> logger)
    {
        _recents = recents;
        _logger = logger;
    }

    public async Task ShowAsync()
    {
        var recents = await _recents.LoadAsync(CancellationToken.None).ConfigureAwait(true);
        // Hand the dialog the recents list via the static rendezvous point ColorSwatchButton
        // already uses — same way the editor populates it. Standalone use of the picker would
        // otherwise see an empty Recent palette.
        ColorSwatchButton.CurrentRecents = recents;
        var initial = recents.Count > 0 ? recents[0] : ShapeColor.Black;

        var dlg = new ColorPickerWindow(initial);
        // No Owner: tray invocation can happen with the main window hidden; setting Owner to
        // a hidden FluentWindow makes the picker invisible too. Centre on screen instead.
        dlg.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;

        if (dlg.ShowDialog() != true) return;
        var c = dlg.PickedColor;
        var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        try { System.Windows.Clipboard.SetText(hex); }
        catch { /* clipboard locked — the colour still went to recents below */ }
        _logger.LogInformation("Color wheel: picked {Hex}, copied to clipboard", hex);
        await _recents.PushAsync(c, CancellationToken.None).ConfigureAwait(false);
    }
}
