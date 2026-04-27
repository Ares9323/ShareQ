using Microsoft.Win32;

namespace ShareQ.App.Services;

public sealed class NativeClipboardHistoryProbe
{
    private const string KeyPath = @"Software\Microsoft\Clipboard";
    private const string ValueName = "EnableClipboardHistory";

    public bool? IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var value = key?.GetValue(ValueName);
        return value switch
        {
            int i => i != 0,
            _ => null
        };
    }

    public void Disable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        key.SetValue(ValueName, 0, RegistryValueKind.DWord);
    }
}
