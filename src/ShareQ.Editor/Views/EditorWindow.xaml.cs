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
    private double _minZoom = 0.1;
    private const double MaxZoom = 8.0;
    private bool _initialFitDone;

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
        SelEffectSlider.ValueChanged += (_, _) => OnSelEffectSliderChanged();
        SelEffectBox.LostFocus += (_, _) => OnSelEffectBoxCommitted();
        SelEffectBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnSelEffectBoxCommitted(); };
        SelEffectSecondarySlider.ValueChanged += (_, _) => OnSelEffectSecondarySliderChanged();
        SelEffectSecondaryBox.LostFocus += (_, _) => OnSelEffectSecondaryBoxCommitted();
        SelEffectSecondaryBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnSelEffectSecondaryBoxCommitted(); };
        SelRotationBox.LostFocus += (_, _) => OnSelRotationBoxCommitted();
        SelRotationBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnSelRotationBoxCommitted(); };
        SelFreehandSmoothCheck.Checked += (_, _) => OnSelFreehandSmoothChanged();
        SelFreehandSmoothCheck.Unchecked += (_, _) => OnSelFreehandSmoothChanged();

        WireFontPicker();
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
            // Defer fit to after layout pass so ViewportWidth/Height is populated.
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () => FitZoomToViewport());
            ColorSwatchButton.EyedropperHandler = continuation =>
            {
                EnterCanvasEyedropperMode(continuation);
                return null;
            };
        };
        Closing += OnClosing;
    }

    public bool Saved { get; private set; }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Cleanup that runs whichever path closes the window.
        CommitInlineTextEdit();
        CommitPendingLiveEdit();

        // Skip the prompt when the user explicitly clicked Save (Saved=true) or when nothing was
        // changed. Cancel button + the X button + Alt+F4 all land here with Saved=false; if there
        // are unsaved changes we ask for confirmation. Yes saves and closes, No discards and
        // closes, Cancel keeps the editor open.
        if (!Saved && _vm.HasUnsavedChanges)
        {
            var result = System.Windows.MessageBox.Show(
                "The image has unsaved changes. Save before closing?",
                "ShareQ editor",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Warning,
                System.Windows.MessageBoxResult.Yes);
            if (result == System.Windows.MessageBoxResult.Cancel)
            {
                e.Cancel = true;
                return;
            }
            if (result == System.Windows.MessageBoxResult.Yes)
            {
                Saved = true;
                _vm.MarkSaved();
            }
            // No → fall through and close without saving.
        }

        ColorSwatchButton.EyedropperHandler = null;
        CancelEyedropper();
    }

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
    private void OnBlurToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Blur;
    private void OnPixelateToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Pixelate;
    private void OnSpotlightToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Spotlight;
    private void OnCropToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.Crop;
    private void OnSmartEraserToolClicked(object sender, RoutedEventArgs e) => _vm.CurrentTool = EditorTool.SmartEraser;
    private void OnResizeClicked(object sender, RoutedEventArgs e) => OpenResizeDialog();
    private void OnImageClicked(object sender, RoutedEventArgs e) => InsertImageFromFile();

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
            (Btn: StepToolBtn, Tool: EditorTool.StepCounter),
            (Btn: BlurToolBtn, Tool: EditorTool.Blur),
            (Btn: PixelateToolBtn, Tool: EditorTool.Pixelate),
            (Btn: SpotlightToolBtn, Tool: EditorTool.Spotlight),
            (Btn: CropToolBtn, Tool: EditorTool.Crop),
            (Btn: SmartEraserToolBtn, Tool: EditorTool.SmartEraser)
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
    private void InsertImageFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Insert image",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All files (*.*)|*.*",
            CheckFileExists = true
        };
        if (dlg.ShowDialog(this) != true) return;

        BitmapSource bs;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(dlg.FileName);
            bmp.EndInit();
            bmp.Freeze();
            bs = bmp;
        }
        catch (Exception) { return; }

        InsertImageShape(bs);
    }

    private void PasteImageFromClipboard()
    {
        BitmapSource? bs;
        try
        {
            if (!System.Windows.Clipboard.ContainsImage()) return;
            bs = System.Windows.Clipboard.GetImage();
        }
        catch (System.Runtime.InteropServices.COMException) { return; }
        if (bs is null) return;
        InsertImageShape(bs);
    }

    /// <summary>Encode <paramref name="bs"/> as PNG, fit to ~50% of the canvas, center, add as ImageShape.</summary>
    private void InsertImageShape(BitmapSource bs)
    {
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bs));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        var bytes = ms.ToArray();

        var canvasW = DrawingCanvas.Width > 0 ? DrawingCanvas.Width : bs.PixelWidth;
        var canvasH = DrawingCanvas.Height > 0 ? DrawingCanvas.Height : bs.PixelHeight;
        var maxW = canvasW * 0.5;
        var maxH = canvasH * 0.5;
        var scale = Math.Min(1.0, Math.Min(maxW / bs.PixelWidth, maxH / bs.PixelHeight));
        var w = bs.PixelWidth * scale;
        var h = bs.PixelHeight * scale;
        var x = (canvasW - w) / 2;
        var y = (canvasH - h) / 2;

        _vm.AddImageShape(new ImageShape(x, y, w, h, bytes));
    }

    private void OpenResizeDialog()
    {
        if (SourceImage.Source is not BitmapSource src) return;
        var dlg = new ResizeDialog(src.PixelWidth, src.PixelHeight) { Owner = this };
        if (dlg.ShowDialog() == true)
        {
            _vm.ApplyResize(dlg.NewWidth, dlg.NewHeight);
        }
    }

    private void OnSaveClicked(object sender, RoutedEventArgs e)
    {
        Saved = true;
        _vm.MarkSaved(); // belt-and-braces: the Closing handler also bypasses the prompt when Saved=true
        Close();
    }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { Saved = false; Close(); }

    private void OnZoomInClicked(object sender, RoutedEventArgs e) => SetZoom(_zoom * 1.25);
    private void OnZoomOutClicked(object sender, RoutedEventArgs e) => SetZoom(_zoom / 1.25);
    private void OnZoomResetClicked(object sender, RoutedEventArgs e) => SetZoom(1.0);

    private void OnCanvasMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt;

        if (ctrl)
        {
            var factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            SetZoom(_zoom * factor);
            e.Handled = true;
            return;
        }

        // Wheel (no modifier) over a selection adjusts stroke width on each selected shape;
        // Alt+wheel rotates them. Falls through to scrollviewer scroll when nothing is selected.
        // Live-edit pattern: each notch live-replaces the shape; the existing
        // CommitPendingLiveEdit machinery wraps the whole gesture into one undo step when the
        // selection changes / window closes / Esc is pressed.
        if (_vm.SelectedShapes.Count == 0) return;
        var direction = e.Delta > 0 ? 1 : -1;

        if (alt)
        {
            // 5° per notch — Shift could later quantize to 1° / 15° etc.
            ApplyRotationDelta(direction * 5.0);
            e.Handled = true;
        }
        else
        {
            ApplyStrokeDelta(direction); // ±1 px per notch
            e.Handled = true;
        }
    }

    private void ApplyStrokeDelta(int delta)
    {
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            // Text shapes have no meaningful stroke; the analogous "thickness" knob is the font
            // size. We adjust by 2pt per notch so each scroll feels tactile, clamped 6..200pt.
            if (s is ShareQ.Editor.Model.TextShape t)
            {
                var nextSize = Math.Clamp(t.Style.FontSize + delta * 2.0, 6.0, 200.0);
                if (Math.Abs(nextSize - t.Style.FontSize) < 0.001) continue;
                var replacedText = t with { Style = t.Style with { FontSize = nextSize } };
                _vm.LiveReplaceShape(s, replacedText);
                continue;
            }
            var newStroke = Math.Clamp(s.StrokeWidth + delta, 1.0, 32.0);
            if (Math.Abs(newStroke - s.StrokeWidth) < 0.001) continue;
            var replaced = WithStrokeWidth(s, newStroke);
            if (replaced is null) continue;
            _vm.LiveReplaceShape(s, replaced);
        }
        RefreshPropertyPanel();
    }

    private void ApplyRotationDelta(double degrees)
    {
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            var current = GetRotation(s);
            if (current is null) continue; // shape type doesn't support rotation
            var next = Normalize360(current.Value + degrees);
            var replaced = WithRotation(s, next);
            if (replaced is null) continue;
            _vm.LiveReplaceShape(s, replaced);
        }
        RefreshPropertyPanel();
    }

    private static double Normalize360(double deg)
    {
        var n = deg % 360.0;
        if (n < 0) n += 360.0;
        return n;
    }

    private static Shape? WithStrokeWidth(Shape s, double w) => s switch
    {
        ShareQ.Editor.Model.RectangleShape r  => r  with { StrokeWidth = w },
        ShareQ.Editor.Model.EllipseShape e    => e  with { StrokeWidth = w },
        ShareQ.Editor.Model.TextShape t       => t  with { StrokeWidth = w },
        ShareQ.Editor.Model.ArrowShape a      => a  with { StrokeWidth = w },
        ShareQ.Editor.Model.LineShape l       => l  with { StrokeWidth = w },
        ShareQ.Editor.Model.FreehandShape f   => f  with { StrokeWidth = w },
        ShareQ.Editor.Model.StepCounterShape sc => sc with { StrokeWidth = w },
        // ImageShape / Blur / Pixelate / Spotlight / SmartEraser have no meaningful stroke.
        _ => null,
    };

    private static double? GetRotation(Shape s) => s switch
    {
        ShareQ.Editor.Model.RectangleShape r => r.Rotation,
        ShareQ.Editor.Model.EllipseShape e   => e.Rotation,
        ShareQ.Editor.Model.TextShape t      => t.Rotation,
        ShareQ.Editor.Model.ImageShape img   => img.Rotation,
        ShareQ.Editor.Model.ArrowShape a     => a.Rotation,
        ShareQ.Editor.Model.LineShape l      => l.Rotation,
        ShareQ.Editor.Model.FreehandShape f  => f.Rotation,
        _ => null,
    };

    private static Shape? WithRotation(Shape s, double r) => s switch
    {
        ShareQ.Editor.Model.RectangleShape rect => rect with { Rotation = r },
        ShareQ.Editor.Model.EllipseShape e      => e with { Rotation = r },
        ShareQ.Editor.Model.TextShape t         => t with { Rotation = r },
        ShareQ.Editor.Model.ImageShape img      => img with { Rotation = r },
        ShareQ.Editor.Model.ArrowShape a        => a with { Rotation = r },
        ShareQ.Editor.Model.LineShape l         => l with { Rotation = r },
        ShareQ.Editor.Model.FreehandShape f     => f with { Rotation = r },
        _ => null,
    };

    private void SetZoom(double newZoom)
    {
        var clamped = Math.Clamp(newZoom, _minZoom, MaxZoom);
        if (Math.Abs(clamped - _zoom) < 0.001) return;
        _zoom = clamped;
        CanvasHost.LayoutTransform = new ScaleTransform(_zoom, _zoom);
        ZoomLabel.Text = $"{_zoom * 100:F0}%";
        // Grips are sized in screen pixels via inverse zoom; refresh so the new factor applies.
        RefreshSelectionAdorner();
    }

    /// <summary>On first open, zoom out so the entire screenshot fits in the viewport. Capped at 100%
    /// (we never zoom IN if the image already fits). The fit factor becomes the new zoom-out floor —
    /// the user can keep zooming in but not past the initial fit.</summary>
    private void FitZoomToViewport()
    {
        if (_initialFitDone) return;
        if (SourceImage.Source is not BitmapSource src) return;
        var vw = CanvasScrollViewer.ViewportWidth;
        var vh = CanvasScrollViewer.ViewportHeight;
        if (vw <= 0 || vh <= 0) return;

        const double margin = 16;
        var fit = Math.Min((vw - margin) / src.PixelWidth, (vh - margin) / src.PixelHeight);
        // Only fit when the image is bigger than the viewport — never zoom IN automatically.
        if (fit < 1.0)
        {
            _minZoom = fit;
            SetZoom(fit);
        }
        _initialFitDone = true;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _eyedropperContinuation is not null) { CancelEyedropper(); e.Handled = true; return; }
        if (e.OriginalSource is System.Windows.Controls.TextBox) return;

        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        if (ctrl && e.Key == Key.Z) { _vm.UndoCommand.Execute(null); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { _vm.RedoCommand.Execute(null); e.Handled = true; return; }
        if (ctrl && e.Key == Key.V) { PasteImageFromClipboard(); e.Handled = true; return; }

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
            case Key.B: _vm.CurrentTool = EditorTool.Blur; e.Handled = true; break;
            case Key.X: _vm.CurrentTool = EditorTool.Pixelate; e.Handled = true; break;
            case Key.O: _vm.CurrentTool = EditorTool.Spotlight; e.Handled = true; break;
            case Key.C: _vm.CurrentTool = EditorTool.Crop; e.Handled = true; break;
            case Key.K: _vm.CurrentTool = EditorTool.SmartEraser; e.Handled = true; break;
            case Key.I: InsertImageFromFile(); e.Handled = true; break;
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
            GripKind.Bend => Cursors.Hand,
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
        BlurShape b => b with { X = b.X + dx, Y = b.Y + dy },
        PixelateShape p => p with { X = p.X + dx, Y = p.Y + dy },
        SpotlightShape sp => sp with { X = sp.X + dx, Y = sp.Y + dy },
        ImageShape i => i with { X = i.X + dx, Y = i.Y + dy },
        SmartEraserShape se => se with { X = se.X + dx, Y = se.Y + dy },
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

    private void OnSelFreehandSmoothChanged()
    {
        if (_suppressLiveUpdates) return;
        var smooth = SelFreehandSmoothCheck.IsChecked == true;
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            if (s is FreehandShape f && f.Smooth != smooth)
                _vm.LiveReplaceShape(s, f with { Smooth = smooth });
        }
        // Sticky: the next freshly-drawn freehand stroke inherits the user's last choice.
        // The VM persists FreehandSmoothDefault to EditorDefaults on close.
        _vm.FreehandSmoothDefault = smooth;
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

    private void OnSelEffectSliderChanged()
    {
        if (_suppressLiveUpdates) return;
        var v = SelEffectSlider.Value;
        SelEffectBox.Text = ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyEffectParam(v);
    }

    private void OnSelEffectBoxCommitted()
    {
        if (_suppressLiveUpdates) return;
        if (!double.TryParse(SelEffectBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            SelEffectBox.Text = ((int)Math.Round(SelEffectSlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        v = Math.Max(SelEffectSlider.Minimum, v);
        _suppressLiveUpdates = true;
        try { SelEffectSlider.Value = Math.Clamp(v, SelEffectSlider.Minimum, SelEffectSlider.Maximum); }
        finally { _suppressLiveUpdates = false; }
        ApplyEffectParam(v);
    }

    private void OnSelEffectSecondarySliderChanged()
    {
        if (_suppressLiveUpdates) return;
        var v = SelEffectSecondarySlider.Value;
        SelEffectSecondaryBox.Text = ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        ApplyEffectSecondaryParam(v);
    }

    private void OnSelEffectSecondaryBoxCommitted()
    {
        if (_suppressLiveUpdates) return;
        if (!double.TryParse(SelEffectSecondaryBox.Text, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v))
        {
            SelEffectSecondaryBox.Text = ((int)Math.Round(SelEffectSecondarySlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return;
        }
        v = Math.Max(SelEffectSecondarySlider.Minimum, v);
        _suppressLiveUpdates = true;
        try { SelEffectSecondarySlider.Value = Math.Clamp(v, SelEffectSecondarySlider.Minimum, SelEffectSecondarySlider.Maximum); }
        finally { _suppressLiveUpdates = false; }
        ApplyEffectSecondaryParam(v);
    }

    private void ApplyEffectParam(double v)
    {
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            Shape? updated = s switch
            {
                BlurShape b => b with { Radius = Math.Max(0, v) },
                PixelateShape p => p with { BlockSize = (int)Math.Max(2, Math.Round(v)) },
                SpotlightShape sp => sp with { DimAmount = Math.Clamp(v / 100.0, 0, 1) },
                _ => null
            };
            if (updated is not null) _vm.LiveReplaceShape(s, updated);
        }
    }

    private void ApplyEffectSecondaryParam(double v)
    {
        foreach (var s in _vm.SelectedShapes.ToList())
        {
            Shape? updated = s switch
            {
                SpotlightShape sp => sp with { BlurRadius = Math.Max(0, v) },
                _ => null
            };
            if (updated is not null) _vm.LiveReplaceShape(s, updated);
        }
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
                ImageShape i => i with { Rotation = deg },
                _ => null
            };
            if (updated is not null) _vm.LiveReplaceShape(s, updated);
        }
    }

    private void BeginInlineTextEdit(double x, double y, TextShape? existing)
    {
        CommitInlineTextEdit();

        // For a brand-new text, clear any previous selection so the previously selected text
        // doesn't stay highlighted while the user types into the new one.
        if (existing is null && _vm.SelectedShapes.Count > 0) _vm.SetSelection([]);

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
        if (e.Key == Key.Escape)
        {
            // Esc commits a new text (so the user keeps the typed content selected) but cancels
            // an in-progress edit of an existing text (Esc = "back out, leave the original alone").
            if (_editingTextShape is null) CommitInlineTextEdit();
            else CancelInlineTextEdit();
            e.Handled = true;
            return;
        }
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
        // Hard cap at 1000 — anything bigger crashes WPF text rendering on the canvas.
        v = Math.Clamp(v, 1, 1000);
        _currentTextSize = v;
        SelFontSizeBox.Text = ((int)Math.Round(v)).ToString(System.Globalization.CultureInfo.InvariantCulture);
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
        var family = string.IsNullOrWhiteSpace(SelFontInput.Text) ? "Segoe UI" : SelFontInput.Text;
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

    /// <summary>Cached system font family names. Lazy-initialized once per process — enumerating
    /// Fonts.SystemFontFamilies isn't free.</summary>
    private static readonly Lazy<List<string>> SystemFonts = new(() =>
        Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList());

    private bool _suppressFontInput;

    /// <summary>Custom font picker: TextBox + Popup with ListBox. The TextBox shows the current font
    /// and acts as a search field; the Popup lists matching fonts; clicking one applies it. This
    /// replaces the editable ComboBox approach which had constant flash from CollectionView refresh
    /// fighting with WPF's text/caret bookkeeping.</summary>
    private void WireFontPicker()
    {
        // No popup on GotFocus — that caused a flash from binding ~500 items into the ListBox.
        // Popup opens only when the user actually starts typing or hits Down arrow.
        SelFontInput.TextChanged += (_, _) =>
        {
            if (_suppressFontInput) return;
            var text = SelFontInput.Text ?? "";
            if (text.Length == 0) { SelFontPopup.IsOpen = false; return; }
            RebuildFontList(text);
        };
        SelFontInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { SelFontPopup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.Down)
            {
                if (!SelFontPopup.IsOpen)
                {
                    RebuildFontList(SelFontInput.Text ?? "");
                    SelFontList.SelectedIndex = 0;
                    (SelFontList.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem)?.BringIntoView();
                }
                else if (SelFontList.Items.Count > 0)
                {
                    SelFontList.SelectedIndex = Math.Min(SelFontList.SelectedIndex + 1, SelFontList.Items.Count - 1);
                    if (SelFontList.SelectedIndex < 0) SelFontList.SelectedIndex = 0;
                    (SelFontList.ItemContainerGenerator.ContainerFromIndex(SelFontList.SelectedIndex) as ListBoxItem)?.BringIntoView();
                }
                PreviewSelectedFont();
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                if (SelFontList.SelectedIndex > 0)
                {
                    SelFontList.SelectedIndex--;
                    (SelFontList.ItemContainerGenerator.ContainerFromIndex(SelFontList.SelectedIndex) as ListBoxItem)?.BringIntoView();
                }
                PreviewSelectedFont();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                if (SelFontList.SelectedItem is string s) CommitFontPick(s);
                else if (SelFontList.Items.Count > 0 && SelFontList.Items[0] is string first) CommitFontPick(first);
                e.Handled = true;
            }
        };
        SelFontList.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d)
            {
                var item = FindParent<ListBoxItem>(d);
                if (item?.Content is string s) CommitFontPick(s);
            }
        };
    }

    private void RebuildFontList(string filter)
    {
        var all = SystemFonts.Value;
        IEnumerable<string> source = string.IsNullOrEmpty(filter)
            ? all
            : all.Where(f => f.Contains(filter, StringComparison.OrdinalIgnoreCase));
        SelFontList.ItemsSource = source.Take(200).ToList();
        if (!SelFontPopup.IsOpen) SelFontPopup.IsOpen = true;
    }

    private void CommitFontPick(string family)
    {
        _suppressFontInput = true;
        try
        {
            SelFontInput.Text = family;
            SelFontInput.CaretIndex = family.Length;
        }
        finally { _suppressFontInput = false; }
        SelFontPopup.IsOpen = false;
        OnSelTextStyleChanged();
    }

    /// <summary>Live-preview the font of the currently highlighted ListBox item without closing the popup.
    /// Updates SelFontInput silently and applies the new style via the normal text-style path.</summary>
    private void PreviewSelectedFont()
    {
        if (SelFontList.SelectedItem is not string family) return;
        _suppressFontInput = true;
        try
        {
            SelFontInput.Text = family;
            SelFontInput.CaretIndex = family.Length;
        }
        finally { _suppressFontInput = false; }
        OnSelTextStyleChanged();
    }

    private static T? FindParent<T>(DependencyObject d) where T : DependencyObject
    {
        while (d is not null)
        {
            if (d is T t) return t;
            d = System.Windows.Media.VisualTreeHelper.GetParent(d);
        }
        return null;
    }

    /// <summary>(Legacy method — kept for reference; not wired anymore.)</summary>
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

        const int MinChars = 2;
        var pendingText = "";
        var debounce = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        debounce.Tick += (_, _) =>
        {
            debounce.Stop();
            // Don't filter on tiny inputs — too many matches, churn high, more chance of caret loss.
            var effective = pendingText.Length >= MinChars ? pendingText : "";
            if (string.Equals(currentFilter, effective, StringComparison.Ordinal)) return;
            currentFilter = effective;

            // Refresh resets the editable ComboBox's caret/text. Snapshot then restore.
            var inner = combo.Template?.FindName("PART_EditableTextBox", combo) as System.Windows.Controls.TextBox;
            var savedText = inner?.Text ?? combo.Text;
            var savedCaret = inner?.CaretIndex ?? savedText.Length;

            view.Refresh();
            // Move the keyboard "current item" to the first match so arrow-down navigates within the
            // filtered list rather than the full one. CurrentItem is the cursor for INavigateBy/key
            // events on a CollectionView.
            view.MoveCurrentToFirst();

            combo.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
            {
                if (inner is null) return;
                if (inner.Text != savedText) inner.Text = savedText;
                inner.CaretIndex = Math.Min(savedCaret, inner.Text.Length);
                if (!string.IsNullOrEmpty(currentFilter) && !combo.IsDropDownOpen) combo.IsDropDownOpen = true;
            });
        };

        combo.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent,
            new System.Windows.Controls.TextChangedEventHandler((_, _) =>
            {
                var text = combo.Text ?? "";
                // Don't filter when the user has just committed a selection (text matches selected item).
                if (combo.SelectedItem is string sel && string.Equals(sel, text, StringComparison.Ordinal))
                {
                    debounce.Stop();
                    if (currentFilter.Length > 0)
                    {
                        currentFilter = "";
                        view.Refresh();
                    }
                    return;
                }
                pendingText = text;
                debounce.Stop();
                debounce.Start();
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
            _suppressFontInput = true;
            try { SelFontInput.Text = s.FontFamily; } finally { _suppressFontInput = false; }
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

    /// <summary>True for shapes that visually use the Outline color. TextShape uses Style.Color
    /// instead, and effect shapes (blur/pixelate/spotlight) have no colored stroke at all.</summary>
    private static bool ShapeSupportsOutline(Shape s) =>
        s is RectangleShape or EllipseShape or ArrowShape or LineShape or FreehandShape or StepCounterShape;

    /// <summary>True for shapes that visually use StrokeWidth. TextShape uses FontSize, effect shapes
    /// have no stroke.</summary>
    private static bool ShapeSupportsStroke(Shape s) =>
        s is RectangleShape or EllipseShape or ArrowShape or LineShape or FreehandShape or StepCounterShape;

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
                var isBendGrip = g.Kind == GripKind.Bend;
                // Rotate grip: filled blue circle. Bend grip: filled yellow circle (drag-to-curve
                // visual hint, distinct from the resize / rotate handles). Other grips: white square.
                var fillBrush = isRotateGrip ? new SolidColorBrush(Color.FromArgb(255, 80, 200, 255))
                              : isBendGrip   ? new SolidColorBrush(Color.FromArgb(255, 255, 208, 0))
                              : (Brush)Brushes.White;
                var grip = new System.Windows.Shapes.Rectangle
                {
                    Width = 8, Height = 8,
                    Stroke = new SolidColorBrush(Color.FromArgb(255, 80, 200, 255)),
                    StrokeThickness = 1.5,
                    Fill = fillBrush,
                    RadiusX = (isRotateGrip || isBendGrip) ? 4 : 0,
                    RadiusY = (isRotateGrip || isBendGrip) ? 4 : 0,
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
        BlurShape b => (b.X, b.Y, b.Width, b.Height),
        PixelateShape p => (p.X, p.Y, p.Width, p.Height),
        SpotlightShape s => (s.X, s.Y, s.Width, s.Height),
        ImageShape i => (i.X, i.Y, i.Width, i.Height),
        SmartEraserShape se => (se.X, se.Y, se.Width, se.Height),
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

    private UIElement MakeUiElement(Shape shape)
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
            BlurShape b => CreateBlur(b),
            PixelateShape p => CreatePixelate(p),
            SpotlightShape s => CreateSpotlight(s),
            ImageShape i => CreateImage(i),
            SmartEraserShape se => CreateSmartEraser(se),
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
        // Quadratic bezier through the optional control point. When ControlOffsetX/Y are zero
        // the control coincides with the midpoint and the bezier degenerates to a straight line —
        // visually identical to the previous Line element.
        var (cx, cy) = l.ControlPoint;
        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(l.FromX, l.FromY) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx, cy), new Point(l.ToX, l.ToY), true));
        geom.Figures.Add(fig);
        var path = new System.Windows.Shapes.Path
        {
            Data = geom,
            Stroke = ToBrush(l.Outline),
            StrokeThickness = l.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round
        };
        if (l.Rotation != 0)
        {
            // Path geometry uses absolute canvas coords (no Canvas.SetLeft/Top), so the rotate
            // pivot is also expressed in canvas coords — the segment midpoint.
            var (mx, my) = l.Midpoint;
            path.RenderTransform = new RotateTransform(l.Rotation, mx, my);
        }
        return path;
    }

    private static UIElement CreateArrow(ArrowShape a)
    {
        // Quadratic bezier from From → ControlPoint → To. The arrowhead is anchored to the
        // tangent at the END of the curve (= ToPoint − ControlPoint), not the straight-line
        // direction, so the head still points "out of" the curve when bent.
        var (cx, cy) = a.ControlPoint;
        var tdx = a.ToX - cx;
        var tdy = a.ToY - cy;
        var tlen = Math.Max(Math.Sqrt(tdx * tdx + tdy * tdy), 1);
        var ux = tdx / tlen; var uy = tdy / tlen;
        var headSize = Math.Max(8, a.StrokeWidth * 4);
        var leftX = a.ToX - ux * headSize - uy * (headSize / 2);
        var leftY = a.ToY - uy * headSize + ux * (headSize / 2);
        var rightX = a.ToX - ux * headSize + uy * (headSize / 2);
        var rightY = a.ToY - uy * headSize - ux * (headSize / 2);

        var geom = new PathGeometry();
        var fig = new PathFigure { StartPoint = new Point(a.FromX, a.FromY) };
        fig.Segments.Add(new QuadraticBezierSegment(new Point(cx, cy), new Point(a.ToX, a.ToY), true));
        fig.Segments.Add(new LineSegment(new Point(leftX, leftY), true));
        fig.Segments.Add(new LineSegment(new Point(a.ToX, a.ToY), true));
        fig.Segments.Add(new LineSegment(new Point(rightX, rightY), true));
        geom.Figures.Add(fig);

        var path = new System.Windows.Shapes.Path
        {
            Data = geom,
            Stroke = ToBrush(a.Outline),
            StrokeThickness = a.StrokeWidth,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round
        };
        if (a.Rotation != 0)
        {
            var (mx, my) = a.Midpoint;
            path.RenderTransform = new RotateTransform(a.Rotation, mx, my);
        }
        return path;
    }

    private static UIElement CreateFreehand(FreehandShape f)
    {
        UIElement element;
        if (f.Smooth && f.Points.Count >= 3)
        {
            // Smoothing pipeline:
            //   1. Moving-average denoise (window 7, applied twice). Each point becomes the mean
            //      of its 7-point neighbourhood — actively removes hand jitter, unlike Chaikin
            //      which only rounds corners (and on dense mouse-captured points there are no
            //      real corners to round). Two passes give a visible smoothing without flattening
            //      the gesture entirely. Endpoints are preserved by the windowed clamp.
            //   2. Catmull-Rom → cubic Bezier rendering through the denoised points so the
            //      stroke looks like a continuous ink curve, not micro-segments.
            var pts = MovingAverage(MovingAverage(f.Points, 7), 7);
            var geom = new PathGeometry();
            var fig = new PathFigure { StartPoint = new Point(pts[0].X, pts[0].Y) };
            for (var i = 0; i < pts.Count - 1; i++)
            {
                var p0 = pts[Math.Max(0, i - 1)];
                var p1 = pts[i];
                var p2 = pts[i + 1];
                var p3 = pts[Math.Min(pts.Count - 1, i + 2)];
                var c1x = p1.X + (p2.X - p0.X) / 6.0;
                var c1y = p1.Y + (p2.Y - p0.Y) / 6.0;
                var c2x = p2.X - (p3.X - p1.X) / 6.0;
                var c2y = p2.Y - (p3.Y - p1.Y) / 6.0;
                fig.Segments.Add(new BezierSegment(new Point(c1x, c1y), new Point(c2x, c2y), new Point(p2.X, p2.Y), true));
            }
            geom.Figures.Add(fig);
            element = new System.Windows.Shapes.Path
            {
                Data = geom,
                Stroke = ToBrush(f.Outline),
                StrokeThickness = f.StrokeWidth,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };
        }
        else
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
            element = poly;
        }
        if (f.Rotation != 0 && element is FrameworkElement fe)
        {
            var (px, py) = f.Pivot;
            fe.RenderTransform = new RotateTransform(f.Rotation, px, py);
        }
        return element;
    }

    /// <summary>Moving-average denoising. Each output point is the mean of its window-sized
    /// neighbourhood in the input — a low-pass filter that removes hand jitter without changing
    /// the overall direction of the stroke. The window clamps at the endpoints so the stroke
    /// still starts where the user pressed and ends where they released.</summary>
    private static IReadOnlyList<(double X, double Y)> MovingAverage(IReadOnlyList<(double X, double Y)> pts, int window)
    {
        if (pts.Count < 3 || window < 3) return pts;
        var result = new List<(double X, double Y)>(pts.Count);
        var half = window / 2;
        for (var i = 0; i < pts.Count; i++)
        {
            var lo = Math.Max(0, i - half);
            var hi = Math.Min(pts.Count - 1, i + half);
            double sx = 0, sy = 0;
            for (var j = lo; j <= hi; j++) { sx += pts[j].X; sy += pts[j].Y; }
            var n = hi - lo + 1;
            result.Add((sx / n, sy / n));
        }
        return result;
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

    private UIElement CreateBlur(BlurShape b)
    {
        if (SourceImage.Source is not BitmapSource src) return EmptyRectFor(b.X, b.Y, b.Width, b.Height);
        // Crop the source to the rect so the BlurEffect operates only on the relevant region —
        // applying it to the full-size image and clipping creates an offset halo.
        var ix = (int)Math.Max(0, Math.Floor(b.X));
        var iy = (int)Math.Max(0, Math.Floor(b.Y));
        var iw = (int)Math.Min(src.PixelWidth - ix, Math.Ceiling(b.Width));
        var ih = (int)Math.Min(src.PixelHeight - iy, Math.Ceiling(b.Height));
        if (iw <= 0 || ih <= 0) return EmptyRectFor(b.X, b.Y, b.Width, b.Height);

        var cropped = new System.Windows.Media.Imaging.CroppedBitmap(src, new Int32Rect(ix, iy, iw, ih));
        var img = new System.Windows.Controls.Image
        {
            Source = cropped,
            Stretch = Stretch.Fill,
            Width = iw,
            Height = ih,
            // Clip the effect so the blur halo doesn't bleed outside the rect.
            Clip = new RectangleGeometry(new Rect(0, 0, iw, ih)),
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = b.Radius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian
            }
        };
        Canvas.SetLeft(img, ix);
        Canvas.SetTop(img, iy);
        return img;
    }

    private UIElement CreatePixelate(PixelateShape p)
    {
        if (SourceImage.Source is not BitmapSource src) return EmptyRectFor(p.X, p.Y, p.Width, p.Height);
        var blockSize = Math.Max(2, p.BlockSize);
        var ix = (int)Math.Max(0, Math.Floor(p.X));
        var iy = (int)Math.Max(0, Math.Floor(p.Y));
        var iw = (int)Math.Min(src.PixelWidth - ix, Math.Ceiling(p.Width));
        var ih = (int)Math.Min(src.PixelHeight - iy, Math.Ceiling(p.Height));
        if (iw <= 0 || ih <= 0) return EmptyRectFor(p.X, p.Y, p.Width, p.Height);

        var cropped = new System.Windows.Media.Imaging.CroppedBitmap(src, new Int32Rect(ix, iy, iw, ih));
        var smallW = Math.Max(1, iw / blockSize);
        var smallH = Math.Max(1, ih / blockSize);
        // Downscale via a scaling transform; the upscale happens in the Image's Stretch=Fill with NearestNeighbor.
        var down = new System.Windows.Media.Imaging.TransformedBitmap(cropped,
            new ScaleTransform((double)smallW / iw, (double)smallH / ih));
        var img = new System.Windows.Controls.Image
        {
            Source = down,
            Stretch = Stretch.Fill,
            Width = iw,
            Height = ih
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.NearestNeighbor);
        Canvas.SetLeft(img, ix);
        Canvas.SetTop(img, iy);
        return img;
    }

    private UIElement CreateSpotlight(SpotlightShape s)
    {
        var canvasW = DrawingCanvas.Width > 0 ? DrawingCanvas.Width : (SourceImage.Source as BitmapSource)?.PixelWidth ?? 0;
        var canvasH = DrawingCanvas.Height > 0 ? DrawingCanvas.Height : (SourceImage.Source as BitmapSource)?.PixelHeight ?? 0;

        // Even-odd geometry covers the canvas with a hole at the spotlight rect.
        var outer = new RectangleGeometry(new Rect(0, 0, canvasW, canvasH));
        var inner = new RectangleGeometry(new Rect(s.X, s.Y, s.Width, s.Height));
        var combined = new GeometryGroup { FillRule = FillRule.EvenOdd };
        combined.Children.Add(outer);
        combined.Children.Add(inner);

        // Use a Canvas (not Grid) so absolute Canvas.SetLeft/Top on children works for the blur strips.
        var canvasHost = new Canvas { Width = canvasW, Height = canvasH };

        // Blur layer: instead of one full-size Image with Effect (which produces phantom duplicates
        // because the effect's expanded bounds escape the Clip), crop the four U-strips around the
        // spotlight rect and blur each one in place.
        if (s.BlurRadius > 0 && SourceImage.Source is BitmapSource src)
        {
            var sx = (int)Math.Max(0, Math.Floor(s.X));
            var sy = (int)Math.Max(0, Math.Floor(s.Y));
            var sw = (int)Math.Min(src.PixelWidth - sx, Math.Ceiling(s.Width));
            var sh = (int)Math.Min(src.PixelHeight - sy, Math.Ceiling(s.Height));
            // Top strip
            AddBlurStrip(canvasHost, src, 0, 0, src.PixelWidth, sy, s.BlurRadius);
            // Bottom strip
            AddBlurStrip(canvasHost, src, 0, sy + sh, src.PixelWidth, src.PixelHeight - (sy + sh), s.BlurRadius);
            // Left strip (between top and bottom strips)
            AddBlurStrip(canvasHost, src, 0, sy, sx, sh, s.BlurRadius);
            // Right strip
            AddBlurStrip(canvasHost, src, sx + sw, sy, src.PixelWidth - (sx + sw), sh, s.BlurRadius);
        }

        // Dim layer on top.
        var alpha = (byte)Math.Round(Math.Clamp(s.DimAmount, 0, 1) * 255);
        var dimPath = new System.Windows.Shapes.Path
        {
            Data = combined,
            Fill = new SolidColorBrush(Color.FromArgb(alpha, 0, 0, 0))
        };
        canvasHost.Children.Add(dimPath);
        Canvas.SetLeft(canvasHost, 0);
        Canvas.SetTop(canvasHost, 0);
        return canvasHost;
    }

    private static void AddBlurStrip(Canvas host, BitmapSource src, int x, int y, int w, int h, double blurRadius)
    {
        if (w <= 0 || h <= 0) return;
        var cropped = new System.Windows.Media.Imaging.CroppedBitmap(src, new Int32Rect(x, y, w, h));
        var img = new System.Windows.Controls.Image
        {
            Source = cropped,
            Stretch = Stretch.Fill,
            Width = w,
            Height = h,
            Clip = new RectangleGeometry(new Rect(0, 0, w, h)),
            Effect = new System.Windows.Media.Effects.BlurEffect
            {
                Radius = blurRadius,
                KernelType = System.Windows.Media.Effects.KernelType.Gaussian
            }
        };
        Canvas.SetLeft(img, x);
        Canvas.SetTop(img, y);
        host.Children.Add(img);
    }

    private static UIElement CreateImage(ImageShape i)
    {
        var bmp = new System.Windows.Media.Imaging.BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
        bmp.StreamSource = new System.IO.MemoryStream(i.PngBytes);
        bmp.EndInit();
        bmp.Freeze();
        var img = new System.Windows.Controls.Image
        {
            Source = bmp,
            Stretch = Stretch.Fill,
            Width = i.Width,
            Height = i.Height
        };
        Canvas.SetLeft(img, i.X);
        Canvas.SetTop(img, i.Y);
        if (i.Rotation != 0)
        {
            img.RenderTransform = new RotateTransform(i.Rotation, i.Width / 2, i.Height / 2);
        }
        return img;
    }

    private UIElement CreateSmartEraser(SmartEraserShape s)
    {
        if (SourceImage.Source is not BitmapSource src) return EmptyRectFor(s.X, s.Y, s.Width, s.Height);

        // Sample the four corners of the rect from the source bitmap.
        var (tl, tr, bl, br) = SampleCornerColors(src, s.X, s.Y, s.Width, s.Height);

        // Build a 2×2 BGRA bitmap with the four corner colors. WPF's BitmapScalingMode=Linear
        // bilinearly interpolates this 2×2 across whatever size the Image stretches to — exactly
        // the gradient we want, for free.
        var corners = new System.Windows.Media.Imaging.WriteableBitmap(2, 2, 96, 96, PixelFormats.Bgra32, null);
        var pixels = new byte[16];
        WritePixel(pixels, 0, tl);   // (0,0) top-left
        WritePixel(pixels, 4, tr);   // (1,0) top-right
        WritePixel(pixels, 8, bl);   // (0,1) bottom-left
        WritePixel(pixels, 12, br);  // (1,1) bottom-right
        corners.WritePixels(new Int32Rect(0, 0, 2, 2), pixels, 8, 0);

        var img = new System.Windows.Controls.Image
        {
            Source = corners,
            Stretch = Stretch.Fill,
            Width = s.Width,
            Height = s.Height
        };
        System.Windows.Media.RenderOptions.SetBitmapScalingMode(img, System.Windows.Media.BitmapScalingMode.Linear);
        Canvas.SetLeft(img, s.X);
        Canvas.SetTop(img, s.Y);
        return img;
    }

    private static (byte[] TL, byte[] TR, byte[] BL, byte[] BR) SampleCornerColors(BitmapSource src, double x, double y, double w, double h)
    {
        // Convert to a uniform Bgra32 view once so CopyPixels gives us 4-byte BGRA per pixel.
        var conv = src.Format == PixelFormats.Bgra32 ? src : new System.Windows.Media.Imaging.FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        byte[] SamplePixel(int px, int py)
        {
            var ix = Math.Clamp(px, 0, conv.PixelWidth - 1);
            var iy = Math.Clamp(py, 0, conv.PixelHeight - 1);
            var buf = new byte[4];
            conv.CopyPixels(new Int32Rect(ix, iy, 1, 1), buf, 4, 0);
            return buf;
        }

        // Inset by 1 px so a tiny rect doesn't sample all four corners from the same pixel.
        var x0 = (int)Math.Floor(x);
        var y0 = (int)Math.Floor(y);
        var x1 = (int)Math.Floor(x + Math.Max(1, w - 1));
        var y1 = (int)Math.Floor(y + Math.Max(1, h - 1));
        return (SamplePixel(x0, y0), SamplePixel(x1, y0), SamplePixel(x0, y1), SamplePixel(x1, y1));
    }

    private static void WritePixel(byte[] dest, int offset, byte[] bgra)
    {
        dest[offset] = bgra[0];
        dest[offset + 1] = bgra[1];
        dest[offset + 2] = bgra[2];
        dest[offset + 3] = bgra[3];
    }

    private static UIElement EmptyRectFor(double x, double y, double w, double h)
    {
        var r = new System.Windows.Shapes.Rectangle { Width = Math.Max(0, w), Height = Math.Max(0, h), Fill = Brushes.Transparent };
        Canvas.SetLeft(r, x);
        Canvas.SetTop(r, y);
        return r;
    }

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

        // Rotation section: only single-selection of a rotatable shape (rect/ellipse/text/image).
        var rotatable = sels.Count == 1 && sels[0] is RectangleShape or EllipseShape or TextShape or ImageShape;
        SelRotationSection.Visibility = rotatable ? Visibility.Visible : Visibility.Collapsed;

        // Freehand-specific: smooth toggle. Visible only on a single FreehandShape selection.
        var singleFreehand = sels.Count == 1 ? sels[0] as FreehandShape : null;
        SelFreehandSection.Visibility = singleFreehand is not null ? Visibility.Visible : Visibility.Collapsed;
        if (singleFreehand is not null)
        {
            _suppressLiveUpdates = true;
            SelFreehandSmoothCheck.IsChecked = singleFreehand.Smooth;
            _suppressLiveUpdates = false;
        }

        // Effect section: single-selection of an effect shape (blur/pixelate/spotlight).
        var isEffect = sels.Count == 1 && sels[0] is BlurShape or PixelateShape or SpotlightShape;
        SelEffectSection.Visibility = isEffect ? Visibility.Visible : Visibility.Collapsed;

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

            if (isEffect)
            {
                switch (sels[0])
                {
                    case BlurShape b:
                        SelEffectLabel.Text = "Blur radius (px)";
                        SelEffectSlider.Minimum = 0; SelEffectSlider.Maximum = 60;
                        SelEffectSlider.Value = Math.Clamp(b.Radius, 0, 60);
                        SelEffectBox.Text = ((int)Math.Round(b.Radius)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case PixelateShape p:
                        SelEffectLabel.Text = "Pixel block size";
                        SelEffectSlider.Minimum = 2; SelEffectSlider.Maximum = 60;
                        SelEffectSlider.Value = Math.Clamp(p.BlockSize, 2, 60);
                        SelEffectBox.Text = p.BlockSize.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case SpotlightShape sp:
                        SelEffectLabel.Text = "Spotlight dim (%)";
                        SelEffectSlider.Minimum = 0; SelEffectSlider.Maximum = 100;
                        SelEffectSlider.Value = Math.Round(sp.DimAmount * 100);
                        SelEffectBox.Text = ((int)Math.Round(sp.DimAmount * 100)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        SelEffectSecondaryLabel.Text = "Spotlight blur (px)";
                        SelEffectSecondarySlider.Minimum = 0; SelEffectSecondarySlider.Maximum = 60;
                        SelEffectSecondarySlider.Value = Math.Clamp(sp.BlurRadius, 0, 60);
                        SelEffectSecondaryBox.Text = ((int)Math.Round(sp.BlurRadius)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                }
                SelEffectSecondarySection.Visibility = sels[0] is SpotlightShape ? Visibility.Visible : Visibility.Collapsed;
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

                _suppressFontInput = true;
                try { if (allSameFamily) SelFontInput.Text = firstText.Style.FontFamily; }
                finally { _suppressFontInput = false; }
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
