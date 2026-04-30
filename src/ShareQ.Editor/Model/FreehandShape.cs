namespace ShareQ.Editor.Model;

/// <summary>Free-drawn polyline. Rotation spins the stroke around its bounding-box centre.
/// <see cref="Smooth"/> tells the renderer to draw the points through a Catmull-Rom curve
/// instead of a jagged polyline — same data, gentler look. <see cref="EndArrow"/> caps the
/// stroke with a filled arrow head tangent to the last segment (ShareX-style "freehand
/// arrow"); the head shares <see cref="Outline"/> so the cap reads as part of the line.</summary>
public sealed record FreehandShape(
    IReadOnlyList<(double X, double Y)> Points,
    ShapeColor Outline, double StrokeWidth,
    double Rotation = 0,
    bool Smooth = false,
    bool EndArrow = false)
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
