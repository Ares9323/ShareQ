using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ShareQ.Storage.Settings;
using Wpf.Ui.Appearance;

namespace ShareQ.App.Services;

/// <summary>Holds the two user-tunable accent colors (background + foreground) and applies them
/// globally so every accent-aware control — Primary buttons, ToggleSwitches, our drop indicators,
/// the pinned-window border — picks them up without local plumbing. Persists hex strings via
/// <see cref="ISettingsStore"/> under <c>theme.accent_bg</c> / <c>theme.accent_fg</c>.</summary>
/// <remarks>The accent flows out through two channels:
/// <list type="bullet">
/// <item><description>WPF-UI's <see cref="ApplicationAccentColorManager"/>: this paints
/// SystemAccentColor* resources, which all WPF-UI Primary controls bind to. The fg color also
/// overrides <c>TextOnAccentFillColorPrimaryBrush</c> so button labels read correctly on top of
/// custom backgrounds.</description></item>
/// <item><description>App-level resources <c>AccentBackgroundBrush</c> / <c>AccentForegroundBrush</c>:
/// used directly in XAML wherever we want the same color but the surface isn't a Primary
/// WPF-UI control (e.g. the pinned-image border, the drop-target line in workflows).</description></item>
/// </list></remarks>
public sealed class ThemeService
{
    private const string BgKey = "theme.accent_bg";
    private const string FgKey = "theme.accent_fg";
    private const string DarkKey = "theme.accent_dark";
    private const string FgDarkKey = "theme.accent_fg_dark";
    private const string Surface1Key = "theme.surface_1";
    private const string Surface2Key = "theme.surface_2";
    private const string Surface3Key = "theme.surface_3";
    public static readonly Color DefaultBackground = (Color)ColorConverter.ConvertFromString("#751C8B")!;
    public static readonly Color DefaultForeground = Colors.White;
    /// <summary>Default for the dim subtext colour — captions, age labels, kind/source rows in
    /// the clipboard list, swatch descriptions in the theme tab. One global control over how
    /// muted the secondary text reads against the surface tones.</summary>
    public static readonly Color DefaultAccentForegroundDark = (Color)ColorConverter.ConvertFromString("#878787")!;
    /// <summary>Default for the "dark accent" — used as the canvas / inactive surface in the
    /// launcher overlay (cell background, inactive tab headers). Sits low on the value axis so
    /// the brighter accent reads on top of it without contrast issues. Picked to pair with the
    /// default purple accent so the launcher's "active vs ambient" tabs read as the same hue
    /// family out of the box.</summary>
    public static readonly Color DefaultAccentDark = (Color)ColorConverter.ConvertFromString("#371242")!;
    /// <summary>Three neutral surface colours, ordered darkest → lightest. Surface1 is for the
    /// deepest backgrounds (input backgrounds, list rows), Surface2 for the standard window
    /// chrome (popup body, launcher card), Surface3 for elevated panels (drag handles, group
    /// containers). Centralising them lets the user retune the entire greyscale palette from
    /// one place instead of editing dozens of hex literals across XAML.</summary>
    public static readonly Color DefaultSurface1 = (Color)ColorConverter.ConvertFromString("#1A1A1A")!;
    public static readonly Color DefaultSurface2 = (Color)ColorConverter.ConvertFromString("#1F1F1F")!;
    public static readonly Color DefaultSurface3 = (Color)ColorConverter.ConvertFromString("#2D2D2D")!;

    private readonly ISettingsStore _settings;

    private Color _bg = DefaultBackground;
    private Color _fg = DefaultForeground;
    private Color _dark = DefaultAccentDark;
    private Color _fgDark = DefaultAccentForegroundDark;
    private Color _surface1 = DefaultSurface1;
    private Color _surface2 = DefaultSurface2;
    private Color _surface3 = DefaultSurface3;

    public ThemeService(ISettingsStore settings) => _settings = settings;

    public Color AccentBackground => _bg;
    public Color AccentForeground => _fg;
    public Color AccentBackgroundDark => _dark;
    public Color AccentForegroundDark => _fgDark;
    public Color Surface1 => _surface1;
    public Color Surface2 => _surface2;
    public Color Surface3 => _surface3;

    /// <summary>Fires after either color changes (via <see cref="SetAsync"/> or
    /// <see cref="LoadAsync"/>). The Theme view-model uses this to re-sync its hex inputs when the
    /// user clicks Reset, since Reset writes via the service rather than setting the hex strings
    /// directly.</summary>
    public event EventHandler? Changed;

