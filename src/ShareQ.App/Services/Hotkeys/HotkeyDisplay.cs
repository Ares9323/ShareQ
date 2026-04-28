using System.Text;
using ShareQ.Hotkeys;

namespace ShareQ.App.Services.Hotkeys;

/// <summary>Maps Win32 virtual-key codes + modifier flags to a user-readable string like
/// <c>"Ctrl + Alt + R"</c>. Used by the Settings → Hotkeys list and the rebind dialog.</summary>
public static class HotkeyDisplay
{
    public static string Format(HotkeyModifiers modifiers, uint virtualKey)
    {
        var sb = new StringBuilder();
        if ((modifiers & HotkeyModifiers.Control) != 0) Append(sb, "Ctrl");
        if ((modifiers & HotkeyModifiers.Alt) != 0) Append(sb, "Alt");
        if ((modifiers & HotkeyModifiers.Shift) != 0) Append(sb, "Shift");
        if ((modifiers & HotkeyModifiers.Win) != 0) Append(sb, "Win");
        Append(sb, KeyName(virtualKey));
        return sb.ToString();
    }

    private static void Append(StringBuilder sb, string token)
    {
        if (sb.Length > 0) sb.Append(" + ");
        sb.Append(token);
    }

    private static string KeyName(uint vk)
    {
        // ASCII letter / digit ranges produce sensible glyphs directly.
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();          // '0'-'9'
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();          // 'A'-'Z'
        if (vk >= 0x70 && vk <= 0x87) return $"F{vk - 0x6F}";                 // F1-F24
        return vk switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x1B => "Esc",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            0xBA => ";",
            0xBB => "=",
            0xBC => ",",
            0xBD => "-",
            0xBE => ".",
            0xBF => "/",
            0xC0 => "`",
            0xDB => "[",
            0xDC => "\\",
            0xDD => "]",
            0xDE => "'",
            _ => $"VK 0x{vk:X2}",
        };
    }
}
