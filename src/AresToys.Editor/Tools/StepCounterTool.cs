using AresToys.Editor.Model;

namespace AresToys.Editor.Tools;

public sealed class StepCounterTool : IDrawingTool
{
    private int _next = 1;
    private StepCounterShape? _preview;

    /// <summary>Font family for the digits inside the disc. Set by <see cref="ViewModels.EditorViewModel"/>
    /// from its sticky <c>StepFontFamily</c>/<c>StepBold</c>/<c>StepItalic</c> defaults so each newly
    /// drawn step counter inherits the user's last choice — same propagation pattern as the
    /// freehand-tool flags.</summary>
    public string FontFamily { get; set; } = "Segoe UI";
    public bool Bold { get; set; } = true;
    public bool Italic { get; set; }

    public EditorTool Kind => EditorTool.StepCounter;

    public Shape? PreviewShape => _preview;

    public void Reset() => _next = 1;

    public void SetNext(int n) => _next = Math.Max(1, n);

    public void Begin(double x, double y, ShapeColor outline, ShapeColor fill, double strokeWidth)
    {
        // Radius derives from StrokeWidth*10 (computed inside StepCounterShape) — no separate
        // size param. The stroke slider / mouse-wheel become the size knob.
        _preview = new StepCounterShape(x, y, _next, outline, fill, strokeWidth, FontFamily, Bold, Italic);
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
