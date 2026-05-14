using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AresToys.App.Services.Wormholes;
using AresToys.App.Views;

namespace AresToys.App.ViewModels;

/// <summary>Backs the Settings → Wormholes tab. Owns the list of <see cref="WormholeRowViewModel"/>
/// rendered as a grid, plus the "New wormhole" command that pops the same dialog the tray entry
/// uses. <see cref="ReloadAsync"/> rehydrates from <see cref="IWormholeStore"/> — called on tab
/// activation (mirrors <c>UploadersViewModel.ReloadAsync</c> / <c>HotkeysViewModel.ReloadAsync</c>
/// pattern) so the grid doesn't pay any I/O cost until the user actually navigates there.</summary>
public sealed partial class WormholesViewModel : ObservableObject
{
    private readonly IWormholeStore _store;
    private readonly IWormholeWindowManager _manager;
    private readonly WormholeDefaultsService _defaults;
    private bool _suppressDefaultsPersist;

    public ObservableCollection<WormholeRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>App-wide default icon-tile size. 0 means "use the user's Windows desktop icon
    /// size" (see <see cref="DesktopIconSize"/>). Bound to the numeric input in Settings →
    /// Wormholes; mutating the setter persists to <see cref="WormholeDefaultsService"/> which
    /// then notifies every open wormhole to refresh.</summary>
    [ObservableProperty] private int _defaultIconSizePx;

    /// <summary>App-wide default opacity expressed as a percentage 30–100 (rendering in WPF
    /// uses 0.30–1.00). Bound to the slider in Settings → Wormholes; the underlying service
    /// holds the double form.</summary>
    [ObservableProperty] private int _defaultOpacityPercent = 95;

    /// <summary>App-wide default tile padding (extra pixels around the icon inside its tile).
    /// Smaller = denser grid; larger = airier. Bound to a slider in Settings → Wormholes.</summary>
    [ObservableProperty] private int _defaultTilePaddingPx = 4;

    public WormholesViewModel(IWormholeStore store, IWormholeWindowManager manager, WormholeDefaultsService defaults)
    {
        _store = store;
        _manager = manager;
        _defaults = defaults;
        // Hydrate the VM properties from the already-loaded service (App.xaml.cs LoadAsync at
        // startup). Subsequent slider drags flow OUT through the partial-method setters below.
        _suppressDefaultsPersist = true;
        DefaultIconSizePx = _defaults.DefaultIconSizePx;
        DefaultOpacityPercent = (int)Math.Round(_defaults.DefaultOpacity * 100);
        DefaultTilePaddingPx = _defaults.DefaultTilePaddingPx;
        _suppressDefaultsPersist = false;
        // Live grid refresh: when the manager persists a record (user drag/resize on the live
        // chrome, lock toggle from chrome, hamburger rename, etc.), the matching row updates
        // its displayed fields in place. The event fires on the UI dispatcher already.
        _manager.RecordChanged += OnManagerRecordChanged;
    }

    partial void OnDefaultIconSizePxChanged(int value)
    {
        if (_suppressDefaultsPersist) return;
        _ = _defaults.SetDefaultIconSizeAsync(value, CancellationToken.None);
    }

    partial void OnDefaultOpacityPercentChanged(int value)
    {
        if (_suppressDefaultsPersist) return;
        _ = _defaults.SetDefaultOpacityAsync(value / 100.0, CancellationToken.None);
    }

    partial void OnDefaultTilePaddingPxChanged(int value)
    {
        if (_suppressDefaultsPersist) return;
        _ = _defaults.SetDefaultTilePaddingAsync(value, CancellationToken.None);
    }

    private void OnManagerRecordChanged(object? sender, Guid id)
    {
        var row = Rows.FirstOrDefault(r => r.Id == id);
        row?.RefreshDisplay();
    }

    /// <summary>Pull the latest snapshot from the store and rebuild the rows. Idempotent —
    /// safe to call on every tab activation. Doesn't subscribe to store change events for v1
    /// (drag-induced LocationChanged would otherwise spam the grid with rebuilds); the user
    /// can re-click the sidebar entry to refresh after manipulating wormholes from chrome.</summary>
    public async Task ReloadAsync()
    {
        var records = await _store.LoadAllAsync(CancellationToken.None).ConfigureAwait(true);
        Rows.Clear();
        foreach (var r in records)
            Rows.Add(new WormholeRowViewModel(r, _store, _manager, this));
        IsEmpty = Rows.Count == 0;
    }

    /// <summary>Called by a row's Delete command after the manager has removed the record.
    /// Keeps the grid in sync without a full <see cref="ReloadAsync"/> round-trip.</summary>
    internal void Remove(WormholeRowViewModel row)
    {
        Rows.Remove(row);
        IsEmpty = Rows.Count == 0;
    }

    [RelayCommand]
    private async Task NewWormholeAsync()
    {
        var dlg = new NewWormholeDialog();
        if (dlg.ShowDialog() != true || dlg.Result is null) return;
        var choice = dlg.Result;
        try
        {
            await _manager.CreateAsync(choice.Title, choice.SourceFolder, CancellationToken.None)
                .ConfigureAwait(true);
            await ReloadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show("Couldn't create the wormhole:\n" + ex.Message,
                "AresToys", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await ReloadAsync().ConfigureAwait(true);
}
