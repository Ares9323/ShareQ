using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Editor.Commands;
using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;

namespace ShareQ.Editor.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly Dictionary<EditorTool, IDrawingTool> _tools;
    private readonly EditorCommandStack _commands = new();
    private IDrawingTool _activeTool;
    private bool _suppressSelectionSync;

    public EditorViewModel()
    {
        _tools = new Dictionary<EditorTool, IDrawingTool>
        {
            [EditorTool.Select] = new SelectTool(),
            [EditorTool.Rectangle] = new RectangleTool(),
            [EditorTool.Arrow] = new ArrowTool(),
            [EditorTool.Line] = new LineTool(),
            [EditorTool.Ellipse] = new EllipseTool(),
            [EditorTool.Freehand] = new FreehandTool(),
            [EditorTool.Text] = new TextTool(TextStyle.Default),
            [EditorTool.StepCounter] = new StepCounterTool()
        };
        _activeTool = _tools[EditorTool.Rectangle];
        Shapes = [];
    }

    public ObservableCollection<Shape> Shapes { get; }

    [ObservableProperty]
    private byte[] _sourcePngBytes = [];

    [ObservableProperty]
    private long _editingItemId;

    [ObservableProperty]
    private EditorTool _currentTool = EditorTool.Rectangle;

    [ObservableProperty]
    private ShapeColor _outlineColor = ShapeColor.Red;

    [ObservableProperty]
    private ShapeColor _fillColor = ShapeColor.Transparent;

    [ObservableProperty]
    private double _strokeWidth = 2;

    [ObservableProperty]
    private TextStyle _currentTextStyle = TextStyle.Default;

    [ObservableProperty]
    private Shape? _selectedShape;

    /// <summary>
    /// Multi-selection (e.g. via marquee). <see cref="SelectedShape"/> is the "primary" focused
    /// shape (kept in sync with the first entry of this set when set externally).
    /// </summary>
    public ObservableCollection<Shape> SelectedShapes { get; } = [];

    public Shape? PreviewShape => _activeTool.PreviewShape;

    /// <summary>Replace the multi-selection set; <see cref="SelectedShape"/> becomes the first entry (or null).</summary>
    public void SetSelection(IReadOnlyList<Shape> shapes)
    {
        SelectedShapes.Clear();
        foreach (var s in shapes) SelectedShapes.Add(s);
        SelectedShape = shapes.Count > 0 ? shapes[0] : null;
    }

    public void RemoveShapes(IReadOnlyList<Shape> shapes)
    {
        if (shapes.Count == 0) return;
        _commands.Execute(new RemoveShapesCommand(shapes), Shapes);
        SetSelection([]);
    }

    partial void OnCurrentToolChanged(EditorTool value)
    {
        _activeTool = _tools[value];
        if (value != EditorTool.Select) SetSelection([]);
    }

    partial void OnCurrentTextStyleChanged(TextStyle value)
    {
        // Refresh the TextTool so the next click uses the new style.
        _tools[EditorTool.Text] = new TextTool(value);
        if (CurrentTool == EditorTool.Text) _activeTool = _tools[EditorTool.Text];
    }

    public void AddTextShape(TextShape shape)
    {
        if (shape.IsEmpty) return;
        _commands.Execute(new AddShapeCommand(shape), Shapes);
    }

    public void ResetStepCounter()
    {
        if (_tools[EditorTool.StepCounter] is StepCounterTool t) t.Reset();
    }

    partial void OnSelectedShapeChanged(Shape? value)
    {
        // Suppress when LiveReplaceShape is about to update both SelectedShapes and SelectedShape.
        if (_suppressSelectionSync) return;

        if (value is null)
        {
            if (SelectedShapes.Count > 0) SelectedShapes.Clear();
            return;
        }
        if (SelectedShapes.Contains(value)) return;
        SelectedShapes.Clear();
        SelectedShapes.Add(value);
    }

    public void BeginGesture(double x, double y)
    {
        // Re-derive the next step number from the current shape set so undo/delete reuses freed numbers.
        if (_activeTool is StepCounterTool sct)
        {
            var maxN = Shapes.OfType<StepCounterShape>().Select(s => s.Number).DefaultIfEmpty(0).Max();
            sct.SetNext(maxN + 1);
        }
        _activeTool.Begin(x, y, OutlineColor, FillColor, StrokeWidth);
        OnPropertyChanged(nameof(PreviewShape));
    }

    public void UpdateGesture(double x, double y)
    {
        _activeTool.Update(x, y);
        OnPropertyChanged(nameof(PreviewShape));
    }

    public void CommitGesture(double x, double y)
    {
        var committed = _activeTool.Commit(x, y);
        if (committed is not null)
        {
            _commands.Execute(new AddShapeCommand(committed), Shapes);
            // Auto-select the just-drawn shape so the property panel shows its attributes.
            // The active tool stays as-is — the user can keep drawing more shapes; the next
            // BeginGesture will not clear the selection (only a tool switch does).
            SetSelection([committed]);
        }
        OnPropertyChanged(nameof(PreviewShape));
    }

    public void ApplyShapeEdit(Shape oldShape, Shape newShape)
    {
        if (ReferenceEquals(oldShape, newShape)) return;
        _commands.Execute(new ReplaceShapeCommand(oldShape, newShape), Shapes);
        SelectedShape = newShape;
    }

    /// <summary>Replace a shape in the collection without pushing to the command stack.
    /// Used for live preview during interactive edits (drag-to-move, property panel).
    /// Also updates the multi-selection set in lockstep so a multi-shape drag/edit doesn't collapse.</summary>
    public void LiveReplaceShape(Shape oldShape, Shape newShape)
    {
        if (ReferenceEquals(oldShape, newShape)) return;
        var idx = Shapes.IndexOf(oldShape);
        if (idx < 0) return;
        Shapes[idx] = newShape;

        var selIdx = SelectedShapes.IndexOf(oldShape);
        if (selIdx >= 0) SelectedShapes[selIdx] = newShape;

        if (ReferenceEquals(SelectedShape, oldShape))
        {
            _suppressSelectionSync = true;
            try { SelectedShape = newShape; }
            finally { _suppressSelectionSync = false; }
        }
    }

    /// <summary>Push a single ReplaceShapeCommand spanning a live-edit gesture (e.g. drag-to-move
    /// or a sequence of property changes), so undo reverts the whole gesture in one step.</summary>
    public void CommitLiveEdit(Shape originalShape, Shape currentShape)
    {
        if (ReferenceEquals(originalShape, currentShape)) return;
        // The command's Apply runs against current state; since the live edit already mutated
        // Shapes[idx] to currentShape, push a synthetic command that records the swap for undo.
        _commands.RecordCommittedReplacement(originalShape, currentShape);
    }

    [RelayCommand]
    private void Undo()
    {
        if (_commands.Undo(Shapes))
        {
            if (SelectedShape is not null && !Shapes.Contains(SelectedShape)) SelectedShape = null;
        }
    }

    [RelayCommand]
    private void Redo()
    {
        _commands.Redo(Shapes);
    }

    [RelayCommand]
    private void SelectTool(EditorTool tool) => CurrentTool = tool;
}
