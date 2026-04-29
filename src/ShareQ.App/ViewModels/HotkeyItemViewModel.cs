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
    private readonly Action<string> _openInWorkflows;

    public HotkeyItemViewModel(
        string id,
        string displayName,
        bool isBuiltIn,
        HotkeyDefinition current,
        HotkeyConfigService config,
        Action refreshList,
        Action<string> openInWorkflows)
    {
        Id = id;
        DisplayName = displayName;
        IsBuiltIn = isBuiltIn;
        _config = config;
        _refreshList = refreshList;
        _openInWorkflows = openInWorkflows;
        UpdateBindingDisplay(current);
    }

    public string Id { get; }
    public string DisplayName { get; }
    public bool IsBuiltIn { get; }

    /// <summary>Reset is meaningful only for built-ins (which have a seeded default); on a custom
    /// workflow there's nothing to "reset to", so we hide the button.</summary>
    public bool CanReset => IsBuiltIn;

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

        // Clear-binding path: dialog returns success but with the (None, 0) sentinel and
        // ClearRequested set. We route that to ClearAsync so the keyboard hook unregisters and
        // the persisted Hotkey on the profile becomes null.
        if (dialog.ClearRequested)
        {
            await _config.ClearAsync(Id, CancellationToken.None).ConfigureAwait(true);
            UpdateBindingDisplay(new HotkeyDefinition(Id, HotkeyModifiers.None, 0));
        }
        else
        {
            await _config.UpdateAsync(Id, dialog.CapturedModifiers, dialog.CapturedVirtualKey, CancellationToken.None).ConfigureAwait(true);
            UpdateBindingDisplay(new HotkeyDefinition(Id, dialog.CapturedModifiers, dialog.CapturedVirtualKey));
        }
        _refreshList();
    }

    [RelayCommand]
    private async Task ResetToDefault()
    {
        await _config.ResetAsync(Id, CancellationToken.None).ConfigureAwait(true);
        var current = await _config.GetEffectiveAsync(Id, CancellationToken.None).ConfigureAwait(true);
        UpdateBindingDisplay(current);
        _refreshList();
    }

    [RelayCommand]
    private void OpenInWorkflows() => _openInWorkflows(Id);
}
