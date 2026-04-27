namespace ShareQ.Hotkeys;

public sealed class HotkeyManager : IHotkeyManager
{
    private readonly IHotkeyRegistrar _registrar;
    private readonly Dictionary<string, int> _idByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, HotkeyDefinition> _defByWmId = [];
    private IntPtr _hwnd;
    private int _nextWmId = 0x9000;

    public HotkeyManager(IHotkeyRegistrar registrar)
    {
        _registrar = registrar;
    }

    public event EventHandler<HotkeyTriggeredEventArgs>? Triggered;

    public void Attach(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero) throw new ArgumentException("Handle cannot be zero.", nameof(windowHandle));
        if (_hwnd != IntPtr.Zero && _hwnd != windowHandle)
            throw new InvalidOperationException("HotkeyManager is already attached to a different window.");
        _hwnd = windowHandle;
    }

    public bool Register(HotkeyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!definition.IsValid()) throw new ArgumentException("Invalid hotkey definition.", nameof(definition));
        EnsureAttached();

        if (_idByName.ContainsKey(definition.Id)) return false;

        var wmId = _nextWmId++;
        var ok = _registrar.RegisterHotKey(_hwnd, wmId, definition.Modifiers, definition.VirtualKey);
        if (!ok) return false;

        _idByName[definition.Id] = wmId;
        _defByWmId[wmId] = definition;
        return true;
    }

    public bool Unregister(string id)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        if (!_idByName.TryGetValue(id, out var wmId)) return false;
        var ok = _registrar.UnregisterHotKey(_hwnd, wmId);
        _idByName.Remove(id);
        _defByWmId.Remove(wmId);
        return ok;
    }

    public bool Dispatch(int wmHotkeyId)
    {
        if (!_defByWmId.TryGetValue(wmHotkeyId, out var def)) return false;
        Triggered?.Invoke(this, new HotkeyTriggeredEventArgs(def));
        return true;
    }

    public void Dispose()
    {
        if (_hwnd == IntPtr.Zero) return;
        foreach (var (id, _) in _idByName.ToArray()) Unregister(id);
        _hwnd = IntPtr.Zero;
    }

    private void EnsureAttached()
    {
        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException("HotkeyManager.Attach(...) must be called before registering hotkeys.");
    }
}
