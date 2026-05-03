namespace ShareQ.App.ViewModels;

/// <summary>One curated colour preset the user can apply with a single click. Selecting a preset
/// in the Theme tab fills every hex field at once and persists via the existing TryApply path —
/// no separate plumbing. Add new presets here by appending to <see cref="All"/>; the ComboBox
/// picks them up automatically.</summary>
public sealed record ThemePreset(
    string Name,
    string AccentForegroundLightHex,
    string AccentForegroundDarkHex,
    string AccentBackgroundLightHex,
    string AccentBackgroundDarkHex,
    string AccentDangerHex,
    string Surface1Hex,
    string Surface2Hex,
    string Surface3Hex);

public static class ThemePresets
{
    /// <summary>Sentinel "no preset" entry used as the initial ComboBox selection so opening the
    /// tab doesn't snap-apply a preset over the user's current colours.</summary>
    public static readonly ThemePreset Custom = new(
        Name: "Custom (current values)",
        AccentForegroundLightHex: string.Empty,
        AccentForegroundDarkHex: string.Empty,
        AccentBackgroundLightHex: string.Empty,
        AccentBackgroundDarkHex: string.Empty,
        AccentDangerHex: string.Empty,
        Surface1Hex: string.Empty,
        Surface2Hex: string.Empty,
        Surface3Hex: string.Empty);

    public static readonly ThemePreset Default = new(
        Name: "ShareQ default",
        AccentForegroundLightHex: "#E5E6E6",
        AccentForegroundDarkHex: "#878787",
        AccentBackgroundLightHex: "#6BA780",
        AccentBackgroundDarkHex: "#314D3B",
        AccentDangerHex: "#8F2720",
        Surface1Hex: "#1A1A1A",
        Surface2Hex: "#1F1F1F",
        Surface3Hex: "#2D2D2D");

    public static readonly ThemePreset WizardBlue = new(
        Name: "Wizard blue",
        AccentForegroundLightHex: "#FFFFFF",
        AccentForegroundDarkHex: "#7592AE",
        AccentBackgroundLightHex: "#2B5278",
        AccentBackgroundDarkHex: "#1A324B",
        AccentDangerHex: "#A04040",
        Surface1Hex: "#111820",
        Surface2Hex: "#17212B",
        Surface3Hex: "#1E2C39");

    public static readonly ThemePreset SorcererPurple = new(
        Name: "Sorcerer Purple",
        AccentForegroundLightHex: "#FFFFFF",
        AccentForegroundDarkHex: "#AD75AE",
        AccentBackgroundLightHex: "#782B78",
        AccentBackgroundDarkHex: "#210E1F",
        AccentDangerHex: "#993355",
        Surface1Hex: "#20111F",
        Surface2Hex: "#2B172B",
        Surface3Hex: "#391E39");

    public static readonly ThemePreset DruidGreen = new(
        Name: "Druid Green",
        AccentForegroundLightHex: "#FFFFFF",
        AccentForegroundDarkHex: "#7FAE75",
        AccentBackgroundLightHex: "#38782B",
        AccentBackgroundDarkHex: "#0F210E",
        AccentDangerHex: "#8F2720",
        Surface1Hex: "#132011",
        Surface2Hex: "#1B2B17",
        Surface3Hex: "#23391E");

    public static readonly ThemePreset WarlockRed = new(
        Name: "Warlock Red",
        AccentForegroundLightHex: "#FFFFFF",
        AccentForegroundDarkHex: "#AE7575",
        AccentBackgroundLightHex: "#782B2B",
        AccentBackgroundDarkHex: "#210E0E",
        AccentDangerHex: "#A02727",
        Surface1Hex: "#201111",
        Surface2Hex: "#2B1717",
        Surface3Hex: "#391E1E");

    public static readonly ThemePreset ClericGold = new(
        Name: "Cleric Gold",
        AccentForegroundLightHex: "#F4F0EB",
        AccentForegroundDarkHex: "#AE9B75",
        AccentBackgroundLightHex: "#786430",
        AccentBackgroundDarkHex: "#211C0E",
        AccentDangerHex: "#952A2A",
        Surface1Hex: "#201D11",
        Surface2Hex: "#2B2417",
        Surface3Hex: "#39301E");

    public static readonly ThemePreset BardRouge = new(
        Name: "Bard Rouge",
        AccentForegroundLightHex: "#F4F0EB",
        AccentForegroundDarkHex: "#AE75A4",
        AccentBackgroundLightHex: "#782B68",
        AccentBackgroundDarkHex: "#210E1D",
        AccentDangerHex: "#952A2A",
        Surface1Hex: "#20111D",
        Surface2Hex: "#2B1728",
        Surface3Hex: "#391E34");

    public static readonly IReadOnlyList<ThemePreset> All = [Custom, Default, BardRouge, ClericGold, DruidGreen, SorcererPurple, WarlockRed, WizardBlue];
}
