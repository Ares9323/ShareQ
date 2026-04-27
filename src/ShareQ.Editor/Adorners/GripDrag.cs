using ShareQ.Editor.Model;

namespace ShareQ.Editor.Adorners;

public static class GripDrag
{
    /// <summary>Compute the shape produced by dragging <paramref name="grip"/> on <paramref name="start"/>
    /// to point (<paramref name="px"/>, <paramref name="py"/>). Returns null if the grip / shape pair
    /// is unsupported.</summary>
    /// <param name="shiftHeld">When true: rect/ellipse preserve aspect ratio; line/arrow snap to 45°;
    /// text/stepcounter behave the same as without shift.</param>
    public static Shape? Transform(Shape start, GripKind grip, double px, double py, bool shiftHeld) => start switch
    {
        RectangleShape r when grip == GripKind.Rotate => r with { Rotation = AngleFromPivot(r.X + r.Width / 2, r.Y + r.Height / 2, px, py, shiftHeld) },
        EllipseShape e when grip == GripKind.Rotate => e with { Rotation = AngleFromPivot(e.X + e.Width / 2, e.Y + e.Height / 2, px, py, shiftHeld) },
        TextShape txt when grip == GripKind.Rotate => txt with { Rotation = AngleFromPivot(TextCenterX(txt), TextCenterY(txt), px, py, shiftHeld) },
        RectangleShape r => ResizeRect(new RectShim(r.X, r.Y, r.Width, r.Height), grip, px, py, shiftHeld) is { } box
            ? r with { X = box.X, Y = box.Y, Width = box.W, Height = box.H }
            : null,
        EllipseShape e => ResizeRect(new RectShim(e.X, e.Y, e.Width, e.Height), grip, px, py, shiftHeld) is { } box
            ? e with { X = box.X, Y = box.Y, Width = box.W, Height = box.H }
            : null,
        LineShape l => grip switch
        {
            GripKind.From => SnapEndpoint(l.ToX, l.ToY, px, py, shiftHeld) is var (fx, fy)
                ? l with { FromX = fx, FromY = fy }
                : null,
            GripKind.To => SnapEndpoint(l.FromX, l.FromY, px, py, shiftHeld) is var (tx, ty)
                ? l with { ToX = tx, ToY = ty }
                : null,
            _ => null
        },
        ArrowShape a => grip switch
        {
            GripKind.From => SnapEndpoint(a.ToX, a.ToY, px, py, shiftHeld) is var (fx, fy)
                ? a with { FromX = fx, FromY = fy }
                : null,
            GripKind.To => SnapEndpoint(a.FromX, a.FromY, px, py, shiftHeld) is var (tx, ty)
                ? a with { ToX = tx, ToY = ty }
                : null,
            _ => null
        },
        TextShape t => grip == GripKind.Resize ? ResizeText(t, px, py) : null,
        StepCounterShape c => grip == GripKind.Resize ? ResizeStepCounter(c, px, py) : null,
        _ => null
    };

    /// <summary>Compute the new (X, Y, Width, Height) for a resized rectangle. The shape interface
    /// is shared with EllipseShape via <see cref="RectShim"/>.</summary>
    private static (double X, double Y, double W, double H)? ResizeRect(IRectLike s, GripKind grip, double px, double py, bool shiftHeld)
    {
        // Identify the "fixed corner" — the diagonal opposite of the dragged grip — for resizing.
        double fixedX, fixedY;
        bool freeX = true, freeY = true;
        switch (grip)
        {
            case GripKind.TopLeft: fixedX = s.X + s.Width; fixedY = s.Y + s.Height; break;
            case GripKind.TopRight: fixedX = s.X; fixedY = s.Y + s.Height; break;
            case GripKind.BottomLeft: fixedX = s.X + s.Width; fixedY = s.Y; break;
            case GripKind.BottomRight: fixedX = s.X; fixedY = s.Y; break;
            case GripKind.Top: fixedX = s.X; fixedY = s.Y + s.Height; freeX = false; break;
            case GripKind.Bottom: fixedX = s.X; fixedY = s.Y; freeX = false; break;
            case GripKind.Left: fixedX = s.X + s.Width; fixedY = s.Y; freeY = false; break;
            case GripKind.Right: fixedX = s.X; fixedY = s.Y; freeY = false; break;
            default: return null;
        }

        var newX = freeX ? Math.Min(fixedX, px) : s.X;
        var newY = freeY ? Math.Min(fixedY, py) : s.Y;
        var newW = freeX ? Math.Abs(px - fixedX) : s.Width;
        var newH = freeY ? Math.Abs(py - fixedY) : s.Height;

        if (shiftHeld && freeX && freeY)
        {
            // Aspect-ratio constraint: lock both dimensions to the larger of the two scales.
            var size = Math.Max(newW, newH);
            // Anchor at the fixed corner, expand toward the drag direction.
            var dirX = Math.Sign(px - fixedX);
            var dirY = Math.Sign(py - fixedY);
            if (dirX == 0) dirX = 1;
            if (dirY == 0) dirY = 1;
            newX = dirX > 0 ? fixedX : fixedX - size;
            newY = dirY > 0 ? fixedY : fixedY - size;
            newW = size;
            newH = size;
        }

        // Clamp to a minimum so we don't end up with degenerate zero-size shapes.
        if (newW < 1) newW = 1;
        if (newH < 1) newH = 1;
        return (newX, newY, newW, newH);
    }

