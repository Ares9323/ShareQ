using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services;

namespace ShareQ.App.ViewModels;

/// <summary>Two-way wrapper around <see cref="ThemeService"/> for the Theme settings tab. Holds
/// the colors as hex strings (so the UI can use a plain TextBox + a swatch preview) and validates
/// before pushing the value back to the service. Reset goes through the service so persistence
/// stays a single owner.</summary>
public sealed partial class ThemeViewModel : ObservableObject
{
    private readonly ThemeService _theme;
    private bool _suppressApply;

    public ThemeViewModel(ThemeService theme)
    {
        _theme = theme;
        SyncFromService();
        // The service raises Changed after Reset / external change — re-pull so the inputs and
        // swatches stay in sync with the actual applied colors.
        _theme.Changed += (_, _) => SyncFromService();
    }

    [ObservableProperty]
    private string _accentBackgroundHex = "#751C8B";

    [ObservableProperty]
    private string _accentForegroundHex = "#FFFFFF";

    [ObservableProperty]
    private string _accentBackgroundDarkHex = "#371242";

    /// <summary>Live preview brushes for the swatch rectangles. Updated whenever the hex strings
    /// parse cleanly; left untouched on parse failure so the user can keep typing.</summary>
    [ObservableProperty]
    private Brush _accentBackgroundPreview = new SolidColorBrush(ThemeService.DefaultBackground);

    [ObservableProperty]
    private Brush _accentForegroundPreview = new SolidColorBrush(ThemeService.DefaultForeground);

    [ObservableProperty]
    private Brush _accentBackgroundDarkPreview = new SolidColorBrush(ThemeService.DefaultAccentDark);

    partial void OnAccentBackgroundHexChanged(string value) => TryApply();
    partial void OnAccentForegroundHexChanged(string value) => TryApply();
    partial void OnAccentBackgroundDarkHexChanged(string value) => TryApply();

    [RelayCommand]
    private async Task ResetAsync() => await _theme.ResetAsync().ConfigureAwait(true);

    private void SyncFromService()
    {
        _suppressApply = true;
        AccentBackgroundHex = ThemeService.ToHex(_theme.AccentBackground);
        AccentForegroundHex = ThemeService.ToHex(_theme.AccentForeground);
        AccentBackgroundDarkHex = ThemeService.ToHex(_theme.AccentBackgroundDark);
        AccentBackgroundPreview = Freeze(new SolidColorBrush(_theme.AccentBackground));
        AccentForegroundPreview = Freeze(new SolidColorBrush(_theme.AccentForeground));
        AccentBackgroundDarkPreview = Freeze(new SolidColorBrush(_theme.AccentBackgroundDark));
        _suppressApply = false;
    }

    private void TryApply()
    {
        if (_suppressApply) return;
        var bg = ParseOrNull(AccentBackgroundHex);
        var fg = ParseOrNull(AccentForegroundHex);
        var dark = ParseOrNull(AccentBackgroundDarkHex);
        if (bg is null || fg is null || dark is null) return;

        AccentBackgroundPreview = Freeze(new SolidColorBrush(bg.Value));
        AccentForegroundPreview = Freeze(new SolidColorBrush(fg.Value));
        AccentBackgroundDarkPreview = Freeze(new SolidColorBrush(dark.Value));

        // Persist + apply globally. Fire-and-forget: persistence is ~1ms (single SQLite row) and
        // a stray failure shouldn't block the UI; the user just sees their hex stuck and can retry.
        _ = _theme.SetAsync(bg.Value, fg.Value, dark.Value);
    }

    private static Color? ParseOrNull(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        var s = hex.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }

    private static Brush Freeze(SolidColorBrush b) { b.Freeze(); return b; }
}
