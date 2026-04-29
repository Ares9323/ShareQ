using System.Runtime.InteropServices;

namespace ShareQ.App.Native;

/// <summary>
/// Helpers around <c>SendInput</c> for paste / press-key tasks. The non-trivial bit is
/// <see cref="ReleaseStickyModifiers"/>: when a workflow is triggered by a hotkey like
/// <c>Win+Shift+P</c>, the user is still physically holding Win/Shift when our SendInput
/// keystrokes fire — so the OS sees <c>Win+Ctrl+V</c> (which opens the audio mixer in Windows 11)
/// instead of plain <c>Ctrl+V</c>. Injecting key-up events for any modifier currently down
/// neutralizes that race so the target window receives the intended keystroke.
/// </summary>
internal static class KeyInjector
{
    private static readonly ushort[] StickyModifierVks =
    [
        AppNativeMethods.VkLWin, AppNativeMethods.VkRWin,
        AppNativeMethods.VkLShift, AppNativeMethods.VkRShift,
        AppNativeMethods.VkLMenu, AppNativeMethods.VkRMenu,
        AppNativeMethods.VkLControl, AppNativeMethods.VkRControl,
    ];

    /// <summary>Inject key-up events for any modifier currently physically held. Does nothing if
    /// none are down. Returns the count of modifiers released so callers can size a settle delay.</summary>
    public static int ReleaseStickyModifiers()
    {
        var releases = new List<AppNativeMethods.INPUT>(StickyModifierVks.Length);
        foreach (var vk in StickyModifierVks)
        {
            if ((AppNativeMethods.GetAsyncKeyState(vk) & 0x8000) != 0)
                releases.Add(MakeKey(vk, keyUp: true));
        }
        if (releases.Count == 0) return 0;
        var arr = releases.ToArray();
        AppNativeMethods.SendInput((uint)arr.Length, arr, Marshal.SizeOf<AppNativeMethods.INPUT>());
        return releases.Count;
    }

    public static AppNativeMethods.INPUT MakeKey(ushort virtualKey, bool keyUp) => new()
    {
        type = AppNativeMethods.InputKeyboard,
        u = new AppNativeMethods.InputUnion
        {
            ki = new AppNativeMethods.KEYBDINPUT
            {
                wVk = virtualKey,
                dwFlags = keyUp ? AppNativeMethods.KeyEventfKeyUp : 0,
            },
        },
    };
}
