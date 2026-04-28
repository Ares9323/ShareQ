using System.Runtime.InteropServices;
using System.Windows;
using ShareQ.App.Services.Hotkeys;
using ShareQ.Hotkeys;

namespace ShareQ.App.Windows;

public partial class HotkeyCaptureWindow : Window
{
    // Hook-tracked modifier state. We can't trust GetAsyncKeyState here because we suppress
    // (return 1 from) every keystroke while the dialog is open — the OS skips its kernel-level
    // key-state update for suppressed events, so by the time the action key arrives Windows
    // thinks Win/Shift are released. Same approach PowerToys takes: track our own state based
    // on the hook events themselves.
    private HotkeyModifiers _liveModifiers;

    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;

    public HotkeyCaptureWindow()
    {
        InitializeComponent();
        // Install a low-level keyboard hook while recording so we see (and suppress) every key,
        // including OS-reserved combos like Win+Shift+S. WPF's PreviewKeyDown alone wouldn't
        // catch those — the shell handles them at a layer below WPF's input system, so by the
        // time the WPF input thread sees Win+Shift+S the Snipping Tool overlay is already up.
        Loaded += (_, _) => InstallHook();
        Closed += (_, _) => UninstallHook();
        CancelButton.Click += (_, _) => { DialogResult = false; Close(); };
    }

    public HotkeyModifiers CapturedModifiers { get; private set; }
    public uint CapturedVirtualKey { get; private set; }

    private void InstallHook()
    {
        _hookProc = HookProc;
        _hookHandle = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null!), 0);
    }

    private void UninstallHook()
    {
        if (_hookHandle == IntPtr.Zero) return;
        _ = UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((data.flags & LLKHF_INJECTED) != 0)
            return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var msg = wParam.ToInt64();
        var isDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
        var isUp   = msg == WM_KEYUP   || msg == WM_SYSKEYUP;
        if (!isDown && !isUp) return CallNextHookEx(_hookHandle, nCode, wParam, lParam);

        var vk = data.vkCode;
        var modifierFlag = ModifierFor(vk);

        if (modifierFlag is { } flag)
        {
            // Update tracked state.
            if (isDown) _liveModifiers |= flag;
            else        _liveModifiers &= ~flag;
            Dispatcher.BeginInvoke(UpdatePreview);
            return (IntPtr)1; // suppress so Windows shell never sees the modifier sequence
        }

        if (isDown)
        {
            if (vk == VK_ESCAPE && _liveModifiers == HotkeyModifiers.None)
            {
                Dispatcher.BeginInvoke(() => { DialogResult = false; Close(); });
                return (IntPtr)1;
            }

            // Real key press → freeze modifiers + close.
            var modifiers = _liveModifiers;
            Dispatcher.BeginInvoke(() =>
            {
                CapturedModifiers = modifiers;
                CapturedVirtualKey = vk;
                DialogResult = true;
                Close();
            });
            return (IntPtr)1;
        }

        // Non-modifier keyup — swallow too so it doesn't leak after we close.
        return (IntPtr)1;
    }

    private static HotkeyModifiers? ModifierFor(uint vk) => vk switch
    {
        VK_SHIFT or VK_LSHIFT or VK_RSHIFT     => HotkeyModifiers.Shift,
        VK_CONTROL or VK_LCONTROL or VK_RCONTROL => HotkeyModifiers.Control,
        VK_MENU or VK_LMENU or VK_RMENU        => HotkeyModifiers.Alt,
        VK_LWIN or VK_RWIN                     => HotkeyModifiers.Win,
        _ => null,
    };

    private void UpdatePreview()
    {
        ComboPreview.Text = _liveModifiers == HotkeyModifiers.None
            ? "(waiting for keys…)"
            : HotkeyDisplay.Format(_liveModifiers, 0).Replace(" + VK 0x00", "…", StringComparison.Ordinal);
    }

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

    private const uint VK_ESCAPE = 0x1B;
    private const uint VK_SHIFT = 0x10;
    private const uint VK_LSHIFT = 0xA0;
    private const uint VK_RSHIFT = 0xA1;
    private const uint VK_CONTROL = 0x11;
    private const uint VK_LCONTROL = 0xA2;
    private const uint VK_RCONTROL = 0xA3;
    private const uint VK_MENU = 0x12;
    private const uint VK_LMENU = 0xA4;
    private const uint VK_RMENU = 0xA5;
    private const uint VK_LWIN = 0x5B;
    private const uint VK_RWIN = 0x5C;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);
}
