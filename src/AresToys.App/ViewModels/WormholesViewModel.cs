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

    public ObservableCollection<WormholeRowViewModel> Rows { get; } = new();

    [ObservableProperty] private bool _isEmpty = true;

    public WormholesViewModel(IWormholeStore store, IWormholeWindowManager manager)
    {
        _store = store;
        _manager = manager;
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
            await _manager.CreateAsync(choice.Title, choice.Kind, choice.SourceFolder, CancellationToken.None)
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
