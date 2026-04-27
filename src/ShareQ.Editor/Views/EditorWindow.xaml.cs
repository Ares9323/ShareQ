using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShareQ.Editor.HitTesting;
using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using ShareQ.Editor.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.Editor.Views;

public partial class EditorWindow : FluentWindow
{
    private readonly EditorViewModel _vm;
    private double _gestureStartX, _gestureStartY;

    private double _zoom = 1.0;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8.0;

    // Marquee state (Select tool, drag on empty area).
    private bool _isMarqueeing;
    private System.Windows.Shapes.Rectangle? _marqueeRect;
    private double _marqueeStartX, _marqueeStartY;

    // Move-drag state (Select tool): _moveStartShape captures the shape at mouse-down,
    // _moveOriginalShape captures the shape as it was when first selected (for undo span).
    private Shape? _moveStartShape;
    private double _moveAnchorX, _moveAnchorY;
    private bool _isDraggingShape;
    // Snapshot of selected shapes at mouse-down, used to compute translation across all selected items.
    private List<Shape>? _moveStartSnapshots;

    // Live-edit tracking: when a shape is selected, _liveEditOriginal holds its initial state
    // so a sequence of property tweaks (or a drag-to-move) commits as ONE undo step.
    private Shape? _liveEditOriginal;
    private bool _suppressLiveUpdates;

    public EditorWindow(EditorViewModel viewModel)
    {
        InitializeComponent();
        _vm = viewModel;
        DataContext = _vm;

        OutlineSwatch.SelectedColor = _vm.OutlineColor;
        FillSwatch.SelectedColor = _vm.FillColor;
        OutlineSwatch.SetBinding(ColorSwatchButton.SelectedColorProperty,
            new System.Windows.Data.Binding(nameof(EditorViewModel.OutlineColor)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        FillSwatch.SetBinding(ColorSwatchButton.SelectedColorProperty,
            new System.Windows.Data.Binding(nameof(EditorViewModel.FillColor)) { Mode = System.Windows.Data.BindingMode.TwoWay });
        StrokeSlider.SetBinding(Slider.ValueProperty,
            new System.Windows.Data.Binding(nameof(EditorViewModel.StrokeWidth)) { Mode = System.Windows.Data.BindingMode.TwoWay });

        _vm.Shapes.CollectionChanged += OnShapesChanged;
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(EditorViewModel.PreviewShape)) RedrawPreview();
            if (e.PropertyName == nameof(EditorViewModel.SourcePngBytes)) LoadSourceImage();
            if (e.PropertyName == nameof(EditorViewModel.SelectedShape)) OnSelectedShapeChanged();
            if (e.PropertyName == nameof(EditorViewModel.CurrentTool)) RefreshToolButtonHighlight();
        };
        _vm.SelectedShapes.CollectionChanged += (_, _) => RedrawAll();

        // Realtime property panel: each change live-updates the shape via DependencyPropertyDescriptor hooks.
        var swatchColorDesc = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            ColorSwatchButton.SelectedColorProperty, typeof(ColorSwatchButton));
        swatchColorDesc?.AddValueChanged(SelOutlineSwatch, (_, _) => OnSelOutlineChanged());
        swatchColorDesc?.AddValueChanged(SelFillSwatch, (_, _) => OnSelFillChanged());
        SelStrokeSlider.ValueChanged += (_, _) => OnSelStrokeChanged();

