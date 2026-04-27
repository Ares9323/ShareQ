namespace ShareQ.Editor.Model;

public sealed record ArrowShape(
    double FromX, double FromY, double ToX, double ToY,
    ShapeColor Outline, double StrokeWidth)
    : Shape(Outline, ShapeColor.Transparent, StrokeWidth);
