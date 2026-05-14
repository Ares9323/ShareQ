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

    /// <summary>Creates a new wormhole record, persists it, spawns the window. The record's id
    /// + geometry are returned so the caller can position the window or focus it. Currently
    /// only Data wormholes are supported in the skeleton; Portal will follow in M-Wormholes-B.</summary>
    Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, CancellationToken cancellationToken);

    /// <summary>Removes the wormhole + its on-disk artifacts (Shortcuts folder for Data, just the
    /// record for Portal) and closes the open window if any.</summary>
    Task DeleteAsync(Guid wormholeId, CancellationToken cancellationToken);

    /// <summary>Closes every live wormhole window without removing records — used by the module
    /// teardown path (Settings → toggle Wormholes OFF) and by app shutdown.</summary>
    void CloseAll();
}
