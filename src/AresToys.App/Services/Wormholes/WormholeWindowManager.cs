using System.Windows;
using Microsoft.Extensions.Logging;
using AresToys.App.Services.Launcher;
using AresToys.App.Views;

namespace AresToys.App.Services.Wormholes;

public sealed class WormholeWindowManager : IWormholeWindowManager
{
    private readonly IWormholeStore _store;
    private readonly IconService _icons;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<WormholeWindowManager> _logger;
    private readonly Dictionary<Guid, WormholeWindow> _live = new();
    private readonly Dictionary<Guid, FolderWatcher> _watchers = new();
    private bool _initialized;

    public WormholeWindowManager(
        IWormholeStore store,
        IconService icons,
        ILoggerFactory loggerFactory,
        ILogger<WormholeWindowManager> logger)
    {
        _store = store;
        _icons = icons;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        _initialized = true;

        // Snapshot the records into an independent list — IWormholeStore.LoadAllAsync returns a
        // ReadOnlyCollection wrapping the store's internal cache, and SpawnWindow triggers WPF's
        // initial Show() which clamps the window's position on multi-monitor / PerMonitorV2
        // setups, which fires LocationChanged → PersistChange → SaveAsync → mutation of the
        // underlying cache mid-foreach. Iterating a snapshot makes the iterator immune to that.
        var records = (await _store.LoadAllAsync(cancellationToken).ConfigureAwait(true)).ToList();
        _logger.LogInformation("Hydrated {Count} wormhole record(s)", records.Count);

        // Per-record try/catch so a broken record (e.g. Portal pointing at a deleted folder,
        // unexpected schema drift, WPF window construction throwing) doesn't kill the rest of
        // the loop. The bad record stays in JSON for next restart; the user can delete it from
        // Settings → Wormholes once that lands.
        foreach (var record in records)
        {
            if (record.IsHidden)
            {
                _logger.LogDebug("Skipping hidden wormhole {Id} ({Title})", record.Id, record.Title);
                continue;
            }
            try
            {
                SpawnWindow(record);
                _logger.LogInformation("Spawned wormhole {Id} kind={Kind} title={Title} source={Source}",
                    record.Id, record.Kind, record.Title,
                    record.Kind == WormholeKind.Portal ? record.Portal?.SourcePath : "<n/a>");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to spawn wormhole {Id} kind={Kind} title={Title}",
                    record.Id, record.Kind, record.Title);
            }
        }
    }

    public async Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, CancellationToken cancellationToken)
    {
        // Portal-with-source overload lives below; this one stays as the simple-Data path for
        // callers that don't know about source folders (e.g. older tray-menu stubs).
        return await CreateAsync(title, kind, sourceFolder: null, cancellationToken).ConfigureAwait(true);
    }

    public async Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, string? sourceFolder, CancellationToken cancellationToken)
    {
        if (kind == WormholeKind.Portal && string.IsNullOrWhiteSpace(sourceFolder))
            throw new ArgumentException("Portal wormhole requires a source folder.", nameof(sourceFolder));

        var record = new WormholeRecord
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(title) ? "Wormhole" : title,
            Kind = kind,
        };
        if (kind == WormholeKind.Data)
            record.Data = new DataWormholeConfig();
        else
            record.Portal = new PortalWormholeConfig { SourcePath = sourceFolder!.Trim() };

        // Position roughly centred on the primary monitor, then cascade-offset so multiple
        // new wormholes don't stack perfectly on top of each other (the "I made 3 portals but
        // I only see 1" report). Each attempt shifts (32, 32) px; we stop on the first slot
        // that doesn't sit within 32 px of any existing open wormhole, or after 12 attempts
        // (= ~400 px diagonal) at which point we accept overlap rather than walk off-screen.
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        var baseX = (screenW - record.Geometry.Width) / 2;
        var baseY = (screenH - record.Geometry.Height) / 2;
        const double cascadeStep = 32;
        const int maxAttempts = 12;
        var x = baseX;
        var y = baseY;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var collides = _live.Values.Any(w =>
                Math.Abs(w.Left - x) < cascadeStep && Math.Abs(w.Top - y) < cascadeStep);
            if (!collides) break;
            x = baseX + (attempt + 1) * cascadeStep;
            y = baseY + (attempt + 1) * cascadeStep;
        }
        record.Geometry.X = x;
        record.Geometry.Y = y;

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(true);
        SpawnWindow(record);
        return record;
    }

    public async Task DeleteAsync(Guid wormholeId, CancellationToken cancellationToken)
    {
        if (_watchers.TryGetValue(wormholeId, out var watcher))
        {
            _watchers.Remove(wormholeId);
            try { watcher.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "FolderWatcher dispose failed for {Id}", wormholeId); }
        }
        if (_live.TryGetValue(wormholeId, out var window))
        {
            _live.Remove(wormholeId);
            try { window.CloseFromManager(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to close wormhole window {Id}", wormholeId); }
        }
        await _store.DeleteAsync(wormholeId, cancellationToken).ConfigureAwait(true);
    }

    public void CloseAll()
    {
        foreach (var (_, watcher) in _watchers)
        {
            try { watcher.Dispose(); }
            catch (Exception ex) { _logger.LogWarning(ex, "FolderWatcher dispose failed during CloseAll"); }
        }
        _watchers.Clear();
        foreach (var (_, window) in _live)
        {
            try { window.CloseFromManager(); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to close wormhole window during CloseAll"); }
        }
        _live.Clear();
    }

    private void SpawnWindow(WormholeRecord record)
    {
        if (_live.ContainsKey(record.Id)) return;
        async void PersistChange()
        {
            try { await _store.SaveAsync(record, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Wormhole save failed for {Id}", record.Id); }
        }

        var window = new WormholeWindow(
            record,
            PersistChange,
            _icons,
            _store.WormholesRootPath,
            _store.GetShortcutsDirectory(record.Id));
        window.DeleteRequested += async (_, id) =>
        {
            try { await DeleteAsync(id, CancellationToken.None).ConfigureAwait(true); }
            catch (Exception ex) { _logger.LogWarning(ex, "Wormhole delete from menu failed for {Id}", id); }
        };
        _live[record.Id] = window;

        // Portal wiring: spin up a FolderWatcher pinned to this wormhole's source. The watcher
        // fires Changed events on the dispatcher after a 300 ms quiet period; the window just
        // re-enumerates its source folder each tick. Disposed in DeleteAsync / CloseAll.
        if (record.Kind == WormholeKind.Portal && record.Portal is { SourcePath: { Length: > 0 } sourcePath })
        {
            var watcher = new FolderWatcher(_loggerFactory.CreateLogger<FolderWatcher>());
            watcher.Changed += (_, _) =>
            {
                if (Application.Current is { } app) app.Dispatcher.BeginInvoke(window.RefreshPortalItems);
                else window.RefreshPortalItems();
            };
            watcher.FullRefreshRequested += (_, _) =>
            {
                if (Application.Current is { } app) app.Dispatcher.BeginInvoke(window.RefreshPortalItems);
                else window.RefreshPortalItems();
            };
            watcher.Start(sourcePath);
            _watchers[record.Id] = watcher;
        }

        // Show + Activate: with Topmost=True the window should already be on top of everything,
        // but on multi-monitor + PerMonitorV2 setups some users report wormholes spawning behind
        // their other always-on-top windows. Activate() forces a focus pass that bumps Z order
        // even when topmost alone wasn't enough.
        if (Application.Current is { } current)
            current.Dispatcher.Invoke(() => { window.Show(); window.Activate(); });
        else { window.Show(); window.Activate(); }

        // Diagnostic: log where the window actually ended up vs. what we asked for. On a
        // multi-monitor PerMonitorV2 setup WPF can clamp Left/Top to the nearest visible
        // monitor's work area, or shift by DPI factor — when a user reports "I only see one
        // wormhole" this log nails down whether the others are off-screen, behind something,
        // or simply landed at coordinates the user isn't looking at.
        _logger.LogInformation("Wormhole {Id} window placed at Left={Left} Top={Top} (asked X={X} Y={Y}), Width={W} Height={H}, IsVisible={Visible}, IsActive={Active}",
            record.Id, window.Left, window.Top, record.Geometry.X, record.Geometry.Y,
            window.Width, window.Height, window.IsVisible, window.IsActive);
    }

    /// <summary>Reposition every live wormhole onto the primary monitor in a cascade, then
    /// persist the new geometry. Surfaced through the tray menu so the user has a one-click
    /// recovery when wormholes end up off-screen (monitor disconnect, weird DPI scaling, layout
    /// rearrange between sessions). Each new position is also Activate()d to guarantee it ends
    /// up visible Z-order-wise.</summary>
    public void RecenterAll()
    {
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        const double cascadeStep = 32;
        var i = 0;
        foreach (var (_, window) in _live)
        {
            var width = double.IsNaN(window.Width) ? 320 : window.Width;
            var height = double.IsNaN(window.Height) ? 240 : window.Height;
            var x = Math.Max(20, (screenW - width) / 2 - 100 + i * cascadeStep);
            var y = Math.Max(20, (screenH - height) / 2 - 100 + i * cascadeStep);
            window.Left = x;
            window.Top = y;
            window.Activate();
            i++;
        }
    }
}
