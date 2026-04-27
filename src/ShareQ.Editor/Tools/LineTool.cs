using ShareQ.Editor.Model;

namespace ShareQ.Editor.Tools;

public sealed class LineTool : IDrawingTool
{
    private double _startX, _startY;
    private ShapeColor _outline = ShapeColor.Red;
    private double _strokeWidth = 2;
    private bool _active;

    public EditorTool Kind => EditorTool.Line;
    public Shape? PreviewShape { get; private set; }

    public void Begin(double x, double y, ShapeColor outline, ShapeColor fill, double strokeWidth)
    {
        _startX = x; _startY = y;
        _outline = outline; _strokeWidth = strokeWidth;
        _active = true;
        PreviewShape = new LineShape(_startX, _startY, x, y, _outline, _strokeWidth);
    }

    public void Update(double x, double y)
    {
        if (!_active) return;
        PreviewShape = new LineShape(_startX, _startY, x, y, _outline, _strokeWidth);
    }

    public Shape? Commit(double x, double y)
    {
        if (!_active) return null;
        _active = false;
        PreviewShape = null;
        var dx = x - _startX; var dy = y - _startY;
        if (dx * dx + dy * dy < 4) return null;
        return new LineShape(_startX, _startY, x, y, _outline, _strokeWidth);
    }
}
