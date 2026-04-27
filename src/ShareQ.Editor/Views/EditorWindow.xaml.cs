using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShareQ.Editor.Adorners;
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

    // Live-edit tracking: per-shape original snapshots, so a sequence of property tweaks (or drag-to-move)
    // commits as ONE undo step per shape when the selection changes (or window closes).
    private List<Shape> _liveEditOriginals = [];
    private bool _suppressLiveUpdates;

    // Inline text editing: a TextBox is parented to the canvas at the click point.
    // _editingTextShape is non-null only when modifying an existing TextShape.
    private System.Windows.Controls.TextBox? _activeTextBox;
    private TextShape? _editingTextShape;

    // Grip drag state (resize / endpoint / font-size). Only active for single-selection in Select tool.
    private GripKind _activeGrip = GripKind.None;
    private Shape? _gripStartShape;
    private double _gripAnchorX, _gripAnchorY;

    // Eyedropper state. While non-null, the next canvas click samples a pixel and feeds it back.
    private Action<ShapeColor?>? _eyedropperContinuation;

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
            if (e.PropertyName == nameof(EditorViewModel.CurrentTool)) { CommitInlineTextEdit(); RefreshToolButtonHighlight(); RefreshPropertyPanel(); }
        };
        _vm.SelectedShapes.CollectionChanged += (_, e) =>
        {
            RedrawAll();
            // Re-snapshot live-edit originals only on real selection changes (Add/Remove/Reset),
            // NOT on Replace (which fires during LiveReplaceShape).
            if (e.Action != System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
            {
                OnSelectionSetChanged();
            }
        };

        // Realtime property panel: each change live-updates the shape via DependencyPropertyDescriptor hooks.
        var swatchColorDesc = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            ColorSwatchButton.SelectedColorProperty, typeof(ColorSwatchButton));
        swatchColorDesc?.AddValueChanged(SelOutlineSwatch, (_, _) => OnSelOutlineChanged());
        swatchColorDesc?.AddValueChanged(SelFillSwatch, (_, _) => OnSelFillChanged());
        SelStrokeSlider.ValueChanged += (_, _) => OnSelStrokeChanged();
        SelRotationSlider.ValueChanged += (_, _) => OnSelRotationSliderChanged();
        SelRotationBox.LostFocus += (_, _) => OnSelRotationBoxCommitted();
        SelRotationBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnSelRotationBoxCommitted(); };

        // Populate font combo with all system fonts (sorted alphabetically) and wrap in a CollectionView
        // so we can apply a substring (not just prefix) filter when the user types into the editable combo.
        var systemFonts = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        SelFontFamilyCombo.ItemsSource = systemFonts;
        WireFontFilter(SelFontFamilyCombo, systemFonts);

        SelFontFamilyCombo.SelectionChanged += (_, _) => OnSelTextStyleChanged();
        SelFontSizeSlider.ValueChanged += (_, _) => OnSelFontSizeSliderChanged();
        SelFontSizeBox.LostFocus += (_, _) => OnSelFontSizeBoxCommitted();
        SelFontSizeBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnSelFontSizeBoxCommitted(); };
        SelBoldCheck.Checked += (_, _) => OnSelTextStyleChanged();
        SelBoldCheck.Unchecked += (_, _) => OnSelTextStyleChanged();
        SelItalicCheck.Checked += (_, _) => OnSelTextStyleChanged();
        SelItalicCheck.Unchecked += (_, _) => OnSelTextStyleChanged();

        var swatchColorDescAlt = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            ColorSwatchButton.SelectedColorProperty, typeof(ColorSwatchButton));
        swatchColorDescAlt?.AddValueChanged(SelTextColorSwatch, (_, _) => OnSelTextStyleChanged());

        Loaded += (_, _) =>
        {
            LoadSourceImage();
            RefreshPropertyPanel();
            RefreshToolButtonHighlight();
            ColorSwatchButton.EyedropperHandler = continuation =>
            {
                EnterCanvasEyedropperMode(continuation);
                return null;
            };
        };
        Closing += (_, _) =>
        {
            CommitInlineTextEdit();
            CommitPendingLiveEdit();
            ColorSwatchButton.EyedropperHandler = null;
            CancelEyedropper();
        };
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
    private void OnTextToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Text;
    private void OnStepToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.StepCounter;

    private void RefreshToolButtonHighlight()
    {
        var buttons = new[]
        {
            (Btn: SelectToolBtn, Tool: EditorTool.Select),
            (Btn: RectangleToolBtn, Tool: EditorTool.Rectangle),
            (Btn: ArrowToolBtn, Tool: EditorTool.Arrow),
            (Btn: LineToolBtn, Tool: EditorTool.Line),
            (Btn: EllipseToolBtn, Tool: EditorTool.Ellipse),
            (Btn: FreehandToolBtn, Tool: EditorTool.Freehand),
            (Btn: TextToolBtn, Tool: EditorTool.Text),
            (Btn: StepToolBtn, Tool: EditorTool.StepCounter)
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
        // Grips are sized in screen pixels via inverse zoom; refresh so the new factor applies.
        RefreshSelectionAdorner();
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _eyedropperContinuation is not null) { CancelEyedropper(); e.Handled = true; return; }
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
            case Key.T: _vm.CurrentTool = EditorTool.Text; e.Handled = true; break;
            case Key.N: _vm.CurrentTool = EditorTool.StepCounter; e.Handled = true; break;
            default: break;
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        // We listen on the ScrollViewer so clicks outside the bitmap area still hit shapes
        // (DrawingCanvas with Transparent Background only hit-tests within Width×Height).
        // Filter out clicks on scrollbar parts and on the inline TextBox editor.
        if (IsScrollBarOrInputControl(e.OriginalSource as DependencyObject)) return;
        var p = e.GetPosition(DrawingCanvas);

        if (_eyedropperContinuation is not null)
        {
            var sampled = SamplePixelAt(p.X, p.Y);
            FinishEyedropper(sampled);
            e.Handled = true;
            return;
        }

        if (_vm.CurrentTool == EditorTool.Text)
        {
            BeginInlineTextEdit(p.X, p.Y, existing: null);
            e.Handled = true;
            return;
        }

        if (_vm.CurrentTool == EditorTool.Select)
        {
            var shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;

            // Grip hit-test takes priority over shape hit-test, but only on single-selection.
            if (_vm.SelectedShapes.Count == 1)
            {
                var sel = _vm.SelectedShapes[0];
                var rotOffset = sel is RectangleShape or EllipseShape or TextShape ? 25.0 / _zoom : 0;
                var grip = ShapeGripLayout.HitTest(sel, p.X, p.Y, ShapeGripLayout.DefaultHitTolerance / _zoom, rotOffset);
                if (grip != GripKind.None)
                {
                    _activeGrip = grip;
                    _gripStartShape = sel;
                    _gripAnchorX = p.X;
                    _gripAnchorY = p.Y;
                    DrawingCanvas.CaptureMouse();
                    e.Handled = true;
                    return;
                }
            }

            var hit = ShapeHitTester.HitTest(_vm.Shapes, p.X, p.Y);
            if (hit is not null)
            {
                if (e.ClickCount == 2 && hit is TextShape ts)
                {
                    BeginInlineTextEdit(ts.X, ts.Y, existing: ts);
                    e.Handled = true;
                    return;
                }
                if (shiftPressed)
                {
                    // Shift+Click: toggle in selection set. No drag.
                    CommitPendingLiveEdit();
                    var current = _vm.SelectedShapes.ToList();
                    if (!current.Remove(hit)) current.Add(hit);
                    _vm.SetSelection(current);
                    return;
                }

                if (!_vm.SelectedShapes.Contains(hit))
                {
                    CommitPendingLiveEdit();
                    _vm.SetSelection([hit]);
                }
                _moveStartShape = hit;
                _moveAnchorX = p.X;
                _moveAnchorY = p.Y;
                _isDraggingShape = true;
                _moveStartSnapshots = null; // built lazily on first move tick
                DrawingCanvas.CaptureMouse();
            }
            else
            {
                // Click on empty area. Without Shift: clear selection then start marquee.
                // With Shift: keep current selection; marquee will add to it on release.
                if (!shiftPressed)
                {
                    CommitPendingLiveEdit();
                    _vm.SetSelection([]);
                }
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
        // Cursor feedback on grip hover (independent of mouse button state).
        UpdateCursorForGripHover(e.GetPosition(DrawingCanvas));

        if (e.LeftButton != MouseButtonState.Pressed) return;

        if (_vm.CurrentTool == EditorTool.Select)
        {
            var p = e.GetPosition(DrawingCanvas);
            if (_activeGrip != GripKind.None && _gripStartShape is not null)
            {
                ApplyGripDrag(p.X, p.Y);
                return;
            }
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

    private void EnterCanvasEyedropperMode(Action<ShapeColor?> continuation)
    {
        _eyedropperContinuation = continuation;
        DrawingCanvas.Cursor = Cursors.Cross;
        // Window stays focused so Esc cancels.
        Focus();
    }

    private void FinishEyedropper(ShapeColor? sampled)
    {
        var cont = _eyedropperContinuation;
        _eyedropperContinuation = null;
        DrawingCanvas.Cursor = null;
        cont?.Invoke(sampled);
    }

    private void CancelEyedropper() => FinishEyedropper(null);

    /// <summary>Sample the pixel at (x, y) of the source bitmap. Returns null when out of bounds
    /// or the source isn't loaded yet.</summary>
    private ShapeColor? SamplePixelAt(double x, double y)
    {
        if (SourceImage.Source is not BitmapSource src) return null;
        var ix = (int)Math.Floor(x);
        var iy = (int)Math.Floor(y);
        if (ix < 0 || iy < 0 || ix >= src.PixelWidth || iy >= src.PixelHeight) return null;

        // Force conversion to a uniform 32-bit BGRA so we can read 4 bytes regardless of source format.
        var conv = new System.Windows.Media.Imaging.FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var px = new byte[4];
        conv.CopyPixels(new Int32Rect(ix, iy, 1, 1), px, 4, 0);
        return new ShapeColor(px[3], px[2], px[1], px[0]);
    }

    /// <summary>True when the originating element is a scrollbar part or a text input — don't treat
    /// such clicks as canvas gestures.</summary>
    private static bool IsScrollBarOrInputControl(DependencyObject? d)
    {
        while (d is not null)
        {
            if (d is System.Windows.Controls.Primitives.ScrollBar) return true;
            if (d is System.Windows.Controls.TextBox tb && (tb.Tag as string) == "inline-text-editor") return true;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return false;
    }

    private void UpdateCursorForGripHover(System.Windows.Point p)
    {
        if (_vm.CurrentTool != EditorTool.Select || _vm.SelectedShapes.Count != 1)
        {
            if (DrawingCanvas.Cursor != null) DrawingCanvas.Cursor = null;
            return;
        }
        var sel = _vm.SelectedShapes[0];
        var rotOffset = sel is RectangleShape or EllipseShape or TextShape ? 25.0 / _zoom : 0;
        var grip = ShapeGripLayout.HitTest(sel, p.X, p.Y, ShapeGripLayout.DefaultHitTolerance / _zoom, rotOffset);
        Cursor? c = grip switch
        {
            GripKind.TopLeft or GripKind.BottomRight or GripKind.Resize => Cursors.SizeNWSE,
            GripKind.TopRight or GripKind.BottomLeft => Cursors.SizeNESW,
            GripKind.Top or GripKind.Bottom => Cursors.SizeNS,
            GripKind.Left or GripKind.Right => Cursors.SizeWE,
            GripKind.From or GripKind.To => Cursors.Hand,
            GripKind.Rotate => Cursors.Cross,
            _ => null
        };
        if (DrawingCanvas.Cursor != c) DrawingCanvas.Cursor = c;
    }

    /// <summary>Apply a grip drag by computing a new shape from the original snapshot and current pointer.
    /// We always recompute from <see cref="_gripStartShape"/> (not the previous tick) so the result is
    /// monotonic in the drag distance — no cumulative drift.</summary>
    private void ApplyGripDrag(double x, double y)
    {
        if (_gripStartShape is null || _vm.SelectedShapes.Count == 0) return;
        var current = _vm.SelectedShapes[0];
        var shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
        var updated = GripDrag.Transform(_gripStartShape, _activeGrip, x, y, shiftPressed);
        if (updated is null || ReferenceEquals(updated, current)) return;
        _vm.LiveReplaceShape(current, updated);
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
            if (_activeGrip != GripKind.None)
            {
                _activeGrip = GripKind.None;
                _gripStartShape = null;
                DrawingCanvas.ReleaseMouseCapture();
                // Grip drag commit happens via OnSelectionSetChanged → CommitPendingLiveEdit
                // when selection changes, or via Closing.
                return;
            }
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
                    var shiftPressed = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
                    var picked = _vm.Shapes
                        .Where(s => MarqueeIntersects(s, x, y, w, h))
                        .ToList();
                    if (shiftPressed)
                    {
                        // Add to existing selection (deduplicating).
                        var union = _vm.SelectedShapes.ToList();
                        foreach (var s in picked) if (!union.Contains(s)) union.Add(s);
                        _vm.SetSelection(union);
                    }
                    else
                    {
                        _vm.SetSelection(picked);
                    }
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
        TextShape t => t with { X = t.X + dx, Y = t.Y + dy },
        StepCounterShape c => c with { CenterX = c.CenterX + dx, CenterY = c.CenterY + dy },
        _ => s
    };

    private void OnSelectedShapeChanged()
    {
        RefreshPropertyPanel();
    }

    private void OnSelectionSetChanged()
    {
        CommitPendingLiveEdit();
        _liveEditOriginals = [.. _vm.SelectedShapes];
        RefreshPropertyPanel();
    }

    private void CommitPendingLiveEdit()
    {
        if (_liveEditOriginals.Count == 0) return;
        var current = _vm.SelectedShapes.ToList();
        for (var i = 0; i < _liveEditOriginals.Count && i < current.Count; i++)
        {
            var orig = _liveEditOriginals[i];
            var cur = current[i];
            if (!ReferenceEquals(orig, cur) && _vm.Shapes.Contains(cur))
            {
                _vm.CommitLiveEdit(orig, cur);
            }
        }
        _liveEditOriginals = [];
    }

    private void OnSelOutlineChanged()
    {
        if (_suppressLiveUpdates) return;
        var color = SelOutlineSwatch.SelectedColor;
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            _vm.LiveReplaceShape(s, ApplyOutlineColor(s, color));
        }
    }

    private void OnSelFillChanged()
    {
        if (_suppressLiveUpdates) return;
        var color = SelFillSwatch.SelectedColor;
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            _vm.LiveReplaceShape(s, ApplyFillColor(s, color));
        }
    }

    private void OnSelStrokeChanged()
    {
        if (_suppressLiveUpdates) return;
        var width = SelStrokeSlider.Value;
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            _vm.LiveReplaceShape(s, ApplyStrokeWidth(s, width));
        }
    }

    private void OnSelRotationSliderChanged()
    {
        if (_suppressLiveUpdates) return;
        var deg = SelRotationSlider.Value;
        SelRotationBox.Text = ((int)Math.Round(deg)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyRotation(deg);
    }

    private void OnSelRotationBoxCommitted()
    {
        if (_suppressLiveUpdates) return;
        if (!double.TryParse(SelRotationBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            SelRotationBox.Text = ((int)Math.Round(SelRotationSlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        v = ((v + 180) % 360 + 360) % 360 - 180;
        _suppressLiveUpdates = true;
        try { SelRotationSlider.Value = v; }
        finally { _suppressLiveUpdates = false; }
        ApplyRotation(v);
    }

    private void ApplyRotation(double deg)
    {
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            Shape? updated = s switch
            {
                RectangleShape r => r with { Rotation = deg },
                EllipseShape e => e with { Rotation = deg },
                TextShape t => t with { Rotation = deg },
                _ => null
            };
            if (updated is not null) _vm.LiveReplaceShape(s, updated);
        }
    }

    private void BeginInlineTextEdit(double x, double y, TextShape? existing)
    {
        CommitInlineTextEdit();

        var style = existing?.Style ?? _vm.CurrentTextStyle;
        var initialText = existing?.Text ?? "";

        _editingTextShape = existing;
        _activeTextBox = new System.Windows.Controls.TextBox
        {
            Text = initialText,
            FontFamily = new FontFamily(style.FontFamily),
            FontSize = style.FontSize,
            FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = ToBrush(style.Color),
            TextAlignment = ToTextAlignment(style.Align),
            Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(180, 80, 200, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
            MinWidth = 60,
            AcceptsReturn = true,
            AcceptsTab = false,
            Tag = "inline-text-editor"
        };
        Canvas.SetLeft(_activeTextBox, x);
        Canvas.SetTop(_activeTextBox, y);
        if (existing is { Rotation: var rot } && rot != 0)
        {
            var existingBounds = TextBounds(existing);
            _activeTextBox.RenderTransform = new RotateTransform(rot, existingBounds.Width / 2, existingBounds.Height / 2);
        }
        DrawingCanvas.Children.Add(_activeTextBox);
        _activeTextBox.KeyDown += OnInlineTextBoxKeyDown;
        var tb = _activeTextBox;
        tb.Loaded += (_, _) =>
        {
            // Wire LostFocus only after focus has been acquired, otherwise the very first
            // focus arrival from Loaded fires LostFocus immediately and commits-empty.
            tb.Focus();
            Keyboard.Focus(tb);
            tb.SelectAll();
            tb.LostFocus += (_, _) => CommitInlineTextEdit();
        };

        if (existing is not null) RedrawAll();
    }

    private void OnInlineTextBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CancelInlineTextEdit(); e.Handled = true; return; }
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) != ModifierKeys.Shift)
        {
            CommitInlineTextEdit();
            e.Handled = true;
        }
    }

    private void CommitInlineTextEdit()
    {
        if (_activeTextBox is null) return;
        var text = _activeTextBox.Text;
        var x = Canvas.GetLeft(_activeTextBox);
        var y = Canvas.GetTop(_activeTextBox);

        DrawingCanvas.Children.Remove(_activeTextBox);
        var existing = _editingTextShape;
        var style = existing?.Style ?? _vm.CurrentTextStyle;
        _activeTextBox = null;
        _editingTextShape = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (existing is not null) _vm.RemoveShapes([existing]);
            return;
        }

        // The TextShape's Outline is unused for rendering (foreground comes from Style.Color);
        // we still set it for hit-testing parity with other shapes. Preserve rotation on edit.
        var rotation = existing?.Rotation ?? 0;
        var shape = new TextShape(x, y, text, style, style.Color, ShapeColor.Transparent, 1, rotation);
        if (existing is null)
        {
            _vm.AddTextShape(shape);
            // Auto-select the new text WITHOUT switching tool — symmetric to drawing tools
            // (CommitGesture does the same). The user can immediately tweak font/color/size/align
            // from the panel and a fresh click on the canvas creates another text.
            _vm.SetSelection([shape]);
        }
        else
        {
            _vm.ApplyShapeEdit(existing, shape);
        }
    }

    private void CancelInlineTextEdit()
    {
        if (_activeTextBox is null) return;
        DrawingCanvas.Children.Remove(_activeTextBox);
        _activeTextBox = null;
        _editingTextShape = null;
        RedrawAll();
    }

    private double _currentTextSize = TextStyle.Default.FontSize;

    private void OnSelFontSizeSliderChanged()
    {
        if (_suppressLiveUpdates) return;
        _currentTextSize = SelFontSizeSlider.Value;
        SelFontSizeBox.Text = ((int)Math.Round(_currentTextSize)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        OnSelTextStyleChanged();
    }

    private void OnSelFontSizeBoxCommitted()
    {
        if (_suppressLiveUpdates) return;
        if (!double.TryParse(SelFontSizeBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            SelFontSizeBox.Text = ((int)Math.Round(_currentTextSize)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        v = Math.Max(1, v);
        _currentTextSize = v;
        // Update slider only if value fits its bounds; otherwise let the slider sit at its max.
        _suppressLiveUpdates = true;
        try { SelFontSizeSlider.Value = Math.Clamp(v, SelFontSizeSlider.Minimum, SelFontSizeSlider.Maximum); }
        finally { _suppressLiveUpdates = false; }
        OnSelTextStyleChanged();
    }

    private void OnSelAlignLeftClicked(object sender, RoutedEventArgs e) => SetTextAlign(TextAlign.Left);
    private void OnSelAlignCenterClicked(object sender, RoutedEventArgs e) => SetTextAlign(TextAlign.Center);
    private void OnSelAlignRightClicked(object sender, RoutedEventArgs e) => SetTextAlign(TextAlign.Right);

    private void SetTextAlign(TextAlign a)
    {
        if (_suppressLiveUpdates) return;
        ApplyTextStyle(a);
        RefreshAlignToggles(a);
    }

    private void RefreshAlignToggles(TextAlign a)
    {
        SelAlignLeftBtn.IsChecked = a == TextAlign.Left;
        SelAlignCenterBtn.IsChecked = a == TextAlign.Center;
        SelAlignRightBtn.IsChecked = a == TextAlign.Right;
    }

    private void OnSelTextStyleChanged() => ApplyTextStyle(null);

    /// <summary>Read every field of the text panel, build a TextStyle and push it to <see cref="EditorViewModel.CurrentTextStyle"/>
    /// and to all selected TextShapes. <paramref name="alignOverride"/> lets the alignment buttons short-circuit
    /// the read of the toggle buttons (which haven't toggled yet at click time).</summary>
    private void ApplyTextStyle(TextAlign? alignOverride)
    {
        if (_suppressLiveUpdates) return;
        var family = SelFontFamilyCombo.SelectedItem as string ?? "Segoe UI";
        var size = _currentTextSize;
        var bold = SelBoldCheck.IsChecked == true;
        var italic = SelItalicCheck.IsChecked == true;
        var color = SelTextColorSwatch.SelectedColor;
        var align = alignOverride ?? CurrentAlignFromToggles();
        var newStyle = new TextStyle(family, size, bold, italic, color, align);

        _vm.CurrentTextStyle = newStyle;

        foreach (var s in _vm.SelectedShapes.OfType<TextShape>().ToList())
        {
            _vm.LiveReplaceShape(s, s with { Style = newStyle });
        }
    }

    /// <summary>Hook a substring (case-insensitive) filter to the editable font ComboBox.
    /// WPF's built-in <c>IsTextSearchEnabled</c> only does prefix matching, which misses entries like
    /// "MS Gothic" when the user types "gothic". This wires the internal TextBox's TextChanged event
    /// to a <see cref="ICollectionView.Filter"/> on the ComboBox's items.</summary>
    private static void WireFontFilter(ComboBox combo, List<string> allFonts)
    {
        var view = System.Windows.Data.CollectionViewSource.GetDefaultView(allFonts);
        var currentFilter = "";
        view.Filter = obj =>
        {
            if (string.IsNullOrEmpty(currentFilter)) return true;
            return obj is string s && s.Contains(currentFilter, StringComparison.OrdinalIgnoreCase);
        };
        combo.ItemsSource = view;

        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) =>
            {
                var text = combo.Text ?? "";
                // Don't re-filter when the user has just committed a selection (text matches selected item exactly).
                if (combo.SelectedItem is string sel && string.Equals(sel, text, StringComparison.Ordinal))
                {
                    if (currentFilter.Length > 0)
                    {
                        currentFilter = "";
                        view.Refresh();
                    }
                    return;
                }
                currentFilter = text;
                view.Refresh();
                if (!string.IsNullOrEmpty(currentFilter)) combo.IsDropDownOpen = true;
            }));
    }

    private TextAlign CurrentAlignFromToggles()
    {
        if (SelAlignCenterBtn.IsChecked == true) return TextAlign.Center;
        if (SelAlignRightBtn.IsChecked == true) return TextAlign.Right;
        return TextAlign.Left;
    }

    private void ApplyDefaultsToTextSection()
    {
        _suppressLiveUpdates = true;
        try
        {
            var s = _vm.CurrentTextStyle;
            if (SelFontFamilyCombo.Items.Contains(s.FontFamily)) SelFontFamilyCombo.SelectedItem = s.FontFamily;
            _currentTextSize = s.FontSize;
            SelFontSizeSlider.Value = Math.Clamp(_currentTextSize, SelFontSizeSlider.Minimum, SelFontSizeSlider.Maximum);
            SelFontSizeBox.Text = ((int)Math.Round(_currentTextSize)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            SelBoldCheck.IsChecked = s.Bold;
            SelItalicCheck.IsChecked = s.Italic;
            SelTextColorSwatch.SelectedColor = s.Color;
            RefreshAlignToggles(s.Align);
        }
        finally { _suppressLiveUpdates = false; }
    }

    /// <summary>Apply the toolbar's current outline/fill/stroke to every selected shape in one gesture.
    /// The selection's live-edit snapshot was captured when the shapes were selected, so this
    /// commits as a single undo step per shape (same machinery as drag-to-move).</summary>
    private void OnApplyCurrentClicked(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedShapes.Count == 0) return;
        var outline = _vm.OutlineColor;
        var fill = _vm.FillColor;
        var stroke = _vm.StrokeWidth;

        foreach (var s in _vm.SelectedShapes.ToList())
        {
            var updated = ApplyStrokeWidth(ApplyFillColor(ApplyOutlineColor(s, outline), fill), stroke);
            _vm.LiveReplaceShape(s, updated);
        }
        RefreshPropertyPanel();
    }

    /// <summary>Adopt the selected shape's outline/fill/stroke as the toolbar's current values.
    /// On multi-selection, takes the first shape's values.</summary>
    private void OnSetAsCurrentClicked(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedShapes.Count == 0) return;
        var s = _vm.SelectedShapes[0];
        _vm.OutlineColor = s.Outline;
        _vm.FillColor = s.Fill;
        _vm.StrokeWidth = s.StrokeWidth;
    }

    private static Shape ApplyOutlineColor(Shape s, ShapeColor c) => s switch
    {
        RectangleShape r => r with { Outline = c },
        EllipseShape e => e with { Outline = c },
        ArrowShape a => a with { Outline = c },
        LineShape l => l with { Outline = c },
        FreehandShape f => f with { Outline = c },
        TextShape t => t with { Outline = c },
        StepCounterShape sc => sc with { Outline = c },
        _ => s
    };

    private static Shape ApplyFillColor(Shape s, ShapeColor c) => s switch
    {
        RectangleShape r => r with { Fill = c },
        EllipseShape e => e with { Fill = c },
        StepCounterShape sc => sc with { Fill = c },
        // Arrow/Line/Freehand/Text have no fill semantics; ignore.
        _ => s
    };

    private static bool ShapeSupportsFill(Shape s) => s is RectangleShape or EllipseShape or StepCounterShape;

    /// <summary>True for shapes that visually use the Outline color. TextShape uses Style.Color instead.</summary>
    private static bool ShapeSupportsOutline(Shape s) => s is not TextShape;

    /// <summary>True for shapes that visually use StrokeWidth. TextShape uses FontSize for sizing.</summary>
    private static bool ShapeSupportsStroke(Shape s) => s is not TextShape;

    private static Shape ApplyStrokeWidth(Shape s, double w) => s switch
    {
        RectangleShape r => r with { StrokeWidth = w },
        EllipseShape e => e with { StrokeWidth = w },
        ArrowShape a => a with { StrokeWidth = w },
        LineShape l => l with { StrokeWidth = w },
        FreehandShape f => f with { StrokeWidth = w },
        StepCounterShape sc => sc with { StrokeWidth = w },
        // TextShape uses Style.FontSize, not StrokeWidth — ignore.
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
            if (ReferenceEquals(shape, _editingTextShape)) continue;
            DrawingCanvas.Children.Add(MakeUiElement(shape));
        }
        RedrawPreview();
        RefreshSelectionAdorner();
        if (_activeTextBox is not null) DrawingCanvas.Children.Add(_activeTextBox);
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
            var rotation = ShapeGripLayout.RotationOf(s);
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
            if (rotation != 0)
            {
                // Pivot is the bbox center, expressed in the rectangle's local coords.
                box.RenderTransform = new RotateTransform(rotation, (b.Width + 8) / 2, (b.Height + 8) / 2);
            }
            DrawingCanvas.Children.Add(box);
        }

        // Edit grips only for single-selection in Select tool. Multi-select keeps just the dashed boxes.
        // Grips are sized in canvas units; we apply an inverse zoom transform so they keep a constant
        // 8×8-pixel screen footprint regardless of zoom level. For rotated shapes, the grip layout
        // is computed in local (non-rotated) coordinates, then a RotateTransform is applied to each
        // grip around the shape's pivot so they follow the rotation visually.
        if (_vm.CurrentTool == EditorTool.Select && _vm.SelectedShapes.Count == 1)
        {
            var sel = _vm.SelectedShapes[0];
            var inv = 1.0 / _zoom;
            var rotation = ShapeGripLayout.RotationOf(sel);
            var pivot = ShapeGripLayout.PivotOf(sel);
            var rotateOffset = sel is RectangleShape or EllipseShape or TextShape ? 25.0 / _zoom : 0;

            foreach (var g in ShapeGripLayout.GripsFor(sel, rotateOffset))
            {
                var isRotateGrip = g.Kind == GripKind.Rotate;
                var grip = new System.Windows.Shapes.Rectangle
                {
                    Width = 8, Height = 8,
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 80, 200, 255)),
                    StrokeThickness = 1.5,
                    Fill = isRotateGrip ? new SolidColorBrush(Color.FromArgb(255, 80, 200, 255)) : Brushes.White,
                    RadiusX = isRotateGrip ? 4 : 0,
                    RadiusY = isRotateGrip ? 4 : 0,
                    Tag = "adorner",
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(grip, g.X - 4);
                Canvas.SetTop(grip, g.Y - 4);
                var transforms = new TransformGroup();
                transforms.Children.Add(new ScaleTransform(inv, inv, 4, 4));
                if (rotation != 0)
                {
                    // RotateTransform is applied in the grip's local coords, so the pivot must be
                    // expressed relative to the grip's top-left (Canvas.SetLeft = g.X - 4).
                    transforms.Children.Add(new RotateTransform(rotation, pivot.X - (g.X - 4), pivot.Y - (g.Y - 4)));
                }
                grip.RenderTransform = transforms;
                DrawingCanvas.Children.Add(grip);
            }
        }
    }

    private static (double X, double Y, double Width, double Height) ComputeBounds(Shape shape) => shape switch
    {
        RectangleShape r => (r.X, r.Y, r.Width, r.Height),
        EllipseShape e => (e.X, e.Y, e.Width, e.Height),
        ArrowShape a => (Math.Min(a.FromX, a.ToX), Math.Min(a.FromY, a.ToY), Math.Abs(a.ToX - a.FromX), Math.Abs(a.ToY - a.FromY)),
        LineShape l => (Math.Min(l.FromX, l.ToX), Math.Min(l.FromY, l.ToY), Math.Abs(l.ToX - l.FromX), Math.Abs(l.ToY - l.FromY)),
        FreehandShape f => FreehandBounds(f),
        TextShape t => TextBounds(t),
        StepCounterShape c => (c.CenterX - c.Radius, c.CenterY - c.Radius, c.Radius * 2, c.Radius * 2),
        _ => (0, 0, 0, 0)
    };

    private static (double X, double Y, double Width, double Height) TextBounds(TextShape t)
    {
        var lines = t.Text.Length == 0 ? new[] { "" } : t.Text.Split('\n');
        var maxLen = 0;
        foreach (var line in lines) if (line.Length > maxLen) maxLen = line.Length;
        var w = Math.Max(8, maxLen * t.Style.FontSize * 0.55);
        var h = lines.Length * t.Style.FontSize * 1.2;
        return (t.X, t.Y, w, h);
    }

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
            TextShape t => CreateText(t),
            StepCounterShape c => CreateStepCounter(c),
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
        if (r.Rotation != 0)
        {
            // RotateTransform's center is in the element's own coordinate system (origin top-left),
            // so the geometric center is (Width/2, Height/2).
            rect.RenderTransform = new RotateTransform(r.Rotation, r.Width / 2, r.Height / 2);
        }
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
        if (e.Rotation != 0)
        {
            ellipse.RenderTransform = new RotateTransform(e.Rotation, e.Width / 2, e.Height / 2);
        }
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

    private static UIElement CreateText(TextShape t)
    {
        // Width must be explicitly set for TextAlignment to take effect on short lines.
        // Without Width, TextBlock auto-sizes to its content and Center/Right look identical to Left.
        var bounds = TextBounds(t);
        var tb = new System.Windows.Controls.TextBlock
        {
            Text = t.Text,
            FontFamily = new FontFamily(t.Style.FontFamily),
            FontSize = t.Style.FontSize,
            FontWeight = t.Style.Bold ? FontWeights.Bold : FontWeights.Normal,
            FontStyle = t.Style.Italic ? FontStyles.Italic : FontStyles.Normal,
            Foreground = ToBrush(t.Style.Color),
            TextAlignment = ToTextAlignment(t.Style.Align),
            Width = bounds.Width
        };
        Canvas.SetLeft(tb, t.X);
        Canvas.SetTop(tb, t.Y);
        if (t.Rotation != 0)
        {
            tb.RenderTransform = new RotateTransform(t.Rotation, bounds.Width / 2, bounds.Height / 2);
        }
        return tb;
    }

    private static System.Windows.TextAlignment ToTextAlignment(TextAlign a) => a switch
    {
        TextAlign.Center => System.Windows.TextAlignment.Center,
        TextAlign.Right => System.Windows.TextAlignment.Right,
        _ => System.Windows.TextAlignment.Left
    };

    private static UIElement CreateStepCounter(StepCounterShape c)
    {
        var grid = new Grid
        {
            Width = c.Radius * 2,
            Height = c.Radius * 2
        };
        var ellipse = new System.Windows.Shapes.Ellipse
        {
            Width = c.Radius * 2,
            Height = c.Radius * 2,
            Stroke = ToBrush(c.Outline),
            StrokeThickness = c.StrokeWidth,
            Fill = c.Fill.IsTransparent ? ToBrush(c.Outline) : ToBrush(c.Fill)
        };
        var text = new System.Windows.Controls.TextBlock
        {
            Text = c.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = Brushes.White,
            FontWeight = FontWeights.Bold,
            FontSize = c.Radius * 0.9,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        grid.Children.Add(ellipse);
        grid.Children.Add(text);
        Canvas.SetLeft(grid, c.CenterX - c.Radius);
        Canvas.SetTop(grid, c.CenterY - c.Radius);
        return grid;
    }

    private static SolidColorBrush ToBrush(ShapeColor c)
        => c.IsTransparent
            ? (SolidColorBrush)System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));

    private void RefreshPropertyPanel()
    {
        var sels = _vm.SelectedShapes;
        if (sels.Count == 0)
        {
            // Empty selection but Text tool active: show only the text-style section pre-filled with defaults.
            if (_vm.CurrentTool == EditorTool.Text)
            {
                NoSelectionText.Visibility = Visibility.Collapsed;
                SelectedShapeStack.Visibility = Visibility.Visible;
                SelectedShapeKindText.Text = "New text — defaults";
                SelOutlineFillRow.Visibility = Visibility.Collapsed;
                SelStrokeSection.Visibility = Visibility.Collapsed;
                SelTextStyleSection.Visibility = Visibility.Visible;
                ApplyDefaultsToTextSection();
                RefreshSelectionAdorner();
                return;
            }
            NoSelectionText.Visibility = Visibility.Visible;
            NoSelectionText.Text = "Select a shape to edit its properties (V tool, then click).";
            SelectedShapeStack.Visibility = Visibility.Collapsed;
            RefreshSelectionAdorner();
            return;
        }

        NoSelectionText.Visibility = Visibility.Collapsed;
        SelectedShapeStack.Visibility = Visibility.Visible;

        // Hide the Fill block when no selected shape supports fill (arrows/lines/freehand/text).
        var anyFillCapable = sels.Any(ShapeSupportsFill);
        SelFillSection.Visibility = anyFillCapable ? Visibility.Visible : Visibility.Collapsed;

        // Hide outline + stroke for text-only selections (text uses Style.Color and FontSize).
        var anyOutlineCapable = sels.Any(ShapeSupportsOutline);
        var anyStrokeCapable = sels.Any(ShapeSupportsStroke);
        SelOutlineSection.Visibility = anyOutlineCapable ? Visibility.Visible : Visibility.Collapsed;
        SelStrokeSection.Visibility = anyStrokeCapable ? Visibility.Visible : Visibility.Collapsed;
        // If both outline and fill are hidden the row collapses entirely (saves vertical space).
        SelOutlineFillRow.Visibility = (anyOutlineCapable || anyFillCapable) ? Visibility.Visible : Visibility.Collapsed;

        var textShapes = sels.OfType<TextShape>().ToList();
        var showTextSection = textShapes.Count > 0 || _vm.CurrentTool == EditorTool.Text;
        SelTextStyleSection.Visibility = showTextSection ? Visibility.Visible : Visibility.Collapsed;

        // Rotation section: only single-selection of a rotatable shape (rect/ellipse/text).
        var rotatable = sels.Count == 1 && sels[0] is RectangleShape or EllipseShape or TextShape;
        SelRotationSection.Visibility = rotatable ? Visibility.Visible : Visibility.Collapsed;

        // For multi-selection, show common values where all selected shapes agree;
        // otherwise show a sensible placeholder (the user can still pick a value to apply to all).
        var first = sels[0];
        var allSameOutline = sels.All(s => s.Outline == first.Outline);
        var fillCapable = sels.Where(ShapeSupportsFill).ToList();
        var allSameFill = fillCapable.Count > 0 && fillCapable.All(s => s.Fill == fillCapable[0].Fill);
        var fillRef = fillCapable.Count > 0 ? fillCapable[0].Fill : ShapeColor.Transparent;
        var allSameStroke = sels.All(s => Math.Abs(s.StrokeWidth - first.StrokeWidth) < 0.01);

        _suppressLiveUpdates = true;
        try
        {
            SelectedShapeKindText.Text = sels.Count == 1
                ? first.GetType().Name.Replace("Shape", "")
                : $"{sels.Count} shapes selected";

            SelOutlineSwatch.SelectedColor = allSameOutline ? first.Outline : ShapeColor.Black;
            SelFillSwatch.SelectedColor = allSameFill ? fillRef : ShapeColor.Transparent;
            SelStrokeSlider.Value = allSameStroke ? first.StrokeWidth : 2;

            if (rotatable)
            {
                var rot = ShapeGripLayout.RotationOf(sels[0]);
                rot = ((rot + 180) % 360 + 360) % 360 - 180;
                SelRotationSlider.Value = rot;
                SelRotationBox.Text = ((int)Math.Round(rot)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (textShapes.Count > 0)
            {
                var firstText = textShapes[0];
                var allSameFamily = textShapes.All(t => t.Style.FontFamily == firstText.Style.FontFamily);
                var allSameSize = textShapes.All(t => Math.Abs(t.Style.FontSize - firstText.Style.FontSize) < 0.01);
                var allSameBold = textShapes.All(t => t.Style.Bold == firstText.Style.Bold);
                var allSameItalic = textShapes.All(t => t.Style.Italic == firstText.Style.Italic);
                var allSameColor = textShapes.All(t => t.Style.Color == firstText.Style.Color);
                var allSameAlign = textShapes.All(t => t.Style.Align == firstText.Style.Align);

                if (allSameFamily) SelFontFamilyCombo.SelectedItem = firstText.Style.FontFamily;
                _currentTextSize = allSameSize ? firstText.Style.FontSize : TextStyle.Default.FontSize;
                SelFontSizeSlider.Value = Math.Clamp(_currentTextSize, SelFontSizeSlider.Minimum, SelFontSizeSlider.Maximum);
                SelFontSizeBox.Text = ((int)Math.Round(_currentTextSize)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                SelBoldCheck.IsChecked = allSameBold && firstText.Style.Bold;
                SelItalicCheck.IsChecked = allSameItalic && firstText.Style.Italic;
                SelTextColorSwatch.SelectedColor = allSameColor ? firstText.Style.Color : ShapeColor.Red;
                RefreshAlignToggles(allSameAlign ? firstText.Style.Align : TextAlign.Left);
            }
            else
            {
                ApplyDefaultsToTextSection();
            }
        }
        finally
        {
            _suppressLiveUpdates = false;
        }
        RefreshSelectionAdorner();
    }
}
