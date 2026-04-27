using ShareQ.Editor.Model;

namespace ShareQ.Editor.Adorners;

public readonly record struct GripPosition(GripKind Kind, double X, double Y);

public static class ShapeGripLayout
{
    /// <summary>Hit tolerance in screen pixels. Callers must divide by the current zoom factor when
    /// passing canvas coordinates so the click target stays consistent regardless of zoom.</summary>
    public const double DefaultHitTolerance = 6.0;

    /// <summary>Return the grip positions for a shape. Empty list when the shape doesn't expose grips
    /// (Freehand has none).</summary>
    public static IReadOnlyList<GripPosition> GripsFor(Shape shape) => shape switch
    {
        RectangleShape r => RectGrips(r.X, r.Y, r.Width, r.Height),
        EllipseShape e => RectGrips(e.X, e.Y, e.Width, e.Height),
        ArrowShape a => [new(GripKind.From, a.FromX, a.FromY), new(GripKind.To, a.ToX, a.ToY)],
        LineShape l => [new(GripKind.From, l.FromX, l.FromY), new(GripKind.To, l.ToX, l.ToY)],
        TextShape t => TextGrip(t),
        StepCounterShape c => [new(GripKind.Resize, c.CenterX + c.Radius * 0.707, c.CenterY + c.Radius * 0.707)],
        _ => []
    };

    /// <summary>Return the grip the point is on, or <see cref="GripKind.None"/>.</summary>
    /// <param name="hitTolerance">Half-side of the square hit zone, in the same coordinate system as
    /// (px, py). Pass <see cref="DefaultHitTolerance"/> divided by the current zoom factor to keep the
    /// click target screen-pixel constant.</param>
    public static GripKind HitTest(Shape shape, double px, double py, double hitTolerance = DefaultHitTolerance)
    {
        foreach (var g in GripsFor(shape))
        {
            if (Math.Abs(px - g.X) <= hitTolerance && Math.Abs(py - g.Y) <= hitTolerance) return g.Kind;
        }
        return GripKind.None;
    }

    private static IReadOnlyList<GripPosition> RectGrips(double x, double y, double w, double h) =>
    [
        new(GripKind.TopLeft, x, y),
        new(GripKind.Top, x + w / 2, y),
        new(GripKind.TopRight, x + w, y),
        new(GripKind.Left, x, y + h / 2),
        new(GripKind.Right, x + w, y + h / 2),
        new(GripKind.BottomLeft, x, y + h),
        new(GripKind.Bottom, x + w / 2, y + h),
        new(GripKind.BottomRight, x + w, y + h)
    ];

    private static IReadOnlyList<GripPosition> TextGrip(TextShape t)
    {
        // Use the same approximation as ShapeHitTester / EditorWindow.TextBounds.
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var w = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        var h = lines.Length * t.Style.FontSize * 1.2;
        return [new(GripKind.Resize, t.X + w, t.Y + h)];
    }
}
