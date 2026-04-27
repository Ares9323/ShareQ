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
        RectangleShape r => HitRect(r, UnrotateAroundCenter(px, py, r.X + r.Width / 2, r.Y + r.Height / 2, r.Rotation)),
        EllipseShape e => HitEllipse(e, UnrotateAroundCenter(px, py, e.X + e.Width / 2, e.Y + e.Height / 2, e.Rotation)),
        ArrowShape a => HitSegment(a.FromX, a.FromY, a.ToX, a.ToY, a.StrokeWidth, px, py),
        LineShape l => HitSegment(l.FromX, l.FromY, l.ToX, l.ToY, l.StrokeWidth, px, py),
        FreehandShape f => HitFreehand(f, px, py),
        TextShape t => HitTextRotated(t, px, py),
        StepCounterShape c => HitStepCounter(c, px, py),
        BlurShape b => HitRectRegion(b.X, b.Y, b.Width, b.Height, px, py),
        PixelateShape p => HitRectRegion(p.X, p.Y, p.Width, p.Height, px, py),
        SpotlightShape s => HitRectRegion(s.X, s.Y, s.Width, s.Height, px, py),
        _ => false
    };

    /// <summary>Hit the perimeter of a rect region (for effect shapes whose interior is "see-through"
    /// in editing terms — clicking the middle should not block clicks on shapes underneath).</summary>
    private static bool HitRectRegion(double x, double y, double w, double h, double px, double py)
    {
        // Generous hit zone on the border so users can grab a thin frame easily.
        const double Tol = 6.0;
        if (px < x - Tol || px > x + w + Tol || py < y - Tol || py > y + h + Tol) return false;
        var distLeft = Math.Abs(px - x);
        var distRight = Math.Abs(px - (x + w));
        var distTop = Math.Abs(py - y);
        var distBottom = Math.Abs(py - (y + h));
        var insideX = px >= x && px <= x + w;
        var insideY = py >= y && py <= y + h;
        return (insideY && (distLeft < Tol || distRight < Tol))
            || (insideX && (distTop < Tol || distBottom < Tol));
    }

    /// <summary>Rotate <paramref name="px"/>,<paramref name="py"/> by -<paramref name="degrees"/> around
    /// (<paramref name="cx"/>,<paramref name="cy"/>). Used to hit-test a rotated shape against its
    /// non-rotated geometry.</summary>
    public static (double X, double Y) UnrotateAroundCenter(double px, double py, double cx, double cy, double degrees)
    {
        if (degrees == 0) return (px, py);
        var rad = -degrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = px - cx;
        var dy = py - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    private static bool HitRect(RectangleShape r, (double X, double Y) p) => HitRect(r, p.X, p.Y);

    private static bool HitEllipse(EllipseShape e, (double X, double Y) p) => HitEllipse(e, p.X, p.Y);

    private static bool HitTextRotated(TextShape t, double px, double py)
    {
        // The rotation pivot of a TextShape is the center of its bbox.
        var (w, h) = TextBboxSize(t);
        var cx = t.X + w / 2;
        var cy = t.Y + h / 2;
        var (qx, qy) = UnrotateAroundCenter(px, py, cx, cy, t.Rotation);
        return HitText(t, qx, qy);
    }

    private static (double Width, double Height) TextBboxSize(TextShape t)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        return (Math.Max(8, maxLen * t.Style.FontSize * 0.55), lines.Length * t.Style.FontSize * 1.2);
    }

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
