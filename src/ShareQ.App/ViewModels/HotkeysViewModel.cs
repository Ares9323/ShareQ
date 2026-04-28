using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ShareQ.App.Services.Hotkeys;

namespace ShareQ.App.ViewModels;

public sealed partial class HotkeysViewModel : ObservableObject
{
    private readonly HotkeyConfigService _config;

    public HotkeysViewModel(HotkeyConfigService config)
    {
        _config = config;
        Items = [];
        _ = ReloadAsync();
    }

    public ObservableCollection<HotkeyItemViewModel> Items { get; }

    public async Task ReloadAsync()
    {
        Items.Clear();
        foreach (var entry in HotkeyConfigService.Catalog)
        {
            var current = await _config.GetEffectiveAsync(entry.Id, CancellationToken.None).ConfigureAwait(true);
            Items.Add(new HotkeyItemViewModel(entry.Id, entry.DisplayName, current, _config, RefreshNoOp));
        }
    }

    private void RefreshNoOp() { /* placeholder for future global refresh hook (e.g. clear duplicate flag) */ }
}
