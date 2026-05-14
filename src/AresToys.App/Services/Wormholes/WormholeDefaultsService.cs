using System.Globalization;
using AresToys.Storage.Settings;
using Microsoft.Extensions.Logging;

namespace AresToys.App.Services.Wormholes;

/// <summary>App-wide default for the two per-wormhole-overridable visual knobs: icon-tile
/// pixel size and window opacity. Each wormhole record can override either independently;
/// when it doesn't, the live window reads from this service. Hosted as a singleton so the
/// manager + Settings VM share the same instance and a slider drag in the Wormholes tab
/// propagates live to every open wormhole via <see cref="DefaultsChanged"/>.
///
/// Persistence is delegated to <see cref="ISettingsStore"/>. Two keys:
/// <see cref="IconSizeKey"/> (int, 0 = "use Windows desktop icon size") and
/// <see cref="OpacityKey"/> (double, clamped 0.30–1.00 — fully transparent isn't a useful
/// state and would make the wormhole un-clickable).</summary>
public sealed class WormholeDefaultsService
{
    public const string IconSizeKey    = "app.wormholes.default_icon_size_px";
    public const string OpacityKey     = "app.wormholes.default_opacity";
    public const string TilePaddingKey = "app.wormholes.default_tile_padding_px";

    private const int IconMin = 0;     // 0 has the special meaning "use DesktopIconSize.Get()"
    private const int IconMax = 256;
    private const double OpacityMin = 0.30;
    private const double OpacityMax = 1.00;
    private const double OpacityFallback = 0.95;
    private const int TilePaddingMin = 0;
    private const int TilePaddingMax = 32;
    private const int TilePaddingFallback = 4;

    private readonly ISettingsStore _store;
    private readonly ILogger<WormholeDefaultsService> _logger;
    private int _defaultIconSizePx;
    private double _defaultOpacity = OpacityFallback;
    private int _defaultTilePaddingPx = TilePaddingFallback;

    public WormholeDefaultsService(ISettingsStore store, ILogger<WormholeDefaultsService> logger)
    {
        _store = store;
        _logger = logger;
    }

    /// <summary>Effective icon-tile size to use when a record has no per-wormhole override
    /// (<see cref="WormholeRecord.IconSizePx"/> == 0). 0 still means "fall back further" — to
    /// <see cref="DesktopIconSize.Get()"/> — so the user's Windows desktop preference wins
    /// when neither the wormhole nor the app has an explicit value.</summary>
    public int DefaultIconSizePx => _defaultIconSizePx;

    /// <summary>Effective opacity used when a record has no <see cref="WormholeAppearance.OpacityOverride"/>.
    /// Pre-clamped to the legal range, never returns 0 (a fully transparent wormhole is
    /// un-clickable and would feel like a bug to the user).</summary>
    public double DefaultOpacity => _defaultOpacity;

    /// <summary>Extra pixels added around each item tile beyond the icon size — controls how
    /// dense the grid feels. 0 = icons hug each other (Portals-style tight pack); 32 = wide
    /// breathing room. Drives <see cref="WormholeItemViewModel.TileWidth"/>,
    /// <see cref="WormholeItemViewModel.TileHeight"/> and the icon Margin.</summary>
    public int DefaultTilePaddingPx => _defaultTilePaddingPx;

    /// <summary>Raised when the default icon size changed. Subscribers must re-extract icons
    /// at the new size (expensive — IShellItemImageFactory call per item).</summary>
    public event EventHandler? IconSizeChanged;

    /// <summary>Raised when the default opacity changed. Cheap subscribers — just update
    /// <c>OuterFrame.Opacity</c> on each open wormhole, no icon re-extraction. Separating
    /// this from <see cref="IconSizeChanged"/> is what keeps the opacity slider drag fluid
    /// (otherwise every value tick would rebuild every wormhole's item list).</summary>
    public event EventHandler? OpacityChanged;

    /// <summary>Raised when the default tile padding changed. Subscribers rebuild the item
    /// VMs (cheap — cached icons reused since the icon size didn't change).</summary>
    public event EventHandler? TilePaddingChanged;

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var iconRaw = await _store.GetAsync(IconSizeKey, cancellationToken).ConfigureAwait(false);
            if (int.TryParse(iconRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var icon))
                _defaultIconSizePx = Math.Clamp(icon, IconMin, IconMax);

            var opacityRaw = await _store.GetAsync(OpacityKey, cancellationToken).ConfigureAwait(false);
            if (double.TryParse(opacityRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var op))
                _defaultOpacity = Math.Clamp(op, OpacityMin, OpacityMax);

            var padRaw = await _store.GetAsync(TilePaddingKey, cancellationToken).ConfigureAwait(false);
            if (int.TryParse(padRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pad))
                _defaultTilePaddingPx = Math.Clamp(pad, TilePaddingMin, TilePaddingMax);
        }
        catch (Exception ex)
        {
            // Don't let a settings-store hiccup crash module init — defaults stay at fallback.
            _logger.LogWarning(ex, "WormholeDefaultsService load failed; using built-in defaults");
        }
    }

    public async Task SetDefaultTilePaddingAsync(int paddingPx, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(paddingPx, TilePaddingMin, TilePaddingMax);
        if (clamped == _defaultTilePaddingPx) return;
        _defaultTilePaddingPx = clamped;
        await _store.SetAsync(TilePaddingKey, clamped.ToString(CultureInfo.InvariantCulture),
            sensitive: false, cancellationToken).ConfigureAwait(true);
        TilePaddingChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetDefaultIconSizeAsync(int sizePx, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(sizePx, IconMin, IconMax);
        if (clamped == _defaultIconSizePx) return;
        _defaultIconSizePx = clamped;
        await _store.SetAsync(IconSizeKey, clamped.ToString(CultureInfo.InvariantCulture),
            sensitive: false, cancellationToken).ConfigureAwait(true);
        IconSizeChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetDefaultOpacityAsync(double opacity, CancellationToken cancellationToken)
    {
        var clamped = Math.Clamp(opacity, OpacityMin, OpacityMax);
        if (Math.Abs(clamped - _defaultOpacity) < 0.005) return;
        _defaultOpacity = clamped;
        await _store.SetAsync(OpacityKey, clamped.ToString("F2", CultureInfo.InvariantCulture),
            sensitive: false, cancellationToken).ConfigureAwait(true);
        OpacityChanged?.Invoke(this, EventArgs.Empty);
    }
}
