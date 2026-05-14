using System.Windows;
using Microsoft.Extensions.Logging;
using AresToys.App.Services.Launcher;
using AresToys.App.Views;

namespace AresToys.App.Services.Wormholes;

public sealed class WormholeWindowManager : IWormholeWindowManager
{
    private readonly IWormholeStore _store;
    private readonly IconService _icons;
    private readonly ILogger<WormholeWindowManager> _logger;
    private readonly Dictionary<Guid, WormholeWindow> _live = new();
    private bool _initialized;

    public WormholeWindowManager(IWormholeStore store, IconService icons, ILogger<WormholeWindowManager> logger)
    {
        _store = store;
        _icons = icons;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        if (_initialized) return;
        _initialized = true;

        var records = await _store.LoadAllAsync(cancellationToken).ConfigureAwait(true);
        _logger.LogInformation("Hydrated {Count} wormhole record(s)", records.Count);

        foreach (var record in records)
        {
            if (record.IsHidden) continue;
            SpawnWindow(record);
        }
    }

    public async Task<WormholeRecord> CreateAsync(string title, WormholeKind kind, CancellationToken cancellationToken)
    {
        if (kind == WormholeKind.Portal)
            throw new NotSupportedException("Portal wormholes ship in M-Wormholes-B; skeleton only supports Data.");

        var record = new WormholeRecord
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(title) ? "Wormhole" : title,
            Kind = kind,
            Data = new DataWormholeConfig(),
        };
        // Position the new wormhole roughly centred on the primary monitor. Multi-monitor /
        // cursor-relative placement lands with the New wormhole dialog (§8.4) which carries a
        // proper "position" combo — for the skeleton a sane default is enough.
        var screenW = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        record.Geometry.X = (screenW - record.Geometry.Width) / 2;
        record.Geometry.Y = (screenH - record.Geometry.Height) / 2;

        await _store.SaveAsync(record, cancellationToken).ConfigureAwait(true);
        SpawnWindow(record);
        return record;
    }

    public async Task DeleteAsync(Guid wormholeId, CancellationToken cancellationToken)
    {
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
        // Persist on user-driven geometry / lock / roll changes. The window calls back into the
        // store via this callback instead of holding the store reference directly — keeps the
        // window decoupled from the persistence layer and easier to unit-test later.
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
        // Window.Show on the dispatcher — InitializeAsync is awaited on the UI thread so this
        // is already on the dispatcher in normal flow, but stay defensive in case a future
        // caller hits us off-thread.
        if (Application.Current is { } app)
            app.Dispatcher.Invoke(window.Show);
        else
            window.Show();
    }
}
