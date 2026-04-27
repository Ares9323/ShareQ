using ShareQ.Editor.Model;

namespace ShareQ.Editor.Tools;

public sealed class PixelateTool : IDrawingTool
{
    private const int DefaultBlockSize = 8;
    private double _startX, _startY;
    private bool _active;

    public EditorTool Kind => EditorTool.Pixelate;
    public Shape? PreviewShape { get; private set; }

    public void Begin(double x, double y, ShapeColor outline, ShapeColor fill, double strokeWidth)
    {
        _startX = x; _startY = y;
        _active = true;
        PreviewShape = BuildShape(x, y);
    }

    public void Update(double x, double y) { if (_active) PreviewShape = BuildShape(x, y); }

    public Shape? Commit(double x, double y)
    {
        if (!_active) return null;
        var final = BuildShape(x, y);
        _active = false;
        PreviewShape = null;
        return final is PixelateShape p && !p.IsEmpty ? final : null;
    }

    private PixelateShape BuildShape(double x, double y)
    {
        var left = Math.Min(_startX, x);
        var top = Math.Min(_startY, y);
        var width = Math.Abs(x - _startX);
        var height = Math.Abs(y - _startY);
        return new PixelateShape(left, top, width, height, DefaultBlockSize);
    }
}
