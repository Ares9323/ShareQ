namespace ShareQ.Editor.Model;

/// <summary>Straight or curved line with optional rotation around the midpoint. Symmetric to
/// <see cref="ArrowShape"/>; same bezier model.</summary>
public sealed record LineShape(
    double FromX, double FromY, double ToX, double ToY,
    ShapeColor Outline, double StrokeWidth,
    double ControlOffsetX = 0,
    double ControlOffsetY = 0,
    double Rotation = 0)
    : Shape(Outline, ShapeColor.Transparent, StrokeWidth)
{
    public (double X, double Y) Midpoint => ((FromX + ToX) / 2, (FromY + ToY) / 2);
    public (double X, double Y) ControlPoint => (Midpoint.X + ControlOffsetX, Midpoint.Y + ControlOffsetY);
    public bool IsCurved => ControlOffsetX != 0 || ControlOffsetY != 0;
}
