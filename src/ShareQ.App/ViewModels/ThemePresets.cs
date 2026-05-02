namespace ShareQ.App.ViewModels;

/// <summary>One curated colour preset the user can apply with a single click. Selecting a preset
/// in the Theme tab fills every hex field at once and persists via the existing TryApply path —
/// no separate plumbing. Add new presets here by appending to <see cref="All"/>; the ComboBox
/// picks them up automatically.</summary>
public sealed record ThemePreset(
    string Name,
    string AccentBackgroundHex,
    string AccentForegroundHex,
    string AccentBackgroundDarkHex,
    string AccentForegroundDarkHex,
    string Surface1Hex,
    string Surface2Hex,
    string Surface3Hex);

public static class ThemePresets
{
    /// <summary>Sentinel "no preset" entry used as the initial ComboBox selection so opening the
    /// tab doesn't snap-apply a preset over the user's current colours.</summary>
    public static readonly ThemePreset Custom = new(
        Name: "Custom (current values)",
        AccentBackgroundHex: string.Empty, AccentForegroundHex: string.Empty,
        AccentBackgroundDarkHex: string.Empty, AccentForegroundDarkHex: string.Empty,
        Surface1Hex: string.Empty, Surface2Hex: string.Empty, Surface3Hex: string.Empty);

    public static readonly ThemePreset Default = new(
        Name: "ShareQ default",
        AccentBackgroundHex: "#6BA780",
        AccentForegroundHex: "#E5E6E6",
        AccentBackgroundDarkHex: "#314D3B",
        AccentForegroundDarkHex: "#878787",
        Surface1Hex: "#1A1A1A",
        Surface2Hex: "#1F1F1F",
        Surface3Hex: "#2D2D2D");

    public static readonly ThemePreset TelegramBlue = new(
        Name: "Telegram blue",
        AccentBackgroundHex: "#2B5278",
        AccentForegroundHex: "#FFFFFF",
        AccentBackgroundDarkHex: "#0E1621",
        AccentForegroundDarkHex: "#768B9D",
        Surface1Hex: "#111820",
        Surface2Hex: "#17212B",
        Surface3Hex: "#243443");

    public static readonly ThemePreset DarkPurple = new(
        Name: "Dark purple",
        AccentBackgroundHex: "#782B78",
        AccentForegroundHex: "#FFFFFF",
        AccentBackgroundDarkHex: "#210E1F",
        AccentForegroundDarkHex: "#9C769D",
        Surface1Hex: "#20111F",
        Surface2Hex: "#2B172B",
        Surface3Hex: "#422443");

    public static readonly IReadOnlyList<ThemePreset> All = [Custom, Default, TelegramBlue, DarkPurple];
}
