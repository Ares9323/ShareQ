using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;

namespace ShareQ.Editor.ViewModels;

public sealed partial class EditorViewModel : ObservableObject
{
    private readonly Dictionary<EditorTool, IDrawingTool> _tools;
    private IDrawingTool _activeTool;

    public EditorViewModel()
    {
        _tools = new Dictionary<EditorTool, IDrawingTool>
        {
            [EditorTool.Select] = new SelectTool(),
            [EditorTool.Rectangle] = new RectangleTool()
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

    public Shape? PreviewShape => _activeTool.PreviewShape;

    partial void OnCurrentToolChanged(EditorTool value)
    {
        _activeTool = _tools[value];
    }

    public void BeginGesture(double x, double y)
    {
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
        if (committed is not null) Shapes.Add(committed);
        OnPropertyChanged(nameof(PreviewShape));
    }

    [RelayCommand]
    private void Undo()
    {
        if (Shapes.Count == 0) return;
        Shapes.RemoveAt(Shapes.Count - 1);
    }

    [RelayCommand]
    private void SelectTool(EditorTool tool) => CurrentTool = tool;
}
