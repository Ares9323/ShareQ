using System.IO;
using System.Runtime.InteropServices;

namespace AresToys.App.Services.Wormholes;

/// <summary>Thin helper for materialising Windows <c>.lnk</c> and <c>.url</c> shortcut files
/// from a dropped target path or URL. Used by <c>DataDropPolicy</c> (drop of an Explorer item
/// onto a Data wormhole creates a <c>.lnk</c> in <c>Shortcuts\{wormholeId}\</c> pointing to the
/// original — the original file is never moved or copied).
///
/// .lnk creation goes through <c>WScript.Shell</c> via COM <c>dynamic</c> dispatch: no project
/// reference to <c>IWshRuntimeLibrary</c> is needed, and the COM ProgID ships with every
/// Windows install since XP. .url files are plain text in the standard INI shape so no COM is
/// involved.</summary>
public static class ShortcutFactory
{
    /// <summary>Create a .lnk in <paramref name="lnkPath"/> pointing at <paramref name="targetPath"/>.
    /// Working directory defaults to the target's parent folder (matches the behaviour of
    /// Explorer's "Create shortcut" verb). Existing file at <paramref name="lnkPath"/> is
    /// overwritten — callers handle name collision upstream by appending " (2)" etc.</summary>
    public static void CreateLnk(string lnkPath, string targetPath, string? arguments = null)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM ProgID not available on this system");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("WScript.Shell instantiation returned null");
        try
        {
            dynamic dShell = shell;
            dynamic shortcut = dShell.CreateShortcut(lnkPath);
            try
            {
                shortcut.TargetPath = targetPath;
                if (!string.IsNullOrEmpty(arguments)) shortcut.Arguments = arguments;
                // Working directory: target's parent if it's a file, the folder itself if it's
                // a folder. Falls back to empty string if neither exists (rare — caller usually
                // validates the path before reaching us).
                if (Directory.Exists(targetPath))
                    shortcut.WorkingDirectory = targetPath;
                else if (File.Exists(targetPath))
                    shortcut.WorkingDirectory = Path.GetDirectoryName(targetPath) ?? string.Empty;
                shortcut.Save();
            }
            finally
            {
                Marshal.FinalReleaseComObject(shortcut);
            }
        }
        finally
        {
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Write a .url internet-shortcut file. Format is the standard Windows INI block
    /// understood by Explorer and every browser. CRLF line endings on purpose — Explorer
    /// tolerates LF but some older shell extensions don't.</summary>
    public static void CreateUrl(string urlFilePath, string targetUrl)
    {
        File.WriteAllText(urlFilePath, "[InternetShortcut]\r\nURL=" + targetUrl + "\r\n");
    }

    /// <summary>Pick a non-colliding filename inside <paramref name="folder"/> for a shortcut
    /// derived from <paramref name="sourcePath"/>. Strips illegal filename characters, defaults
    /// to .lnk extension; appends " (2)", " (3)" etc. when the candidate already exists.</summary>
    public static string SuggestUniqueShortcutPath(string folder, string sourcePath, string extension = ".lnk")
    {
        var basename = Directory.Exists(sourcePath)
            ? Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
            : Path.GetFileNameWithoutExtension(sourcePath);
        if (string.IsNullOrWhiteSpace(basename)) basename = "shortcut";

        // Strip invalid filename characters. GetInvalidFileNameChars covers the platform's full
        // set; we replace rather than drop so the visual name remains close to the original.
        foreach (var c in Path.GetInvalidFileNameChars())
            basename = basename.Replace(c, '_');

        var candidate = Path.Combine(folder, basename + extension);
        if (!File.Exists(candidate)) return candidate;

        for (var n = 2; n < 1000; n++)
        {
            candidate = Path.Combine(folder, $"{basename} ({n}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
        // 999 collisions is extreme — fall through to a GUID-suffixed name as last resort.
        return Path.Combine(folder, $"{basename}-{Guid.NewGuid():N}{extension}");
    }
}
