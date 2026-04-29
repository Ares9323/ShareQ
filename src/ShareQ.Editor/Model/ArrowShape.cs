namespace ShareQ.Editor.Model;

/// <summary>Arrow with optional curvature and rotation. <see cref="ControlOffsetX"/>/
/// <see cref="ControlOffsetY"/> displaces the quadratic-bezier control point relative to the
/// segment's midpoint — (0, 0) renders as a straight arrow. <see cref="Rotation"/> spins the
/// whole arrow around its midpoint (bezier shape preserved).</summary>
public sealed record ArrowShape(
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
