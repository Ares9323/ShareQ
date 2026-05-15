using System.Runtime.InteropServices;

namespace AresToys.Hotkeys;

/// <summary>Low-level keyboard hook (WH_KEYBOARD_LL) for intercepting key combinations the OS would
/// normally consume itself — most importantly Win+V, which Windows reserves for its native clipboard
/// history and which RegisterHotKey cannot bind. Same pattern as PowerToys KeyboardManager.</summary>
public sealed class KeyboardHook : IDisposable
{
    // Bindings keyed by id so the host can replace/remove individual hotkeys when the user
    // rebinds them in Settings. Iterating a list of (id, binding) keeps lookup O(n) but n is tiny
    // (handful of catalog hotkeys), and we win clarity over a dictionary that hides the order.
    private readonly Dictionary<string, HookBinding> _bindings = new(StringComparer.Ordinal);
    private readonly object _bindingsLock = new();
    /// <summary>Set of vkCodes currently held down (saw KEYDOWN but not yet KEYUP). Used to
    /// de-bounce Windows' auto-repeat: a held key fires WM_KEYDOWN every ~33ms, which would
    /// otherwise re-trigger the hotkey callback indefinitely.</summary>
    private readonly HashSet<uint> _heldKeys = [];
    private readonly object _heldKeysLock = new();

    /// <summary>vkCodes whose KEYDOWN we've already suppressed for a matching hotkey — their
    /// matching KEYUP must be suppressed too, otherwise keys with "on release" semantics (most
    /// notably VK_APPS = the Menu key, which opens the active window's context menu on KEYUP,
    /// not KEYDOWN) leak the post-action to the foreground app. Modifier-only keys (Alt, Win)
    /// also fire taskbar-focus / startmenu on the up edge, so the pair-up matters for any
    /// suppressed combo, not just VK_APPS.</summary>
    private readonly HashSet<uint> _suppressedKeyUps = [];
    private readonly object _suppressedKeyUpsLock = new();
    private readonly LowLevelKeyboardProc _hookProc;
    private IntPtr _hookHandle = IntPtr.Zero;

    public KeyboardHook()
    {
        _hookProc = HookProc; // keep delegate rooted so the GC doesn't move/collect it
    }

    /// <summary>Register or replace a key combination by id. <paramref name="callback"/> runs on the
    /// hook thread (the message-pump thread of whoever installed the hook) — keep it fast and
    /// dispatch heavy work elsewhere. Suppress=true makes us "win" against the foreground app and
    /// any OS reservation (Win+V etc).</summary>
    public void Register(string id, HotkeyModifiers modifiers, uint vkCode, Action callback, bool suppress = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(callback);
        lock (_bindingsLock)
        {
            _bindings[id] = new HookBinding(modifiers, vkCode, callback, suppress);
        }
    }

    /// <summary>Remove a binding by id. Returns true if it existed.</summary>
    public bool Unregister(string id)
    {
        if (string.IsNullOrEmpty(id)) return false;
        lock (_bindingsLock) { return _bindings.Remove(id); }
    }

