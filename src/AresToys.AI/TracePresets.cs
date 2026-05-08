namespace AresToys.AI;

/// <summary>Built-in trace presets matching the Illustrator Image Trace panel verbatim
/// where parameters map cleanly to potrace. A few notes on approximations:
/// <list type="bullet">
/// <item>Illustrator's "Colors" slider behaves differently per palette: Limited = 2-30
///   integer count; Full Tone = 0-100% share threshold; Grayscale Limited = 2-256 grays.
///   Our <see cref="TraceOptions.ColorCount"/> is a single int; presets store the
///   visible-value count where it matters and our Full Tone path caps at 64 layers
///   internally regardless.</item>
/// <item>Illustrator's "Strokes" output mode (Line Art, Technical Drawing) renders
///   stroked-only paths instead of filled ones. potrace always emits filled paths;
///   for now those presets carry the same numeric parameters as Illustrator but the
///   output is filled. The Outlines view in the preview window approximates the
///   stroked look interactively.</item>
/// </list></summary>
public sealed record TracePreset(string Name, TraceOptions Options);

public static class TracePresets
{
    /// <summary>Pure white = (255, 255, 255). Used by several stock presets that ignore
    /// the canvas background ("Sketched Art", "Silhouettes").</summary>
    private static readonly System.Drawing.Color White = System.Drawing.Color.FromArgb(255, 255, 255, 255);

    public static IReadOnlyList<TracePreset> Stock { get; } = new[]
    {
        // Black and White, Threshold 128, Paths 50%, Corners 75%, Noise 25 px,
        // Method=Overlapping, Snap Curves to Lines ON. Identical to "Black and White
        // Logo" — Illustrator ships them as separate entries for muscle memory.
        new TracePreset("[Default]", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 128,
            PathsPercent: 50,
            CornersPercent: 75,
            NoisePx: 25,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: true)),

        // Color, Full Tone, Colors 85% (we cap at 64 layers internally regardless of
        // the slider value so the visible "85%" stores as 30 = max), Paths 50%,
        // Corners 50%, Noise 5 px (preserve fine detail), Method=Abutting.
        new TracePreset("High Fidelity Photo", new TraceOptions(
            Mode: TraceMode.Color,
            Palette: TracePalette.FullTone,
            ColorCount: 30,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 5,
            Method: TraceMethod.Abutting,
            SnapCurvesToLines: false)),

        // Color, Full Tone, Colors 20%, Paths 50%, Corners 50%, Noise 10 px,
        // Method=Abutting. ColorCount 6 ≈ "20% of 30".
        new TracePreset("Low Fidelity Photo", new TraceOptions(
            Mode: TraceMode.Color,
            Palette: TracePalette.FullTone,
            ColorCount: 6,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 10,
            Method: TraceMethod.Abutting,
            SnapCurvesToLines: false)),

        // Color, Limited, Colors 3, Paths 50%, Corners 50%, Noise 15 px, Overlapping.
        new TracePreset("3 Colors", new TraceOptions(
            Mode: TraceMode.Color,
            Palette: TracePalette.Limited,
            ColorCount: 3,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 15,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false)),

        new TracePreset("6 Colors", new TraceOptions(
            Mode: TraceMode.Color,
            Palette: TracePalette.Limited,
            ColorCount: 6,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 15,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false)),

        new TracePreset("16 Colors", new TraceOptions(
            Mode: TraceMode.Color,
            Palette: TracePalette.Limited,
            ColorCount: 16,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 15,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false)),

        // Grayscale, Limited, Grays 50, Paths 50%, Corners 50%, Noise 15 px,
        // Method=Abutting. (Illustrator slider goes higher in grayscale; our XAML
        // cap is 64 to accommodate.)
        new TracePreset("Shades of Gray", new TraceOptions(
            Mode: TraceMode.Grayscale,
            Palette: TracePalette.Limited,
            ColorCount: 50,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 15,
            Method: TraceMethod.Abutting,
            SnapCurvesToLines: false)),

        // BW, Threshold 128, Paths 50%, Corners 75%, Noise 25 px, Snap ON.
        new TracePreset("Black and White Logo", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 128,
            PathsPercent: 50,
            CornersPercent: 75,
            NoisePx: 25,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: true)),

        // BW, Threshold 128, Paths 50%, Corners 50%, Noise 20 px, Snap OFF,
        // Ignore Color = White (drops the paper background of a sketch scan).
        new TracePreset("Sketched Art", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 128,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 20,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false,
            IgnoreColor: White)),

        // BW, Threshold 230 (very bright cutoff isolates dark silhouettes only),
        // Noise 90 px (aggressive despeckle), Ignore Color = White.
        new TracePreset("Silhouettes", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 230,
            PathsPercent: 50,
            CornersPercent: 50,
            NoisePx: 90,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false,
            IgnoreColor: White)),

        // BW, Threshold 128, Paths 50%, Corners 75%, Noise 20 px. Illustrator emits
        // strokes-only at 50 px width here — we still emit filled paths; the
        // preview's Outlines view approximates the look.
        new TracePreset("Line Art", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 128,
            PathsPercent: 50,
            CornersPercent: 75,
            NoisePx: 20,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: false)),

        // BW, Threshold 128, Paths 50%, Corners 100% (preserve every corner), Noise
        // 1 px (no despeckle — keep tiny details), Snap ON. Illustrator emits 10 px
        // strokes; same caveat as Line Art.
        new TracePreset("Technical Drawing", new TraceOptions(
            Mode: TraceMode.BlackAndWhite,
            Threshold: 128,
            PathsPercent: 50,
            CornersPercent: 100,
            NoisePx: 1,
            Method: TraceMethod.Overlapping,
            SnapCurvesToLines: true)),
    };
}
