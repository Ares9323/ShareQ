using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ShareQ.App.Services.Launcher;

/// <summary>Resolves a Windows shell icon for any path through <c>SHGetFileInfo</c>. Same call
/// Explorer uses, so .exe / .lnk / files / folders / virtual targets all return the right
/// icon. Results are cached by absolute path — extracting an icon involves a P/Invoke and a
/// GDI handle, not free to do per-render. Cache survives the lifetime of the launcher window.</summary>
public sealed class IconService
{
    private readonly Dictionary<string, BitmapSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public BitmapSource? GetIcon(string? rawPath) => GetIcon(rawPath, iconIndex: 0);

    /// <summary>Resolve an icon for a path with an optional index into multi-icon containers
    /// (.dll/.exe/.ico). <paramref name="iconIndex"/> > 0 forces ExtractIconEx (so the user
    /// can pick e.g. the 4th icon out of shell32.dll); 0 falls through to the default shell
    /// icon for the path (raster images and unspecified extracts). Cached by composite key
    /// so the same (path, index) pair never re-extracts.</summary>
    public BitmapSource? GetIcon(string? rawPath, int iconIndex)
    {
        if (string.IsNullOrWhiteSpace(rawPath)) return null;
        string expanded;
        try { expanded = Environment.ExpandEnvironmentVariables(rawPath).Trim(); }
        catch { return null; }
        if (string.IsNullOrEmpty(expanded)) return null;

        var cacheKey = iconIndex == 0 ? expanded : $"{expanded}|{iconIndex}";
        lock (_lock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        }

        var bmp = LoadFromImageFile(expanded)
                  ?? (iconIndex > 0 ? ExtractIconAt(expanded, iconIndex) : null)
                  ?? ExtractIcon(expanded);
        lock (_lock)
        {
            _cache[cacheKey] = bmp;
        }
        return bmp;
    }

    /// <summary>Resolve a cell icon: custom icon (if set) wins over the default shell icon for
    /// the launch path. The custom slot accepts both raster images (.png / .jpg / .bmp / .gif)
    /// and Windows icon files (.ico) — anything <see cref="LoadFromImageFile"/> or the shell
    /// can render. Falls back gracefully when the custom path is missing.</summary>
    public BitmapSource? GetIcon(string? customIconPath, string? fallbackPath, int customIconIndex = 0)
    {
        if (!string.IsNullOrWhiteSpace(customIconPath))
        {
            var custom = GetIcon(customIconPath, customIconIndex);
            if (custom is not null) return custom;
        }
        return GetIcon(fallbackPath);
    }

    /// <summary>Load a path with WPF's native imaging if it points at a recognised image
    /// extension. Returns null for non-images so the caller can fall through to shell-icon
    /// extraction. Handles .png/.jpg/.bmp/.gif and .ico (BitmapDecoder reads ICO containers).</summary>
    private static BitmapSource? LoadFromImageFile(string path)
    {
        if (!System.IO.File.Exists(path)) return null;
        var ext = System.IO.Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext)) return null;
        if (ext is not (".png" or ".jpg" or ".jpeg" or ".bmp" or ".gif" or ".ico")) return null;
        try
        {
            // OnLoad caches the bytes immediately so the file isn't kept open / locked.
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Pull the N-th icon out of a multi-icon container (.dll, .exe, .ico). Useful
    /// for shell32.dll (hundreds of icons), imageres.dll, accessibility.dll, etc. Returns
    /// null if the index is out of range or the file isn't a multi-icon container.</summary>
    private static BitmapSource? ExtractIconAt(string path, int iconIndex)
    {
        var large = IntPtr.Zero;
        var small = IntPtr.Zero;
        try
        {
            // ExtractIconEx returns the count actually written. We ask for one icon (large),
            // index = the user-supplied position. Negative indexes are rare but supported by
            // the API (resource ID lookups); we don't surface them in the UI.
            var copied = ExtractIconEx(path, iconIndex, out large, out small, 1);
            if (copied == 0 || large == IntPtr.Zero) return null;
            var src = Imaging.CreateBitmapSourceFromHIcon(large,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (large != IntPtr.Zero) DestroyIcon(large);
            if (small != IntPtr.Zero) DestroyIcon(small);
        }
    }

    private static BitmapSource? ExtractIcon(string path)
    {
        // SHGFI_USEFILEATTRIBUTES tells the shell to look up the icon based on the file
        // extension alone — fast, doesn't touch the disk, and works even for paths that don't
        // resolve (URLs, missing files). For real files/folders we drop the flag so the shell
        // can return the actual file's icon (custom .ico for .lnk shortcuts, etc).
        var attrs = SHGFI_ICON | SHGFI_LARGEICON;
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
                attrs |= SHGFI_USEFILEATTRIBUTES;
        }
        catch { attrs |= SHGFI_USEFILEATTRIBUTES; }

        var info = default(SHFILEINFO);
        var hr = SHGetFileInfo(path, FILE_ATTRIBUTE_NORMAL, ref info,
            (uint)Marshal.SizeOf<SHFILEINFO>(), attrs);
        if (hr == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;

        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon,
                System.Windows.Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();    // freeze so it can be used cross-thread + faster rendering
            return src;
        }
        catch
        {
            return null;
        }
        finally
        {
            DestroyIcon(info.hIcon);   // SHGetFileInfo gives ownership; we always release it
        }
    }

    // ── Win32 interop ──────────────────────────────────────────────────────────────

    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_LARGEICON = 0x000000000;   // 32×32 in the legacy enumeration
    private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex,
        out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);
}
