namespace ShareQ.Editor.Model;

public sealed record FreehandShape(
    IReadOnlyList<(double X, double Y)> Points,
    ShapeColor Outline, double StrokeWidth)
    : Shape(Outline, ShapeColor.Transparent, StrokeWidth);
