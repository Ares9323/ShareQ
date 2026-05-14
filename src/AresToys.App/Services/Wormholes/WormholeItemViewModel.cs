using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AresToys.App.Services.Launcher;

namespace AresToys.App.Services.Wormholes;

/// <summary>UI wrapper around a single visible entry in a wormhole — the path of a real file
/// or folder inside the watched source folder, plus its rendered icon and label. Created
/// transiently on every refresh tick of the parent <see cref="WormholeWindow"/> (or on every
/// FileSystemWatcher debounced burst) — no per-item JSON state.</summary>
public sealed partial class WormholeItemViewModel : ObservableObject
{
    public string AbsolutePath { get; }

    /// <summary>Tile icon pixel size — resolved at construction time from the wormhole's
    /// per-record zoom (which falls back to the system desktop icon size when unset). Bound
    /// directly to the Image's Width/Height in the XAML so the rendered icon is pixel-accurate
    /// for whatever size we asked <see cref="IconService.GetIconAtSize"/> to fetch — no WPF
    /// resampling between request size and render size.</summary>
    public int IconSizePx { get; }

    /// <summary>User-tunable extra space around the icon inside the tile. Smaller = denser
    /// (Portals-style), larger = airier. Controls vertical + horizontal padding identically
    /// (one knob is plenty; separate H/V would just be analysis paralysis).</summary>
    public int TilePaddingPx { get; }

    /// <summary>Container tile dimensions derived from icon size + user padding. The +8
    /// horizontal / +28 vertical baselines are the minimum needed for a 2-line label below
    /// the icon (font 11 → ~14 px per line → 28 max-height).</summary>
    public double TileWidth => IconSizePx + 8 + 2 * TilePaddingPx;
    public double TileHeight => IconSizePx + 28 + 2 * TilePaddingPx;

    /// <summary>Vertical breathing room around the Image, bound to <c>Image.Margin</c>. The
    /// 2-px floor keeps the icon from touching the tile's top edge even when the user dials
    /// padding all the way to 0.</summary>
    public System.Windows.Thickness IconMargin => new(0, 2 + TilePaddingPx, 0, 2);

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private BitmapSource? _icon;

    public WormholeItemViewModel(string absolutePath, IconService icons, int iconSizePx, int tilePaddingPx)
    {
        AbsolutePath = absolutePath;
        IconSizePx = iconSizePx;
        TilePaddingPx = tilePaddingPx;
        DisplayName = ResolveDisplayName(absolutePath);
        Icon = icons.GetIconAtSize(absolutePath, iconSizePx);
    }

    /// <summary>Strip the <c>.lnk</c> / <c>.url</c> extension from the visible label (Explorer
    /// does the same — keeps "Notepad" readable instead of "Notepad.lnk"). Folders keep their
    /// full name.</summary>
    private static string ResolveDisplayName(string absolutePath)
    {
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
