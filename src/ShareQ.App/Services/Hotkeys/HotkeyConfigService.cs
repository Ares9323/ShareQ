using System.Globalization;
using ShareQ.Hotkeys;
using ShareQ.Storage.Settings;

namespace ShareQ.App.Services.Hotkeys;

/// <summary>
/// Catalog of user-rebindable hotkeys. Holds the canonical id → display name + default binding,
/// reads/writes user overrides via <see cref="ISettingsStore"/>, and raises <see cref="Changed"/>
/// when a binding changes so the App can unregister/re-register live.
/// </summary>
public sealed class HotkeyConfigService
{
    public sealed record HotkeyEntry(string Id, string DisplayName, HotkeyModifiers DefaultModifiers, uint DefaultVirtualKey);

    /// <summary>Canonical list of hotkeys exposed in the Settings UI. All bindings go through the
    /// low-level keyboard hook with suppress=true, so any combo (including OS-reserved ones like
    /// Win+V or Win+Shift+S) can be rebound here.</summary>
    public static readonly IReadOnlyList<HotkeyEntry> Catalog =
    [
        new("popup",                "Show clipboard",          HotkeyModifiers.Win,                              0x56), // Win+V
        new("incognito",            "Toggle incognito",        HotkeyModifiers.Control | HotkeyModifiers.Alt,    0x49), // Ctrl+Alt+I
        new("capture-region",       "Region capture",          HotkeyModifiers.Control | HotkeyModifiers.Alt,    0x52), // Ctrl+Alt+R
        new("screen-color-picker",  "Screen color picker",     HotkeyModifiers.Control | HotkeyModifiers.Shift,  0x50), // Ctrl+Shift+P
        new("record-screen",        "Screen recording (mp4)",  HotkeyModifiers.Control | HotkeyModifiers.Alt,    0x53), // Ctrl+Alt+S
        new("record-screen-gif",    "Screen recording (gif)",  HotkeyModifiers.Control | HotkeyModifiers.Alt,    0x47), // Ctrl+Alt+G
    ];

    private readonly ISettingsStore _settings;

    public HotkeyConfigService(ISettingsStore settings)
    {
        _settings = settings;
    }

    public event EventHandler<HotkeyDefinition>? Changed;

    public async Task<HotkeyDefinition> GetEffectiveAsync(string id, CancellationToken cancellationToken)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id)
            ?? throw new ArgumentException($"unknown hotkey id '{id}'", nameof(id));
        var raw = await _settings.GetAsync(KeyFor(id), cancellationToken).ConfigureAwait(false);
        if (TryParse(raw, out var modifiers, out var vk))
            return new HotkeyDefinition(id, modifiers, vk);
        return new HotkeyDefinition(id, entry.DefaultModifiers, entry.DefaultVirtualKey);
    }

    public async Task UpdateAsync(string id, HotkeyModifiers modifiers, uint virtualKey, CancellationToken cancellationToken)
    {
        var serialized = string.Format(CultureInfo.InvariantCulture, "{0},{1}", (int)modifiers, virtualKey);
        await _settings.SetAsync(KeyFor(id), serialized, sensitive: false, cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new HotkeyDefinition(id, modifiers, virtualKey));
    }

    public async Task ResetAsync(string id, CancellationToken cancellationToken)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id)
            ?? throw new ArgumentException($"unknown hotkey id '{id}'", nameof(id));
        await _settings.RemoveAsync(KeyFor(id), cancellationToken).ConfigureAwait(false);
        Changed?.Invoke(this, new HotkeyDefinition(id, entry.DefaultModifiers, entry.DefaultVirtualKey));
    }

    private static string KeyFor(string id) => $"hotkey.{id}";

    private static bool TryParse(string? raw, out HotkeyModifiers modifiers, out uint virtualKey)
    {
        modifiers = HotkeyModifiers.None;
        virtualKey = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        var parts = raw.Split(',');
        if (parts.Length != 2) return false;
        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var modInt)) return false;
        if (!uint.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var vk)) return false;
        modifiers = (HotkeyModifiers)modInt;
        virtualKey = vk;
        return true;
    }
}
