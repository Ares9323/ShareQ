using ShareQ.Editor.Model;

namespace ShareQ.Editor.Tools;

public sealed class FreehandTool : IDrawingTool
{
    private readonly List<(double X, double Y)> _points = [];
    private ShapeColor _outline = ShapeColor.Red;
    private double _strokeWidth = 2;
    private bool _active;

    public EditorTool Kind => EditorTool.Freehand;
    public Shape? PreviewShape { get; private set; }

    /// <summary>Sticky default applied to new strokes — propagated by <c>EditorViewModel</c> from
    /// the persisted <c>EditorDefaults</c>. Toggling the per-shape Smooth flag in the properties
    /// panel also updates this so the next stroke inherits the same choice.</summary>
    public bool SmoothStrokes { get; set; } = true;

    public void Begin(double x, double y, ShapeColor outline, ShapeColor fill, double strokeWidth)
    {
        _points.Clear();
        _points.Add((x, y));
        _outline = outline; _strokeWidth = strokeWidth;
        _active = true;
        PreviewShape = new FreehandShape([.. _points], _outline, _strokeWidth, Smooth: SmoothStrokes);
    }

    public void Update(double x, double y)
    {
        if (!_active) return;
        var last = _points[^1];
        if (Math.Abs(last.X - x) < 0.5 && Math.Abs(last.Y - y) < 0.5) return;
        _points.Add((x, y));
        PreviewShape = new FreehandShape([.. _points], _outline, _strokeWidth, Smooth: SmoothStrokes);
    }

    public Shape? Commit(double x, double y)
    {
        if (!_active) return null;
        _active = false;
        if (_points.Count < 2) { PreviewShape = null; return null; }
        var shape = new FreehandShape([.. _points], _outline, _strokeWidth, Smooth: SmoothStrokes);
        PreviewShape = null;
        return shape;
    }
}
