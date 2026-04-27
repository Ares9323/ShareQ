using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using ShareQ.Editor.ViewModels;
using Wpf.Ui.Controls;

namespace ShareQ.Editor.Views;

public partial class EditorWindow : FluentWindow
{
    private readonly EditorViewModel _vm;

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
        };
        Loaded += (_, _) => LoadSourceImage();
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
    private void OnUndoClicked(object sender, RoutedEventArgs e) => _vm.UndoCommand.Execute(null);
    private void OnSaveClicked(object sender, RoutedEventArgs e) { Saved = true; Close(); }
    private void OnCancelClicked(object sender, RoutedEventArgs e) { Saved = false; Close(); }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        DrawingCanvas.CaptureMouse();
        var p = e.GetPosition(DrawingCanvas);
        _vm.BeginGesture(p.X, p.Y);
    }

    private void OnCanvasMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(DrawingCanvas);
        _vm.UpdateGesture(p.X, p.Y);
    }

    private void OnCanvasMouseUp(object sender, MouseButtonEventArgs e)
    {
        DrawingCanvas.ReleaseMouseCapture();
        var p = e.GetPosition(DrawingCanvas);
        _vm.CommitGesture(p.X, p.Y);
    }

    private void OnShapesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RedrawAll();
    }

    private void RedrawAll()
    {
        DrawingCanvas.Children.Clear();
        foreach (var shape in _vm.Shapes)
        {
            DrawingCanvas.Children.Add(MakeUiElement(shape));
        }
        RedrawPreview();
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

    private static UIElement MakeUiElement(Shape shape) => shape switch
    {
        RectangleShape r => CreateRectangle(r),
        _ => throw new NotSupportedException($"Unknown shape kind: {shape.GetType().Name}")
    };

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

    private static SolidColorBrush ToBrush(ShapeColor c)
        => c.IsTransparent
            ? (SolidColorBrush)System.Windows.Media.Brushes.Transparent
            : new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
}
