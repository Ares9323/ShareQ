using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AresToys.App.Services.Wormholes;

namespace AresToys.App.ViewModels;

/// <summary>One row in the Settings → Wormholes list. Wraps a <see cref="WormholeRecord"/>
/// with INPC properties so two-way bindings on Lock / Hidden CheckBoxes round-trip through the
/// store and the live <c>WormholeWindow</c> (via <see cref="IWormholeWindowManager.ReconcileAsync"/>).
/// Display-only geometry / kind / folder strings derive from the wrapped record.</summary>
public sealed partial class WormholeRowViewModel : ObservableObject
{
    private readonly IWormholeStore _store;
    private readonly IWormholeWindowManager _manager;
    private readonly WormholesViewModel _owner;
    private bool _suppressPersist;

    public WormholeRecord Record { get; }

    [ObservableProperty] private string _title = string.Empty;
    [ObservableProperty] private bool _isLocked;
    [ObservableProperty] private bool _isHidden;

    public WormholeRowViewModel(
        WormholeRecord record,
        IWormholeStore store,
        IWormholeWindowManager manager,
        WormholesViewModel owner)
    {
        Record = record;
        _store = store;
        _manager = manager;
        _owner = owner;
        // Initial property assignments must not echo back to the store — guard with a flag the
        // OnXxxChanged partials check before persisting.
        _suppressPersist = true;
        try
        {
            Title = record.Title;
            IsLocked = record.IsLocked;
            IsHidden = record.IsHidden;
        }
        finally { _suppressPersist = false; }
    }

    public Guid Id => Record.Id;

    /// <summary>Badge label — "Shortcuts" or "Folder portal" matching the New wormhole dialog.</summary>
    public string KindLabel => Record.Kind == WormholeKind.Portal ? "Folder portal" : "Shortcuts";

    public string PositionX => $"{Record.Geometry.X:F0}";
    public string PositionY => $"{Record.Geometry.Y:F0}";
    public string SizeW => $"{Record.Geometry.Width:F0}";
    public string SizeH => $"{Record.Geometry.Height:F0}";

    /// <summary>Tooltip shown over the Title cell — full folder path for Portal, "—" for Data
    /// (the Shortcuts folder path is implementation-internal and not useful to surface).</summary>
    public string Tooltip => Record.Kind == WormholeKind.Portal
        ? (Record.Portal?.SourcePath ?? "(no source)")
        : "Curated list of shortcuts";

    partial void OnIsLockedChanged(bool value)
    {
        if (_suppressPersist) return;
        Record.IsLocked = value;
        Persist();
    }

    partial void OnIsHiddenChanged(bool value)
    {
        if (_suppressPersist) return;
        Record.IsHidden = value;
        Persist();
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        // Lightweight rename via the InputBox-equivalent — we don't have a generic prompt dialog
        // in AresToys, so this command is wired in but currently unused. Inline rename lives in
        // the chrome's hamburger menu; the panel will get its own inline-edit gesture once the
        // list rows learn click-to-edit. Kept as a hook for that follow-up.
        await Task.CompletedTask;
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var folder = Record.Kind == WormholeKind.Portal
            ? Record.Portal?.SourcePath
            : _store.GetShortcutsDirectory(Record.Id);
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            if (Record.Kind == WormholeKind.Data) Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Couldn't open the folder:\n" + ex.Message,
                "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        var confirm = MessageBox.Show(
            Record.Kind == WormholeKind.Portal
                ? $"Delete wormhole \"{Record.Title}\"?\n\nThe source folder on disk is NOT touched."
                : $"Delete wormhole \"{Record.Title}\"? This cannot be undone.",
            "AresToys",
            MessageBoxButton.OKCancel, MessageBoxImage.Question,
            MessageBoxResult.Cancel);
        if (confirm != MessageBoxResult.OK) return;
        await _manager.DeleteAsync(Record.Id, CancellationToken.None).ConfigureAwait(true);
        _owner.Remove(this);
    }

    private void Persist()
    {
        _ = PersistAsync();
        async Task PersistAsync()
        {
            try
            {
                await _store.SaveAsync(Record, CancellationToken.None).ConfigureAwait(true);
                await _manager.ReconcileAsync(Record, CancellationToken.None).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Couldn't apply the change:\n" + ex.Message,
                    "AresToys", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
