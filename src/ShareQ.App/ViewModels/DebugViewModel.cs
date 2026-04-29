using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.App.Services.Logging;

namespace ShareQ.App.ViewModels;

/// <summary>Backs the Debug settings tab. Exposes the live entry collection for direct binding +
/// Clear / Copy commands. AutoScroll lets the user lock the view to the latest entry — turned
/// off automatically (in code-behind) when the user manually scrolls up so they can read older
/// lines without the list yanking back to the bottom.</summary>
public sealed partial class DebugViewModel : ObservableObject
{
    private readonly DebugLogService _service;

    public DebugViewModel(DebugLogService service)
    {
        _service = service;
    }

    public ObservableCollection<DebugLogEntry> Entries => _service.Entries;

    [ObservableProperty]
    private bool _autoScroll = true;

    [RelayCommand]
    private void Clear() => _service.Clear();

    [RelayCommand]
    private void CopyAll()
    {
        var text = _service.FormatAll();
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked by another app — silent retry isn't worth it here */ }
    }
}