    public async Task LoadAsync(CancellationToken ct = default)
    {
        var bgRaw     = await _settings.GetAsync(BgKey, ct).ConfigureAwait(false);
        var fgRaw     = await _settings.GetAsync(FgKey, ct).ConfigureAwait(false);
        var darkRaw   = await _settings.GetAsync(DarkKey, ct).ConfigureAwait(false);
        var fgDarkRaw = await _settings.GetAsync(FgDarkKey, ct).ConfigureAwait(false);
        var s1Raw     = await _settings.GetAsync(Surface1Key, ct).ConfigureAwait(false);
        var s2Raw     = await _settings.GetAsync(Surface2Key, ct).ConfigureAwait(false);
        var s3Raw     = await _settings.GetAsync(Surface3Key, ct).ConfigureAwait(false);
        _bg       = TryParseHex(bgRaw)     ?? DefaultBackground;
        _fg       = TryParseHex(fgRaw)     ?? DefaultForeground;
        _dark     = TryParseHex(darkRaw)   ?? DefaultAccentDark;
        _fgDark   = TryParseHex(fgDarkRaw) ?? DefaultAccentForegroundDark;
        _surface1 = TryParseHex(s1Raw)     ?? DefaultSurface1;
        _surface2 = TryParseHex(s2Raw)     ?? DefaultSurface2;
        _surface3 = TryParseHex(s3Raw)     ?? DefaultSurface3;
        Apply();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetAsync(Color background, Color foreground, Color dark, Color foregroundDark,
        Color surface1, Color surface2, Color surface3, CancellationToken ct = default)
    {
        _bg = background;
        _fg = foreground;
        _dark = dark;
        _fgDark = foregroundDark;
        _surface1 = surface1;
        _surface2 = surface2;
        _surface3 = surface3;
        Apply();
        await _settings.SetAsync(BgKey,       ToHex(background),     sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(FgKey,       ToHex(foreground),     sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(DarkKey,     ToHex(dark),           sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(FgDarkKey,   ToHex(foregroundDark), sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(Surface1Key, ToHex(surface1),       sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(Surface2Key, ToHex(surface2),       sensitive: false, ct).ConfigureAwait(false);
        await _settings.SetAsync(Surface3Key, ToHex(surface3),       sensitive: false, ct).ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public Task ResetAsync(CancellationToken ct = default) =>
        SetAsync(DefaultBackground, DefaultForeground, DefaultAccentDark, DefaultAccentForegroundDark,
            DefaultSurface1, DefaultSurface2, DefaultSurface3, ct);

    /// <summary>Pushes the current colors into Application.Resources and the WPF-UI accent
    /// manager. Called on Load and on every Set; safe to call from any thread (marshals to the UI
    /// dispatcher because Application.Resources is a UI-thread object).</summary>
    /// <remarks>Why we override individual Fluent brushes instead of trusting
    /// <see cref="ApplicationAccentColorManager.Apply"/> alone: WPF-UI v4 declares its accent
    /// brushes (e.g. <c>AccentFillColorDefaultBrush</c>) via <c>StaticResource</c> bindings to the
    /// <c>SystemAccentColor*</c> color keys in its merged dictionary. Apply() updates the colors,
    /// but the brushes' <c>Color</c> property was already snapshotted at template-instantiation
    /// time — so just changing colors doesn't repaint live. Replacing the brush objects themselves
    /// at <c>Application.Resources</c> overrides the merged dictionary lookup and triggers the
    /// DynamicResource refresh on every consumer (Primary buttons, ToggleSwitch, etc.).</remarks>
    private void Apply()
    {
        var app = Application.Current;
        if (app is null) return;
        if (!app.Dispatcher.CheckAccess())
        {
            app.Dispatcher.Invoke(Apply);
            return;
        }

        var bgBrush = new SolidColorBrush(_bg); bgBrush.Freeze();
        var fgBrush = new SolidColorBrush(_fg); fgBrush.Freeze();

        // Lighter / darker shades for hover/pressed states. WPF-UI v4 uses three accent variants
        // (Primary > Secondary > Tertiary) where Secondary is slightly lighter and Tertiary
        // slightly darker than the base — same convention as the system accent. Cheap perceptual
        // approximation that works on any user-chosen accent without recomputing luminance.
        var lighterBrush = new SolidColorBrush(Lighten(_bg, 0.15)); lighterBrush.Freeze();
        var darkerBrush  = new SolidColorBrush(Lighten(_bg, -0.15)); darkerBrush.Freeze();

        // Our own keys — used directly in XAML (DynamicResource AccentBackgroundBrush). The pin
        // window border, drop indicator and Theme sample preview consume these.
        app.Resources["AccentBackgroundColor"] = _bg;
        app.Resources["AccentForegroundColor"] = _fg;
        app.Resources["AccentBackgroundBrush"] = bgBrush;
        app.Resources["AccentForegroundBrush"] = fgBrush;

        // Accent dark — separate user-tunable colour used by surfaces that aren't the primary
        // accent but still want to follow the theme: launcher cells, inactive tab headers,
        // anywhere that needs an "ambient" accent-tinted dark. Also derive a slightly-lighter
        // shade for borders so the launcher gets a coherent tone with one user setting.
        var darkBrush = new SolidColorBrush(_dark); darkBrush.Freeze();
        var darkBorderBrush = new SolidColorBrush(Lighten(_dark, 0.15)); darkBorderBrush.Freeze();
        app.Resources["AccentBackgroundDarkColor"] = _dark;
        app.Resources["AccentBackgroundDarkBrush"] = darkBrush;
        app.Resources["AccentBackgroundDarkBorderBrush"] = darkBorderBrush;

        // Three neutral surface tones — Surface1 darkest (input bg / list rows), Surface2 the
        // standard window body, Surface3 elevated panels. XAML consumes via DynamicResource so
        // the user's theme tweaks apply live everywhere without re-instantiating templates.
        var surface1Brush = new SolidColorBrush(_surface1); surface1Brush.Freeze();
        var surface2Brush = new SolidColorBrush(_surface2); surface2Brush.Freeze();
        var surface3Brush = new SolidColorBrush(_surface3); surface3Brush.Freeze();
        app.Resources["Surface1Color"] = _surface1;
        app.Resources["Surface2Color"] = _surface2;
        app.Resources["Surface3Color"] = _surface3;
        app.Resources["Surface1Brush"] = surface1Brush;
        app.Resources["Surface2Brush"] = surface2Brush;
        app.Resources["Surface3Brush"] = surface3Brush;

        // Override WPF-UI v4's chrome brushes so the dark.baml defaults (#202020 window body,
        // #2D2D2D control fills) track our surface palette. WPF-UI's FluentWindow template
        // resolves Background via {DynamicResource WindowBackground}, NOT the FluentWindow's
        // own Background property — setting it inline in XAML is silently overridden by the
        // theme dictionary. ApplicationBackgroundBrush is also pinned in case some control
        // template still binds to the legacy v3 name.
        app.Resources["WindowBackground"] = surface2Brush;
        app.Resources["ApplicationBackgroundBrush"] = surface2Brush;
        // TextBox / RichTextBox / PasswordBox surfaces — the visible default-state fill is
        // TextControlBackground; the hover / focus / disabled variants share the same row
        // semantically so they all map onto Surface3 too (otherwise the row would flash a
        // different shade on pointer-over).
        app.Resources["TextControlBackground"] = surface3Brush;
        app.Resources["TextControlBackgroundPointerOver"] = surface3Brush;
        app.Resources["TextControlBackgroundFocused"] = surface3Brush;
        app.Resources["TextControlBackgroundDisabled"] = surface3Brush;

        // Default-appearance ui:Button surfaces — anything NOT marked Appearance=Primary/Danger
        // resolves Background through ButtonBackground. ComboBox / TimePicker / DatePicker
        // pickers reuse the same key family so they all track Surface3 in sync.
        app.Resources["ButtonBackground"] = surface3Brush;
        app.Resources["ButtonBackgroundPointerOver"] = surface3Brush;
        app.Resources["ButtonBackgroundPressed"] = surface3Brush;
        app.Resources["ButtonBackgroundDisabled"] = surface3Brush;
        app.Resources["ComboBoxBackground"] = surface3Brush;
        app.Resources["ComboBoxBackgroundPointerOver"] = surface3Brush;
        app.Resources["ComboBoxBackgroundFocused"] = surface3Brush;

        // Dim subtext tone — captions, age / kind / source on clipboard rows, swatch
        // descriptions, etc. One global handle for "secondary text on dark surfaces".
        var fgDarkBrush = new SolidColorBrush(_fgDark); fgDarkBrush.Freeze();
        app.Resources["AccentForegroundDarkColor"] = _fgDark;
        app.Resources["AccentForegroundDarkBrush"] = fgDarkBrush;

        // WPF-UI v4 declares BOTH naming conventions in resources/accent.baml — the legacy
        // SystemAccentColor* family AND the Fluent-v2 AccentFillColor* family. Different
        // controls bind to different ones (Button → AccentFillColor*, ToggleSwitch internal
        // tracks → SystemAccentColor*). We override both so every accent-aware surface follows
        // the user's choice. Replacing the BRUSH objects (not just Color keys) because v4's
        // theme XAML uses StaticResource Color="{...}" inside its brush definitions — changing
        // only color keys would leave already-instantiated brushes pointing at stale colors.
        app.Resources["SystemAccentColor"] = _bg;
        app.Resources["SystemAccentColorPrimary"] = _bg;
        app.Resources["SystemAccentColorSecondary"] = Lighten(_bg, 0.15);
        app.Resources["SystemAccentColorTertiary"] = Lighten(_bg, -0.15);
        app.Resources["SystemAccentBrush"] = bgBrush;
        app.Resources["SystemAccentColorBrush"] = bgBrush;
        app.Resources["SystemAccentColorPrimaryBrush"] = bgBrush;
        app.Resources["SystemAccentColorSecondaryBrush"] = lighterBrush;
        app.Resources["SystemAccentColorTertiaryBrush"] = darkerBrush;

        // Fluent v2 base accent fill keys — these feed the per-control intermediary brushes below.
        app.Resources["AccentFillColorDefaultBrush"] = bgBrush;
        app.Resources["AccentFillColorSecondaryBrush"] = lighterBrush;
        app.Resources["AccentFillColorTertiaryBrush"] = darkerBrush;
        app.Resources["AccentFillColorDisabledBrush"] = darkerBrush;
        app.Resources["AccentFillColorSelectedTextBackgroundBrush"] = bgBrush;
        app.Resources["AccentTextFillColorPrimaryBrush"] = bgBrush;
        app.Resources["AccentTextFillColorSecondaryBrush"] = bgBrush;
        app.Resources["AccentTextFillColorTertiaryBrush"] = bgBrush;
        app.Resources["TextOnAccentFillColorPrimaryBrush"] = fgBrush;
        app.Resources["TextOnAccentFillColorSecondaryBrush"] = fgBrush;
        app.Resources["TextOnAccentFillColorDisabledBrush"] = fgBrush;
        app.Resources["TextOnAccentFillColorSelectedTextBrush"] = fgBrush;

        // Per-control intermediary brushes — extracted from WPF-UI v4 dark.baml. Templates bind
        // to THESE names, not directly to AccentFillColor*. They're declared in the theme
        // dictionary as separate brush instances pointing at AccentFillColor* via StaticResource —
        // so changing AccentFillColor* alone leaves these intermediaries stuck on the original
        // accent. Override at App.Resources level so DynamicResource consumers (the templates)
        // resolve to our brushes instead of the merged-dictionary originals.
        app.Resources["AccentButtonBackground"] = bgBrush;
        app.Resources["AccentButtonBackgroundPointerOver"] = lighterBrush;
        app.Resources["AccentButtonBackgroundPressed"] = darkerBrush;
        app.Resources["AccentButtonBorderBrushPressed"] = darkerBrush;
        app.Resources["AccentButtonForeground"] = fgBrush;
        app.Resources["AccentButtonForegroundPointerOver"] = fgBrush;
        app.Resources["AccentButtonForegroundPressed"] = fgBrush;
        app.Resources["AccentControlElevationBorderBrush"] = bgBrush;

        // ToggleSwitch ON-state track — this is what changes when a switch toggles to ON.
        app.Resources["ToggleSwitchFillOn"] = bgBrush;
        app.Resources["ToggleSwitchFillOnPointerOver"] = lighterBrush;
        app.Resources["ToggleSwitchFillOnDisabled"] = darkerBrush;

        // NavigationView selected item background — sidebar selection in MainWindow.
        app.Resources["NavigationViewItemBackgroundSelected"] = bgBrush;
        app.Resources["NavigationViewItemBackgroundSelectedLeftFluent"] = bgBrush;
        app.Resources["NavigationViewSelectionIndicatorForeground"] = bgBrush;

        // Apply through WPF-UI's manager too — repaints SystemAccentColor* in case anything else
        // reads those directly. Pure write, no template re-creation, so it doesn't undo our
        // overrides above.
        ApplicationAccentColorManager.Apply(_bg, ApplicationTheme.Dark, false);
    }

    /// <summary>Mix toward white (positive amount) or black (negative amount). Used for the
    /// Primary→Secondary→Tertiary accent ladder.</summary>
    private static Color Lighten(Color c, double amount)
    {
        var t = (byte)Math.Clamp(amount > 0 ? 255 : 0, 0, 255);
        var w = Math.Abs(amount);
        byte Mix(byte channel) => (byte)Math.Clamp(channel * (1 - w) + t * w, 0, 255);
        return Color.FromRgb(Mix(c.R), Mix(c.G), Mix(c.B));
    }

    private static Color? TryParseHex(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        try { return (Color)ColorConverter.ConvertFromString(s)!; }
        catch { return null; }
    }

    public static string ToHex(Color c)
        => $"#{c.R.ToString("X2", CultureInfo.InvariantCulture)}{c.G.ToString("X2", CultureInfo.InvariantCulture)}{c.B.ToString("X2", CultureInfo.InvariantCulture)}";
}
