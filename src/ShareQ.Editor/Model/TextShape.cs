namespace ShareQ.Editor.Model;

public sealed record TextShape(
    double X,
    double Y,
    string Text,
    TextStyle Style,
    ShapeColor Outline,
    ShapeColor Fill,
    double StrokeWidth)
    : Shape(Outline, Fill, StrokeWidth)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Text);
}
