using ShareQ.Editor.Model;

namespace ShareQ.Editor.Adorners;

public readonly record struct GripPosition(GripKind Kind, double X, double Y);

public static class ShapeGripLayout
{
    /// <summary>Hit tolerance in screen pixels. Callers must divide by the current zoom factor when
    /// passing canvas coordinates so the click target stays consistent regardless of zoom.</summary>
    public const double DefaultHitTolerance = 6.0;

    /// <summary>Return the grip positions for a shape. Empty list when the shape doesn't expose grips
    /// (Freehand has none). <paramref name="rotateGripOffset"/> controls the distance of the optional
    /// rotation grip from the shape's top edge — pass <c>0</c> to suppress, or <c>25 / zoom</c> to keep
    /// it ~25 px screen-constant.</summary>
    public static IReadOnlyList<GripPosition> GripsFor(Shape shape, double rotateGripOffset = 0) => shape switch
    {
        RectangleShape r => RectGrips(r.X, r.Y, r.Width, r.Height, rotateGripOffset),
        EllipseShape e => RectGrips(e.X, e.Y, e.Width, e.Height, rotateGripOffset),
        ArrowShape a => [new(GripKind.From, a.FromX, a.FromY), new(GripKind.To, a.ToX, a.ToY)],
        LineShape l => [new(GripKind.From, l.FromX, l.FromY), new(GripKind.To, l.ToX, l.ToY)],
        TextShape t => TextGrip(t, rotateGripOffset),
        StepCounterShape c => [new(GripKind.Resize, c.CenterX + c.Radius * 0.707, c.CenterY + c.Radius * 0.707)],
        BlurShape b => RectGrips(b.X, b.Y, b.Width, b.Height, 0),
        PixelateShape p => RectGrips(p.X, p.Y, p.Width, p.Height, 0),
        SpotlightShape s => RectGrips(s.X, s.Y, s.Width, s.Height, 0),
        ImageShape i => RectGrips(i.X, i.Y, i.Width, i.Height, rotateGripOffset),
        _ => []
    };

    /// <summary>Return the grip the point is on, or <see cref="GripKind.None"/>.
    /// Click is unrotated around the shape's pivot before matching against the (non-rotated) grip layout.</summary>
    /// <param name="hitTolerance">Half-side of the square hit zone, in the same coordinate system as
    /// (px, py). Pass <see cref="DefaultHitTolerance"/> divided by the current zoom factor to keep the
    /// click target screen-pixel constant.</param>
    public static GripKind HitTest(Shape shape, double px, double py, double hitTolerance = DefaultHitTolerance, double rotateGripOffset = 0)
    {
        var (qx, qy) = UnrotatePointForShape(shape, px, py);
        foreach (var g in GripsFor(shape, rotateGripOffset))
        {
            if (Math.Abs(qx - g.X) <= hitTolerance && Math.Abs(qy - g.Y) <= hitTolerance) return g.Kind;
        }
        return GripKind.None;
    }

    /// <summary>Pivot (in canvas coordinates) around which the shape rotates. Non-rotatable shapes
    /// return their natural anchor (no-op for unrotate).</summary>
    public static (double X, double Y) PivotOf(Shape shape) => shape switch
    {
        RectangleShape r => (r.X + r.Width / 2, r.Y + r.Height / 2),
        EllipseShape e => (e.X + e.Width / 2, e.Y + e.Height / 2),
        TextShape t => TextPivot(t),
        StepCounterShape c => (c.CenterX, c.CenterY),
        BlurShape b => (b.X + b.Width / 2, b.Y + b.Height / 2),
        PixelateShape p => (p.X + p.Width / 2, p.Y + p.Height / 2),
        SpotlightShape s => (s.X + s.Width / 2, s.Y + s.Height / 2),
        ImageShape i => (i.X + i.Width / 2, i.Y + i.Height / 2),
        _ => (0, 0)
    };

    public static double RotationOf(Shape shape) => shape switch
    {
        RectangleShape r => r.Rotation,
        EllipseShape e => e.Rotation,
        TextShape t => t.Rotation,
        ImageShape i => i.Rotation,
        _ => 0
    };

    private static (double X, double Y) UnrotatePointForShape(Shape shape, double px, double py)
    {
        var deg = RotationOf(shape);
        if (deg == 0) return (px, py);
        var (cx, cy) = PivotOf(shape);
        var rad = -deg * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);
        var dx = px - cx;
        var dy = py - cy;
        return (cx + dx * cos - dy * sin, cy + dx * sin + dy * cos);
    }

    private static (double X, double Y) TextPivot(TextShape t)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var w = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        var h = lines.Length * t.Style.FontSize * 1.2;
        return (t.X + w / 2, t.Y + h / 2);
    }

    private static IReadOnlyList<GripPosition> RectGrips(double x, double y, double w, double h, double rotateOffset)
    {
        var grips = new List<GripPosition>(9)
        {
            new(GripKind.TopLeft, x, y),
            new(GripKind.Top, x + w / 2, y),
            new(GripKind.TopRight, x + w, y),
            new(GripKind.Left, x, y + h / 2),
            new(GripKind.Right, x + w, y + h / 2),
            new(GripKind.BottomLeft, x, y + h),
            new(GripKind.Bottom, x + w / 2, y + h),
            new(GripKind.BottomRight, x + w, y + h)
        };
        if (rotateOffset > 0) grips.Add(new(GripKind.Rotate, x + w / 2, y - rotateOffset));
        return grips;
    }

    private static IReadOnlyList<GripPosition> TextGrip(TextShape t, double rotateOffset)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var w = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        var h = lines.Length * t.Style.FontSize * 1.2;
        var grips = new List<GripPosition>(2) { new(GripKind.Resize, t.X + w, t.Y + h) };
        if (rotateOffset > 0) grips.Add(new(GripKind.Rotate, t.X + w / 2, t.Y - rotateOffset));
        return grips;
    }
}
