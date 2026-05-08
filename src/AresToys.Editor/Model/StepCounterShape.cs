namespace AresToys.Editor.Model;

public sealed record StepCounterShape(
    double CenterX,
    double CenterY,
    int Number,
    ShapeColor Outline,
    ShapeColor Fill,
    double StrokeWidth,
    string FontFamily = "Segoe UI",
    bool Bold = true,
    bool Italic = false)
    : Shape(Outline, Fill, StrokeWidth)
{
    /// <summary>Disc radius derived from <see cref="Shape.StrokeWidth"/>. Step counters used
    /// to carry an independent <c>Radius</c> field, but the user wanted size to ride on the
    /// existing stroke knob — that way the mouse-wheel utility (which adjusts stroke) and the
    /// "save as default" persistence (which already covers stroke) both work for free.
    /// Multiplier is 2.5 (not 10): at the slider's max stroke of 20 a step counter sits at
    /// radius 50, which matches the visual weight of an arrow with stroke ≈5 — the user's
    /// calibration point. *10 was too aggressive and produced 200-px discs at max stroke.</summary>
    public double Radius => StrokeWidth * 2.5;
}
