using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Hotkeys;
using ShareQ.App.Windows;
using ShareQ.Hotkeys;

namespace ShareQ.App.ViewModels;

public sealed partial class HotkeyItemViewModel : ObservableObject
{
    private readonly HotkeyConfigService _config;
    private readonly Action _refreshList;

    public HotkeyItemViewModel(string id, string displayName, HotkeyDefinition current, HotkeyConfigService config, Action refreshList)
    {
        Id = id;
        DisplayName = displayName;
        _config = config;
        _refreshList = refreshList;
        UpdateBindingDisplay(current);
    }

    public string Id { get; }
    public string DisplayName { get; }

    [ObservableProperty]
    private string _bindingDisplay = string.Empty;

    private void UpdateBindingDisplay(HotkeyDefinition def)
        => BindingDisplay = HotkeyDisplay.Format(def.Modifiers, def.VirtualKey);

    [RelayCommand]
    private async Task Rebind()
    {
        var dialog = new HotkeyCaptureWindow { Owner = System.Windows.Application.Current.MainWindow };
        var ok = dialog.ShowDialog();
        if (ok != true) return;

        await _config.UpdateAsync(Id, dialog.CapturedModifiers, dialog.CapturedVirtualKey, CancellationToken.None).ConfigureAwait(true);
        UpdateBindingDisplay(new HotkeyDefinition(Id, dialog.CapturedModifiers, dialog.CapturedVirtualKey));
        _refreshList();
    }

    [RelayCommand]
    private async Task ResetToDefault()
    {
        await _config.ResetAsync(Id, CancellationToken.None).ConfigureAwait(true);
        var entry = HotkeyConfigService.Catalog.First(e => e.Id == Id);
        UpdateBindingDisplay(new HotkeyDefinition(Id, entry.DefaultModifiers, entry.DefaultVirtualKey));
        _refreshList();
    }

    [RelayCommand]
    private async Task Clear()
    {
        await _config.ClearAsync(Id, CancellationToken.None).ConfigureAwait(true);
        UpdateBindingDisplay(new HotkeyDefinition(Id, HotkeyModifiers.None, 0));
        _refreshList();
    }
}