    public void Install()
    {
        if (_hookHandle != IntPtr.Zero) return;
        // Per Win32 docs, hMod for WH_KEYBOARD_LL must be the module containing the hook proc, but
        // any non-null HMODULE in the same process actually works (the OS only checks it's loaded).
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null!), 0);
        if (_hookHandle == IntPtr.Zero)
        {
            throw new InvalidOperationException($"SetWindowsHookEx failed: {Marshal.GetLastWin32Error()}");
        }
    }

    public void Uninstall()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            _ = UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        // Skip events we (or another tool) injected via SendInput, otherwise we recurse.
        if ((data.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Reset the "held down" tracker on keyup so the next genuine press will fire again.
        // Without this, auto-repeat (Windows fires WM_KEYDOWN repeatedly while the user holds
        // the combo) would invoke the callback once per repeat — e.g. holding Ctrl+Alt+R would
        // stack endless region-capture overlays.
        var isKeyDown = wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN;
        var isKeyUp = wParam == (IntPtr)WM_KEYUP || wParam == (IntPtr)WM_SYSKEYUP;
        if (isKeyUp)
        {
            lock (_heldKeysLock) _heldKeys.Remove(data.vkCode);
            // If the matching KEYDOWN was suppressed for a hotkey match, swallow the KEYUP too.
            // VK_APPS (Menu key) is the canonical case: its "open context menu" trigger fires
            // on key release, so a KEYDOWN-only suppression leaks the menu pop to the active
            // window every time the user fires a hotkey bound to the Menu key.
            bool wasSuppressed;
            lock (_suppressedKeyUpsLock) wasSuppressed = _suppressedKeyUps.Remove(data.vkCode);
            if (wasSuppressed) return (IntPtr)1;
            // Special-case: Windows consumes WM_KEYDOWN before the low-level hook gets it for a
            // handful of system keys — only the KEYUP reaches us. PrintScreen and Pause/Break
            // are the two we care about for hotkey binding; the rest are unbindable by design
            // (Ctrl+Alt+Del, Ctrl+Shift+Esc → SAS, Win+L → secure-desktop transition — gated at
            // the kernel level by winlogon and unreachable from any usermode hook).
            if (data.vkCode != VK_SNAPSHOT && data.vkCode != VK_PAUSE)
                return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }
        else if (!isKeyDown)
        {
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        var modifiers = ReadModifiers();

        HookBinding? matched = null;
        lock (_bindingsLock)
        {
            foreach (var b in _bindings.Values)
            {
                if (b.VkCode == data.vkCode && b.Modifiers == modifiers)
                {
                    matched = b;
                    break;
                }
            }
        }

        if (matched is null) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // De-bounce auto-repeat: if the trigger key was already down (we never saw its keyup),
        // this is a Windows-driven repeat — suppress the visual side-effects but DON'T fire the
        // callback. A genuine second press only happens after the user lets go and re-presses,
        // at which point we'll have cleared _heldKeys via WM_KEYUP above.
        bool firstPress;
        lock (_heldKeysLock) firstPress = _heldKeys.Add(data.vkCode);

        if (firstPress)
        {
            // Fire the callback off the hook thread so a slow callback doesn't trip the Windows
            // hook timeout (which would cause the OS to silently uninstall us).
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try { matched.Callback(); }
                catch { /* swallow — host should log inside the callback */ }
            });
        }

        if (!matched.Suppress) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        // Track the suppressed KEYDOWN so the matching KEYUP also gets swallowed — see the
        // _suppressedKeyUps remark above for why this matters (VK_APPS on-release menu pop is
        // the headline case, modifier-only releases hit the start menu / taskbar focus too).
        lock (_suppressedKeyUpsLock) _suppressedKeyUps.Add(data.vkCode);

        // Some shortcuts (Win+Shift+S, Win+L, Win+G, …) are tracked by the Windows shell at a layer
        // that's NOT bypassed by simply returning 1 from this hook. Same fix PowerToys
        // KeyboardManager uses: inject a "dummy key" event (VK 0xFF — unmapped to any physical key)
        // which breaks the shell's tracking of the modifier+key combo. Without this, suppressing
        // Win+Shift+S still triggers the native Snipping Tool overlay.
        if ((matched.Modifiers & HotkeyModifiers.Win) != 0)
        {
            InjectDummyKey();
        }
        return (IntPtr)1;
    }

    private static void InjectDummyKey()
    {
        var inputs = new INPUT[2];
        inputs[0] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = DUMMY_KEY } } };
        inputs[1] = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = DUMMY_KEY, dwFlags = KEYEVENTF_KEYUP } } };
        _ = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static HotkeyModifiers ReadModifiers()
    {
        var m = HotkeyModifiers.None;
        if ((GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0) m |= HotkeyModifiers.Control;
        if ((GetAsyncKeyState(VK_MENU) & 0x8000) != 0) m |= HotkeyModifiers.Alt;
        if ((GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0) m |= HotkeyModifiers.Shift;
        if ((GetAsyncKeyState(VK_LWIN) & 0x8000) != 0) m |= HotkeyModifiers.Win;
        if ((GetAsyncKeyState(VK_RWIN) & 0x8000) != 0) m |= HotkeyModifiers.Win;
        return m;
    }

    public void Dispose() => Uninstall();

    private sealed record HookBinding(HotkeyModifiers Modifiers, uint VkCode, Action Callback, bool Suppress);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_KEYUP = 0x0101;
    private const int WM_SYSKEYDOWN = 0x0104;
    private const int WM_SYSKEYUP = 0x0105;
    private const uint LLKHF_INJECTED = 0x00000010;

    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12; // Alt
    private const uint VK_SNAPSHOT = 0x2C; // PrintScreen
    private const uint VK_PAUSE = 0x13;    // Pause / Break
    private const int VK_LWIN = 0x5B;
    private const int VK_RWIN = 0x5C;

    /// <summary>0xFF — virtual key code that doesn't map to any physical key. Used as a "combo
    /// breaker" inject so the Windows shell stops tracking Win+key sequences (PowerToys trick).</summary>
    private const ushort DUMMY_KEY = 0xFF;
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion U; }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KEYBDINPUT ki;
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx; public int dy;
        public uint mouseData; public uint dwFlags; public uint time; public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT { public uint uMsg; public ushort wParamL; public ushort wParamH; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);
}
