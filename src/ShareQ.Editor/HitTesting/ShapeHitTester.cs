using ShareQ.Editor.Model;

namespace ShareQ.Editor.HitTesting;

public static class ShapeHitTester
{
    private const double Tolerance = 4.0;

    /// <summary>Returns the topmost shape under the point, or null. Iterates back-to-front (last = top).</summary>
    public static Shape? HitTest(IReadOnlyList<Shape> shapes, double px, double py)
    {
        for (var i = shapes.Count - 1; i >= 0; i--)
        {
            if (IsHit(shapes[i], px, py)) return shapes[i];
        }
        return null;
    }

    public static bool IsHit(Shape shape, double px, double py) => shape switch
    {
        RectangleShape r => HitRect(r, px, py),
        EllipseShape e => HitEllipse(e, px, py),
        ArrowShape a => HitSegment(a.FromX, a.FromY, a.ToX, a.ToY, a.StrokeWidth, px, py),
        LineShape l => HitSegment(l.FromX, l.FromY, l.ToX, l.ToY, l.StrokeWidth, px, py),
        FreehandShape f => HitFreehand(f, px, py),
        TextShape t => HitText(t, px, py),
        StepCounterShape c => HitStepCounter(c, px, py),
        _ => false
    };

    private static bool HitText(TextShape t, double px, double py)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var width = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        var height = lines.Length * t.Style.FontSize * 1.2;
        return px >= t.X && px <= t.X + width && py >= t.Y && py <= t.Y + height;
    }

    private static bool HitStepCounter(StepCounterShape c, double px, double py)
    {
        var dx = px - c.CenterX;
        var dy = py - c.CenterY;
        return dx * dx + dy * dy <= c.Radius * c.Radius;
    }

    private static bool HitRect(RectangleShape r, double x, double y)
    {
        var inside = x >= r.X && x <= r.X + r.Width && y >= r.Y && y <= r.Y + r.Height;
        if (!inside) return false;
        if (!r.Fill.IsTransparent) return true;
        var distLeft = Math.Abs(x - r.X);
        var distRight = Math.Abs(x - (r.X + r.Width));
        var distTop = Math.Abs(y - r.Y);
        var distBottom = Math.Abs(y - (r.Y + r.Height));
        var tol = Math.Max(Tolerance, r.StrokeWidth / 2 + 2);
        return distLeft < tol || distRight < tol || distTop < tol || distBottom < tol;
    }

    private static bool HitEllipse(EllipseShape e, double x, double y)
    {
        var cx = e.X + e.Width / 2.0;
        var cy = e.Y + e.Height / 2.0;
        var rx = e.Width / 2.0;
        var ry = e.Height / 2.0;
        if (rx <= 0 || ry <= 0) return false;
        var nx = (x - cx) / rx;
        var ny = (y - cy) / ry;
        var d = nx * nx + ny * ny;
        if (!e.Fill.IsTransparent) return d <= 1.0;
        return Math.Abs(d - 1.0) < 0.15;
    }

    private static bool HitSegment(double x1, double y1, double x2, double y2, double strokeWidth, double px, double py)
    {
        var dx = x2 - x1; var dy = y2 - y1;
        var lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-6) return false;
        var t = Math.Clamp(((px - x1) * dx + (py - y1) * dy) / lenSq, 0, 1);
        var nearestX = x1 + t * dx;
        var nearestY = y1 + t * dy;
        var distSq = (px - nearestX) * (px - nearestX) + (py - nearestY) * (py - nearestY);
        var tol = Math.Max(Tolerance, strokeWidth / 2 + 2);
        return distSq <= tol * tol;
    }

    private static bool HitFreehand(FreehandShape f, double x, double y)
    {
        for (var i = 0; i < f.Points.Count - 1; i++)
        {
            var (x1, y1) = f.Points[i];
            var (x2, y2) = f.Points[i + 1];
            if (HitSegment(x1, y1, x2, y2, f.StrokeWidth, x, y)) return true;
        }
        return false;
    }
}
