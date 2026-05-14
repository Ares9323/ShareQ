using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AresToys.App.Services.Launcher;

namespace AresToys.App.Services.Wormholes;

/// <summary>UI wrapper around a <see cref="WormholeItem"/>. Resolves the icon via
/// <see cref="IconService"/> at construction (synchronous — the icon cache absorbs the cost on
/// repeat hits) and exposes the visible label, hiding <c>.lnk</c> / <c>.url</c> extensions in
/// line with the spec §8.5 "Hide .lnk and .url extension" default. Holds the absolute path to
/// the shortcut file so the window can fire <c>ShellExecute</c> on double-click without
/// re-resolving the storage root.</summary>
public sealed partial class WormholeItemViewModel : ObservableObject
{
    public WormholeItem Item { get; }
    public string AbsoluteShortcutPath { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private BitmapSource? _icon;

    public WormholeItemViewModel(WormholeItem item, string wormholesRoot, IconService icons)
    {
        Item = item;
        AbsoluteShortcutPath = Path.Combine(wormholesRoot, item.ShortcutPath);
        DisplayName = ResolveDisplayName(item, AbsoluteShortcutPath);
        Icon = icons.GetIcon(AbsoluteShortcutPath);
    }

    /// <summary>Derive the user-facing label. Per-item <see cref="WormholeItem.DisplayName"/>
    /// override wins; otherwise we use the filename without the <c>.lnk</c> / <c>.url</c>
    /// extension (Portals does the same — keeps "Notepad" readable instead of "Notepad.lnk").</summary>
    private static string ResolveDisplayName(WormholeItem item, string absolutePath)
    {
        if (!string.IsNullOrWhiteSpace(item.DisplayName)) return item.DisplayName!;
        var name = Path.GetFileNameWithoutExtension(absolutePath);
        return string.IsNullOrWhiteSpace(name) ? Path.GetFileName(absolutePath) : name;
    }
}
