using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AresToys.App.Services.Launcher;

namespace AresToys.App.Services.Wormholes;

/// <summary>UI wrapper around a single visible entry in a wormhole. Two flavours:
/// <list type="bullet">
///   <item>**Data** items: <see cref="PersistedItem"/> is set; <see cref="AbsolutePath"/> is the
///         absolute path of the persisted <c>.lnk</c> / <c>.url</c> shortcut.</item>
///   <item>**Portal** items: <see cref="PersistedItem"/> is null; <see cref="AbsolutePath"/> is
///         the absolute path of the real file inside the watched folder. Created transiently
///         on every folder-watcher tick — no JSON state to keep in sync.</item>
/// </list>
/// Double-click → <see cref="AbsolutePath"/> + <c>ShellExecute</c>. Same gesture, both flavours;
/// the shell resolves <c>.lnk</c> targets transparently and opens regular files with their
/// registered handler.</summary>
public sealed partial class WormholeItemViewModel : ObservableObject
{
    /// <summary>Backing record for Data items, null for Portal items.</summary>
    public WormholeItem? PersistedItem { get; }

    public string AbsolutePath { get; }

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private BitmapSource? _icon;

    /// <summary>Data-item constructor: shortcut path is relative to <paramref name="wormholesRoot"/>.</summary>
    public WormholeItemViewModel(WormholeItem item, string wormholesRoot, IconService icons)
    {
        PersistedItem = item;
        AbsolutePath = Path.Combine(wormholesRoot, item.ShortcutPath);
        DisplayName = ResolveDisplayName(item.DisplayName, AbsolutePath);
        Icon = icons.GetIcon(AbsolutePath);
    }

    /// <summary>Portal-item constructor: <paramref name="absolutePath"/> points directly at the
    /// real file or folder inside the watched <c>sourcePath</c>. No persisted record exists.</summary>
    public WormholeItemViewModel(string absolutePath, IconService icons)
    {
        PersistedItem = null;
        AbsolutePath = absolutePath;
        DisplayName = ResolveDisplayName(null, absolutePath);
        Icon = icons.GetIcon(absolutePath);
    }

    /// <summary>Derive the user-facing label. Per-item override (Data flavour only) wins;
    /// otherwise we use the filename without the <c>.lnk</c> / <c>.url</c> extension (Portals
    /// does the same — keeps "Notepad" readable instead of "Notepad.lnk"). Folders keep their
    /// full name.</summary>
    private static string ResolveDisplayName(string? overrideName, string absolutePath)
    {
        if (!string.IsNullOrWhiteSpace(overrideName)) return overrideName!;
        var ext = Path.GetExtension(absolutePath);
        if (string.Equals(ext, ".lnk", StringComparison.OrdinalIgnoreCase)
            || string.Equals(ext, ".url", StringComparison.OrdinalIgnoreCase))
        {
            var noExt = Path.GetFileNameWithoutExtension(absolutePath);
            return string.IsNullOrWhiteSpace(noExt) ? Path.GetFileName(absolutePath) : noExt;
        }
        return Path.GetFileName(absolutePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }
}
