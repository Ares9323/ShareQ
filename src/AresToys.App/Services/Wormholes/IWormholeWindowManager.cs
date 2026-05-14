namespace AresToys.App.Services.Wormholes;

/// <summary>Lifecycle of the WPF wormhole windows. Owns the mapping <c>WormholeRecord.Id →
/// live WormholeWindow</c>: hydrates one window per persisted record on startup, spawns a new
/// one when the user creates a wormhole from Settings / tray, and disposes them all on app
/// shutdown.
///
/// Hibernation / WorkerW parenting (per the spec §4.4) land in later milestones. The skeleton
/// just opens always-on-top transparent windows; the seam to swap in desktop-layer parenting
/// is a single call inside <c>SpawnWindowAsync</c> wrapped by an "is desktop layer enabled"
/// flag.</summary>
public interface IWormholeWindowManager
{
    /// <summary>Hydrates the records from <see cref="IWormholeStore"/> and spawns a window for
    /// every non-hidden one. Idempotent — safe to call multiple times but expected once at
    /// startup. If the wormholes module is disabled (<see cref="ModuleSettings.WormholesEnabled"/>
    /// false), the caller skips this entirely; the manager itself doesn't double-check the
    /// flag.</summary>
    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>Creates a new wormhole record, persists it, spawns the window. For Data the
    /// <paramref name="sourceFolder"/> parameter is ignored; for Portal it MUST point at an
    /// existing folder to mirror (throws otherwise).</summary>
    Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, CancellationToken cancellationToken);

    /// <summary>Portal-aware overload. <paramref name="sourceFolder"/> is required when
    /// <paramref name="kind"/> is <see cref="WormholeKind.Portal"/> and ignored otherwise. The
    /// folder is captured verbatim — caller validates existence via the New wormhole dialog.</summary>
    Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, string? sourceFolder, CancellationToken cancellationToken);

    /// <summary>Removes the wormhole + its on-disk artifacts (Shortcuts folder for Data, just the
    /// record for Portal) and closes the open window if any.</summary>
    Task DeleteAsync(Guid wormholeId, CancellationToken cancellationToken);

    /// <summary>Closes every live wormhole window without removing records — used by the module
    /// teardown path (Settings → toggle Wormholes OFF) and by app shutdown.</summary>
    void CloseAll();

    /// <summary>Repositions every currently-open wormhole onto the primary monitor in a cascade
    /// and activates them. User-triggered recovery path (tray menu) for when wormholes end up
    /// off-screen — monitor disconnect, weird DPI scaling, multi-monitor layout change between
    /// sessions.</summary>
    void RecenterAll();
}
