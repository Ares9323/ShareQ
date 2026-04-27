namespace ShareQ.Editor.Model;

public sealed record StepCounterShape(
    double CenterX,
    double CenterY,
    double Radius,
    int Number,
    ShapeColor Outline,
    ShapeColor Fill,
    double StrokeWidth)
    : Shape(Outline, Fill, StrokeWidth);