        Loaded += (_, _) => { LoadSourceImage(); RefreshPropertyPanel(); RefreshToolButtonHighlight(); };
        Closing += (_, _) => CommitPendingLiveEdit();
    }

    public bool Saved { get; private set; }

    private void LoadSourceImage()
    {
        if (_vm.SourcePngBytes.Length == 0) return;
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.StreamSource = new MemoryStream(_vm.SourcePngBytes);
        bmp.EndInit();
        bmp.Freeze();
        SourceImage.Source = bmp;
        DrawingCanvas.Width = bmp.PixelWidth;
        DrawingCanvas.Height = bmp.PixelHeight;
        SourceImage.Width = bmp.PixelWidth;
        SourceImage.Height = bmp.PixelHeight;
    }

    private void OnSelectToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Select;
    private void OnRectangleToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Rectangle;
    private void OnArrowToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Arrow;
    private void OnLineToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Line;
    private void OnEllipseToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Ellipse;
    private void OnFreehandToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Freehand;

    private void RefreshToolButtonHighlight()
    {
        var buttons = new[]
        {
            (Btn: SelectToolBtn, Tool: EditorTool.Select),
            (Btn: RectangleToolBtn, Tool: EditorTool.Rectangle),
            (Btn: ArrowToolBtn, Tool: EditorTool.Arrow),
            (Btn: LineToolBtn, Tool: EditorTool.Line),
            (Btn: EllipseToolBtn, Tool: EditorTool.Ellipse),
            (Btn: FreehandToolBtn, Tool: EditorTool.Freehand)
        };
        foreach (var (btn, tool) in buttons)
        {
            btn.Appearance = tool == _vm.CurrentTool
                ? Wpf.Ui.Controls.ControlAppearance.Primary
                : Wpf.Ui.Controls.ControlAppearance.Secondary;
        }
    }
    private void OnUndoClicked(object sender, RoutedEventArgs e) => _vm.UndoCommand.Execute(null);
    private void OnRedoClicked(object sender, RoutedEventArgs e) => _vm.RedoCommand.Execute(null);
    private void OnSaveClicked(object sender, RoutedEventArgs e) { Saved = true; Close(); }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { Saved = false; Close(); }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;
        var factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
        SetZoom(_zoom * factor);
        e.Handled = true;
    }

    private void SetZoom(double newZoom)
    {
        var clamped = Math.Clamp(newZoom, MinZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.001) return;
        _zoom = clamped;
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomLabel.Text = $"{_zoom * 100:F0}%";
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key == Key.Z) { _vm.UndoCommand.Execute(null); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { _vm.RedoCommand.Execute(null); e.Handled = true; return; }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_vm.SelectedShapes.Count > 0)
            {
                CommitPendingLiveEdit(); // Settle any in-flight edit before destroying the selection
                _vm.RemoveShapes([.. _vm.SelectedShapes]);
                e.Handled = true;
                return;
            }
        }

        switch (e.Key)
        {
            case Key.V: _vm.CurrentTool = EditorTool.Select; e.Handled = true; break;
            case Key.R: _vm.CurrentTool = EditorTool.Rectangle; e.Handled = true; break;
            case Key.A: _vm.CurrentTool = EditorTool.Arrow; e.Handled = true; break;
            case Key.L: _vm.CurrentTool = EditorTool.Line; e.Handled = true; break;
            case Key.E: _vm.CurrentTool = EditorTool.Ellipse; e.Handled = true; break;
            case Key.P: _vm.CurrentTool = EditorTool.Freehand; e.Handled = true; break;
            default: break;
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(DrawingCanvas);

        if (_vm.CurrentTool == EditorTool.Select)
        {
            var hit = ShapeHitTester.HitTest(_vm.Shapes, p.X, p.Y);
            if (hit is not null)
            {
                // Click on a shape: if not already in selection, replace selection with [hit].
                if (!_vm.SelectedShapes.Contains(hit))
                {
                    CommitPendingLiveEdit();
                    _vm.SetSelection([hit]);
                }
                _moveStartShape = hit;
                _moveAnchorX = p.X;
                _moveAnchorY = p.Y;
                _isDraggingShape = true;
                DrawingCanvas.CaptureMouse();
            }
            else
            {
                // Click on empty area: begin marquee + clear selection.
                CommitPendingLiveEdit();
                _vm.SetSelection([]);
                _isMarqueeing = true;
                _marqueeStartX = p.X;
                _marqueeStartY = p.Y;
                _marqueeRect = new System.Windows.Shapes.Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromArgb(180, 80, 200, 255)),
                    StrokeThickness = 1,
                    StrokeDashArray = [3.0, 2.0],
                    Fill = new SolidColorBrush(Color.FromArgb(40, 80, 200, 255)),
                    IsHitTestVisible = false,
                    Tag = "marquee"
                };
                Canvas.SetLeft(_marqueeRect, p.X);
                Canvas.SetTop(_marqueeRect, p.Y);
                DrawingCanvas.Children.Add(_marqueeRect);
                DrawingCanvas.CaptureMouse();
            }
            return;
        }

        _gestureStartX = p.X;
        _gestureStartY = p.Y;
        DrawingCanvas.CaptureMouse();
        _vm.BeginGesture(p.X, p.Y);
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (_vm.CurrentTool == EditorTool.Select)
        {
            var p = e.GetPosition(DrawingCanvas);
            if (_isMarqueeing && _marqueeRect is not null)
            {
                var x = Math.Min(_marqueeStartX, p.X);
                var y = Math.Min(_marqueeStartY, p.Y);
                Canvas.SetLeft(_marqueeRect, x);
                Canvas.SetTop(_marqueeRect, y);
                _marqueeRect.Width = Math.Abs(p.X - _marqueeStartX);
                _marqueeRect.Height = Math.Abs(p.Y - _marqueeStartY);
                return;
            }
            if (_isDraggingShape && _moveStartShape is not null)
            {
                var dx = p.X - _moveAnchorX;
                var dy = p.Y - _moveAnchorY;
                MoveSelectedShapesBy(dx, dy);
            }
            return;
        }

        var pos = e.GetPosition(DrawingCanvas);
        var (cx, cy) = ApplyShiftConstraint(pos.X, pos.Y);
        _vm.UpdateGesture(cx, cy);
    }

    /// <summary>Translate every selected shape by (dx, dy) relative to the move-anchor's recorded snapshot.
    /// We track per-selected-shape "start" snapshots in <see cref="_moveStartSnapshots"/>.</summary>
    private void MoveSelectedShapesBy(double dx, double dy)
    {
        if (_moveStartSnapshots is null) BuildMoveStartSnapshots();
        if (_moveStartSnapshots is null) return;

        // Build a parallel list of new shapes and apply via LiveReplaceShape.
        // Iterate by snapshot, find current incarnation (may differ from snapshot if previously replaced).
        var currentList = _vm.SelectedShapes.ToList();
        for (var i = 0; i < _moveStartSnapshots.Count && i < currentList.Count; i++)
        {
            var snapshot = _moveStartSnapshots[i];
            var translated = TranslateShape(snapshot, dx, dy);
            var currentIncarnation = currentList[i];
            _vm.LiveReplaceShape(currentIncarnation, translated);
        }
    }

    private void BuildMoveStartSnapshots()
    {
        _moveStartSnapshots = [.. _vm.SelectedShapes];
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_vm.CurrentTool == EditorTool.Select)
        {
            if (_isMarqueeing && _marqueeRect is not null)
            {
                var p = e.GetPosition(DrawingCanvas);
                var x = Math.Min(_marqueeStartX, p.X);
                var y = Math.Min(_marqueeStartY, p.Y);
                var w = Math.Abs(p.X - _marqueeStartX);
                var h = Math.Abs(p.Y - _marqueeStartY);

                DrawingCanvas.Children.Remove(_marqueeRect);
                _marqueeRect = null;
                _isMarqueeing = false;
                DrawingCanvas.ReleaseMouseCapture();

                if (w >= 3 && h >= 3)
                {
                    var picked = _vm.Shapes
                        .Where(s => MarqueeIntersects(s, x, y, w, h))
                        .ToList();
                    _vm.SetSelection(picked);
                }
            }
            else if (_isDraggingShape)
            {
                DrawingCanvas.ReleaseMouseCapture();
                _isDraggingShape = false;
                _moveStartShape = null;
                _moveStartSnapshots = null;
                // Move's commit happens via OnSelectedShapeChanged when the user changes selection
                // or via Closing → CommitPendingLiveEdit.
            }
            return;
        }

        DrawingCanvas.ReleaseMouseCapture();
        var pos = e.GetPosition(DrawingCanvas);
        var (cx, cy) = ApplyShiftConstraint(pos.X, pos.Y);
        _vm.CommitGesture(cx, cy);
    }

    private static bool MarqueeIntersects(Shape s, double mx, double my, double mw, double mh)
    {
        var b = ComputeBounds(s);
        return !(b.X + b.Width < mx || mx + mw < b.X || b.Y + b.Height < my || my + mh < b.Y);
    }

    private static Shape TranslateShape(Shape s, double dx, double dy) => s switch
    {
        RectangleShape r => r with { X = r.X + dx, Y = r.Y + dy },
        EllipseShape e => e with { X = e.X + dx, Y = e.Y + dy },
        ArrowShape a => a with { FromX = a.FromX + dx, FromY = a.FromY + dy, ToX = a.ToX + dx, ToY = a.ToY + dy },
        LineShape l => l with { FromX = l.FromX + dx, FromY = l.FromY + dy, ToX = l.ToX + dx, ToY = l.ToY + dy },
        FreehandShape f => f with { Points = f.Points.Select(p => (p.X + dx, p.Y + dy)).ToList() },
        _ => s
    };

    private void OnSelectedShapeChanged()
    {
        // Commit any pending live edit BEFORE swapping the original.
        CommitPendingLiveEdit();
        _liveEditOriginal = _vm.SelectedShape;
        RefreshPropertyPanel();
    }

    private void CommitPendingLiveEdit()
    {
        if (_liveEditOriginal is null) return;
        var current = _vm.SelectedShape;
        // If the selection was edited (current differs from original) AND current is still in Shapes,
        // push a single ReplaceShapeCommand spanning the whole gesture.
        if (current is not null && !ReferenceEquals(_liveEditOriginal, current) && _vm.Shapes.Contains(current))
        {
            _vm.CommitLiveEdit(_liveEditOriginal, current);
        }
        _liveEditOriginal = null;
    }

    private void OnSelOutlineChanged()
    {
        if (_suppressLiveUpdates) return;
        var sel = _vm.SelectedShape;
        if (sel is null) return;
        var newShape = ApplyOutlineColor(sel, SelOutlineSwatch.SelectedColor);
        _vm.LiveReplaceShape(sel, newShape);
    }

    private void OnSelFillChanged()
    {
        if (_suppressLiveUpdates) return;
        var sel = _vm.SelectedShape;
        if (sel is null) return;
        var newShape = ApplyFillColor(sel, SelFillSwatch.SelectedColor);
        _vm.LiveReplaceShape(sel, newShape);
    }

    private void OnSelStrokeChanged()
    {
        if (_suppressLiveUpdates) return;
        var sel = _vm.SelectedShape;
        if (sel is null) return;
        var newShape = ApplyStrokeWidth(sel, SelStrokeSlider.Value);
        _vm.LiveReplaceShape(sel, newShape);
    }

    private static Shape ApplyOutlineColor(Shape s, ShapeColor c) => s switch
    {
        RectangleShape r => r with { Outline = c },
        EllipseShape e => e with { Outline = c },
        ArrowShape a => a with { Outline = c },
        LineShape l => l with { Outline = c },
        FreehandShape f => f with { Outline = c },
        _ => s
    };

    private static Shape ApplyFillColor(Shape s, ShapeColor c) => s switch
    {
        RectangleShape r => r with { Fill = c },
        EllipseShape e => e with { Fill = c },
        // Arrow/Line/Freehand have no fill semantics; ignore.
        _ => s
    };

    private static Shape ApplyStrokeWidth(Shape s, double w) => s switch
    {
        RectangleShape r => r with { StrokeWidth = w },
        EllipseShape e => e with { StrokeWidth = w },
        ArrowShape a => a with { StrokeWidth = w },
        LineShape l => l with { StrokeWidth = w },
        FreehandShape f => f with { StrokeWidth = w },
        _ => s
    };

    private (double X, double Y) ApplyShiftConstraint(double x, double y)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift) return (x, y);

        var dx = x - _gestureStartX;
        var dy = y - _gestureStartY;

        switch (_vm.CurrentTool)
        {
            case EditorTool.Rectangle:
            case EditorTool.Ellipse:
            {
                // Constrain to square / circle (equal width and height).
                var size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                var sx = Math.Sign(dx) * size;
                var sy = Math.Sign(dy) * size;
                if (sx == 0) sx = size;
                if (sy == 0) sy = size;
                return (_gestureStartX + sx, _gestureStartY + sy);
            }
            case EditorTool.Line:
            case EditorTool.Arrow:
            {
                // Snap to nearest multiple of 45° (8 angles).
                var angle = Math.Atan2(dy, dx);
                var snapped = Math.Round(angle / (Math.PI / 4)) * (Math.PI / 4);
                var len = Math.Sqrt(dx * dx + dy * dy);
                return (_gestureStartX + len * Math.Cos(snapped), _gestureStartY + len * Math.Sin(snapped));
            }
            default:
                return (x, y);
        }
    }

    private void OnShapesChanged(object? sender, NotifyCollectionChangedEventArgs e) => RedrawAll();

    private void RedrawAll()
    {
        DrawingCanvas.Children.Clear();
        foreach (var shape in _vm.Shapes)
        {
            DrawingCanvas.Children.Add(MakeUiElement(shape));
        }
        RedrawPreview();
        RefreshSelectionAdorner();
    }

    private void RedrawPreview()
    {
        for (var i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (DrawingCanvas.Children[i] is FrameworkElement fe && (fe.Tag as string) == "preview")
            {
                DrawingCanvas.Children.RemoveAt(i);
            }
        }
        if (_vm.PreviewShape is not null)
        {
            var ui = MakeUiElement(_vm.PreviewShape);
            if (ui is FrameworkElement fe) fe.Tag = "preview";
            DrawingCanvas.Children.Add(ui);
        }
    }

    private void RefreshSelectionAdorner()
    {
        for (var i = DrawingCanvas.Children.Count - 1; i >= 0; i--)
        {
            if (DrawingCanvas.Children[i] is FrameworkElement fe && (fe.Tag as string) == "adorner")
                DrawingCanvas.Children.RemoveAt(i);
        }

        foreach (var s in _vm.SelectedShapes)
        {
            var b = ComputeBounds(s);
            var box = new System.Windows.Shapes.Rectangle
            {
                Width = b.Width + 8, Height = b.Height + 8,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 80, 200, 255)),
                StrokeThickness = 1.5,
                StrokeDashArray = [4.0, 3.0],
                Tag = "adorner",
                IsHitTestVisible = false
            };
            Canvas.SetLeft(box, b.X - 4);
            Canvas.SetTop(box, b.Y - 4);
            DrawingCanvas.Children.Add(box);
        }
    }

    private static (double X, double Y, double Width, double Height) ComputeBounds(Shape shape) => shape switch
    {
        RectangleShape r => (r.X, r.Y, r.Width, r.Height),
        EllipseShape e => (e.X, e.Y, e.Width, e.Height),
        ArrowShape a => (Math.Min(a.FromX, a.ToX), Math.Min(a.FromY, a.ToY), Math.Abs(a.ToX - a.FromX), Math.Abs(a.ToY - a.FromY)),
        LineShape l => (Math.Min(l.FromX, l.ToX), Math.Min(l.FromY, l.ToY), Math.Abs(l.ToX - l.FromX), Math.Abs(l.ToY - l.FromY)),
        FreehandShape f => FreehandBounds(f),
        _ => (0, 0, 0, 0)
    };

    private static (double X, double Y, double Width, double Height) FreehandBounds(FreehandShape f)
    {
        if (f.Points.Count == 0) return (0, 0, 0, 0);
        var minX = f.Points.Min(p => p.X);
        var minY = f.Points.Min(p => p.Y);
        var maxX = f.Points.Max(p => p.X);
        var maxY = f.Points.Max(p => p.Y);
        return (minX, minY, maxX - minX, maxY - minY);
    }

    private static UIElement MakeUiElement(Shape shape)
    {
        UIElement ui = shape switch
        {
            RectangleShape r => CreateRectangle(r),
            EllipseShape e => CreateEllipse(e),
            ArrowShape a => CreateArrow(a),
            LineShape l => CreateLine(l),
            FreehandShape f => CreateFreehand(f),
            _ => throw new NotSupportedException($"Unknown shape kind: {shape.GetType().Name}")
        };
        // Drawn shapes never intercept clicks: hit-testing happens geometrically via ShapeHitTester.
        ui.IsHitTestVisible = false;
        return ui;
    }

    private static UIElement CreateRectangle(RectangleShape r)
    {
        var rect = new System.Windows.Shapes.Rectangle
        {
            Width = r.Width,
            Height = r.Height,
            Stroke = ToBrush(r.Outline),
            StrokeThickness = r.StrokeWidth,
            Fill = ToBrush(r.Fill)
        };
        Canvas.SetLeft(rect, r.X);
        Canvas.SetTop(rect, r.Y);
        return rect;
    }

    private static UIElement CreateEllipse(EllipseShape e)
    {
        var ellipse = new System.Windows.Shapes.Ellipse
        {
            Width = e.Width,
            Height = e.Height,
            Stroke = ToBrush(e.Outline),
            StrokeThickness = e.StrokeWidth,
            Fill = ToBrush(e.Fill)
        };
        Canvas.SetLeft(ellipse, e.X);
        Canvas.SetTop(ellipse, e.Y);
        return ellipse;
    }

    private static UIElement CreateLine(LineShape l)
    {
        return new System.Windows.Shapes.Line
        {
            X1 = l.FromX, Y1 = l.FromY, X2 = l.ToX, Y2 = l.ToY,
            Stroke = ToBrush(l.Outline),
            StrokeThickness = l.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
    }

    private static UIElement CreateArrow(ArrowShape a)
    {
        var dx = a.ToX - a.FromX;
        var dy = a.ToY - a.FromY;
        var len = Math.Max(Math.Sqrt(dx * dx + dy * dy), 1);
        var ux = dx / len; var uy = dy / len;
        var headSize = Math.Max(8, a.StrokeWidth * 4);
        var leftX = a.ToX - ux * headSize - uy * (headSize / 2);
        var leftY = a.ToY - uy * headSize + ux * (headSize / 2);
        var rightX = a.ToX - ux * headSize + uy * (headSize / 2);
        var rightY = a.ToY - uy * headSize - ux * (headSize / 2);

        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(a.FromX, a.FromY) };
        fig.Segments.Add(new LineSegment(new Point(a.ToX, a.ToY), true));
        fig.Segments.Add(new LineSegment(new Point(leftX, leftY), true));
        fig.Segments.Add(new LineSegment(new Point(a.ToX, a.ToY), true));
        fig.Segments.Add(new LineSegment(new Point(rightX, rightY), true));
        geom.Figures.Add(fig);

        return new System.Windows.Shapes.Path
        {
            Data = geom,
            Stroke = ToBrush(a.Outline),
            StrokeThickness = a.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
    }

    private static UIElement CreateFreehand(FreehandShape f)
    {
        var poly = new System.Windows.Shapes.Polyline
        {
            Stroke = ToBrush(f.Outline),
            StrokeThickness = f.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        foreach (var (x, y) in f.Points) poly.Points.Add(new Point(x, y));
        return poly;
    }

    private static SolidColorBrush ToBrush(ShapeColor c)
        => c.IsTransparent
            ? (SolidColorBrush)System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));

    private void RefreshPropertyPanel()
    {
        var sel = _vm.SelectedShape;
        if (sel is null || _vm.SelectedShapes.Count > 1)
        {
            // No selection or multi-selection: property editing disabled in M3b.
            NoSelectionText.Visibility = Visibility.Visible;
            NoSelectionText.Text = _vm.SelectedShapes.Count > 1
                ? $"{_vm.SelectedShapes.Count} shapes selected. Single-shape editing only for now."
                : "Select a shape to edit its properties (V tool, then click).";
            SelectedShapeStack.Visibility = Visibility.Collapsed;
            RefreshSelectionAdorner();
            return;
        }
        NoSelectionText.Visibility = Visibility.Collapsed;
        SelectedShapeStack.Visibility = Visibility.Visible;

        // Suppress live-update events while we populate the panel from the selected shape's values
        // (otherwise setting the slider would fire ValueChanged → another LiveReplaceShape → infinite loop).
        _suppressLiveUpdates = true;
        try
        {
            SelectedShapeKindText.Text = sel.GetType().Name.Replace("Shape", "");
            SelOutlineSwatch.SelectedColor = sel.Outline;
            SelFillSwatch.SelectedColor = sel.Fill;
            SelStrokeSlider.Value = sel.StrokeWidth;
        }
        finally
        {
            _suppressLiveUpdates = false;
        }
        RefreshSelectionAdorner();
    }
}