    /// <summary>The Rotate grip lives ABOVE the shape (negative Y from pivot) when Rotation == 0,
    /// so we offset the raw atan2 by 90° to get a clean 0° = "no rotation". Shift snaps to 15° steps.</summary>
    private static double AngleFromPivot(double cx, double cy, double px, double py, bool shiftHeld)
    {
        var dx = px - cx;
        var dy = py - cy;
        if (dx == 0 && dy == 0) return 0;
        var deg = Math.Atan2(dy, dx) * 180.0 / Math.PI + 90.0;
        if (shiftHeld) deg = Math.Round(deg / 15.0) * 15.0;
        // Normalize to [-180, 180): more intuitive than [0, 360) — left = negative, right = positive.
        deg = ((deg + 180) % 360 + 360) % 360 - 180;
        return deg;
    }

    private static double TextCenterX(TextShape t)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var w = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        return t.X + w / 2;
    }

    private static double TextCenterY(TextShape t)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var h = lines.Length * t.Style.FontSize * 1.2;
        return t.Y + h / 2;
    }

    private static (double X, double Y) SnapEndpoint(double anchorX, double anchorY, double px, double py, bool shiftHeld)
    {
        if (!shiftHeld) return (px, py);
        var dx = px - anchorX;
        var dy = py - anchorY;
        var angle = Math.Atan2(dy, dx);
        var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
        var len = Math.Sqrt(dx * dx + dy * dy);
        return (anchorX + len * Math.Cos(snapped), anchorY + len * Math.Sin(snapped));
    }

    private static TextShape ResizeText(TextShape t, double px, double py)
    {
        // Scale the FontSize by the diagonal distance the grip travels relative to the text's anchor (X, Y).
        // Use the longer of horizontal/vertical projections so it works for both wide and tall text.
        var dx = Math.Max(8, px - t.X);
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        // Inverse of the bbox formula: width = maxLen * fontSize * 0.55  →  fontSize = width / (maxLen * 0.55).
        var fontSizeFromWidth = maxLen > 0 ? dx / (maxLen * 0.55) : t.Style.FontSize;
        // Also derive from height so single-character text still resizes by vertical drag.
        var dy = Math.Max(1, py - t.Y);
        var fontSizeFromHeight = dy / (lines.Length * 1.2);
        var size = Math.Clamp(Math.Max(fontSizeFromWidth, fontSizeFromHeight), 4, 400);
        return t with { Style = t.Style with { FontSize = size } };
    }

    private static StepCounterShape ResizeStepCounter(StepCounterShape c, double px, double py)
    {
        var dx = px - c.CenterX;
        var dy = py - c.CenterY;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        // Grip sits at radius * sqrt(2)/2 ≈ radius * 0.707; invert to recover radius from drag distance.
        var radius = Math.Clamp(dist / 0.707, 6, 200);
        return c with { Radius = radius };
    }

    private interface IRectLike
    {
        double X { get; }
        double Y { get; }
        double Width { get; }
        double Height { get; }
    }

    private sealed record RectShim(double X, double Y, double Width, double Height) : IRectLike;
}
