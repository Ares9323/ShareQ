using ShareQ.Editor.Model;

namespace ShareQ.Editor.Tools;

public sealed class StepCounterTool : IDrawingTool
{
    private int _next = 1;
    private StepCounterShape? _preview;

    public EditorTool Kind => EditorTool.StepCounter;

    public Shape? PreviewShape => _preview;

    public void Reset() => _next = 1;

    public void SetNext(int n) => _next = Math.Max(1, n);

    public void Begin(double x, double y, ShapeColor outline, ShapeColor fill, double strokeWidth)
    {
        var radius = Math.Max(16, strokeWidth * 6);
        _preview = new StepCounterShape(x, y, radius, _next, outline, fill, strokeWidth);
    }

    public void Update(double x, double y)
    {
        if (_preview is null) return;
        _preview = _preview with { CenterX = x, CenterY = y };
    }

    public Shape? Commit(double x, double y)
    {
        var p = _preview;
        _preview = null;
        if (p is null) return null;
        _next++;
        return p;
    }
}
