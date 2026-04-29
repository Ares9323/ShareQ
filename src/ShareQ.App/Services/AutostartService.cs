using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace ShareQ.App.Services;

/// <summary>
/// Windows autostart toggle. Writes / removes a value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> — per-user, no admin needed, same
/// mechanism Task Manager → Startup apps reports. Value is the quoted absolute path of the
/// running executable so renames and cwd changes don't break it.
/// </summary>
public sealed class AutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ShareQ";

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var existing = key?.GetValue(ValueName) as string;
            if (string.IsNullOrEmpty(existing)) return false;
            // Compare the path inside the existing entry against our current exe so a stale
            // entry from a different install location reports as "off" (and Enable below
            // overwrites it).
            return string.Equals(NormalizeForCompare(existing), NormalizeForCompare(BuildEntryValue()), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (key is null) return;
        if (enabled) key.SetValue(ValueName, BuildEntryValue(), RegistryValueKind.String);
        else key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>Quoted path so spaces in "Program Files" etc. don't break the auto-launch parser.</summary>
    private static string BuildEntryValue()
    {
        var exe = Process.GetCurrentProcess().MainModule?.FileName
                  ?? throw new InvalidOperationException("Could not resolve current executable path");
        return $"\"{exe}\"";
    }

    private static string NormalizeForCompare(string raw) => raw.Trim().Trim('"');
}
