namespace ShareQ.Editor.Model;

/// <summary>Free-drawn polyline. Rotation spins the stroke around its bounding-box centre,
/// so Alt+wheel rotation works the same way it does on rect / ellipse / arrow / line.</summary>
public sealed record FreehandShape(
    IReadOnlyList<(double X, double Y)> Points,
    ShapeColor Outline, double StrokeWidth,
    double Rotation = 0)
    : Shape(Outline, ShapeColor.Transparent, StrokeWidth)
{
    /// <summary>Pivot for rotation: bbox centre. Returns (0, 0) when the stroke has no points.</summary>
    public (double X, double Y) Pivot
    {
        get
        {
            if (Points.Count == 0) return (0, 0);
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (x, y) in Points)
            {
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
            return ((minX + maxX) / 2, (minY + maxY) / 2);
        }
    }
}
