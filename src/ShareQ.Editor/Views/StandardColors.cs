using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

/// <summary>The "Standard colors" palette shown in <see cref="ColorPickerWindow"/> when the user
/// flips the bottom radio away from "Recent colors". Two rows of 14 swatches; first row light /
/// neutral / pastel, second row saturated primaries — mirrors ShareX's palette ordering so the
/// muscle-memory carries over for users coming from there.</summary>
internal static class StandardColors
{
    public static readonly IReadOnlyList<ShapeColor> Palette = new ShapeColor[]
    {
        // Row 1: light & neutral
        Hex(0xFFFFFF), Hex(0xC0C0C0), Hex(0x808080), Hex(0xFFB6B6),
        Hex(0xFFD7A0), Hex(0xFFFFB0), Hex(0xB0FFB0), Hex(0xB0FFFF),
        Hex(0xB0B0FF), Hex(0xE0B0FF), Hex(0xFFB0E0), Hex(0xFFC0CB),
        Hex(0xF5DEB3), Hex(0xE6E6FA),
        // Row 2: saturated
        Hex(0x000000), Hex(0x404040), Hex(0xA52A2A), Hex(0xFF0000),
        Hex(0xFFA500), Hex(0xFFFF00), Hex(0x00FF00), Hex(0x00FFFF),
        Hex(0x0000FF), Hex(0x800080), Hex(0xFF00FF), Hex(0xFF1493),
        Hex(0x008000), Hex(0x000080),
    };

    private static ShapeColor Hex(int rgb) =>
        new(255, (byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF));
}
