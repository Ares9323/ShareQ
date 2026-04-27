using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.Capture;

namespace ShareQ.App.Windows;

public partial class RegionOverlayWindow : Window
{
    private Point? _dragStart;
    private CaptureRegion? _result;

    public RegionOverlayWindow()
    {
        InitializeComponent();

        var (left, top, width, height) = VirtualScreen.GetBounds();
        Left = left;
        Top = top;
        Width = width;
        Height = height;

        KeyDown += OnKeyDown;
        MouseLeftButtonDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseUp;
        Loaded += (_, _) => { Activate(); Focus(); Cursor = Cursors.Cross; };
    }

    public CaptureRegion? PickRegion()
    {
        ShowDialog();
        return _result;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            _result = null;
            Close();
            e.Handled = true;
        }
    }

    private void OnMouseDown(object? sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(OverlayCanvas);
        Canvas.SetLeft(SelectionRect, _dragStart.Value.X);
        Canvas.SetTop(SelectionRect, _dragStart.Value.Y);
        SelectionRect.Width = 0;
        SelectionRect.Height = 0;
        SelectionRect.Visibility = Visibility.Visible;
        SizeLabelBorder.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        if (_dragStart is null) return;
        var current = e.GetPosition(OverlayCanvas);
        var x = Math.Min(_dragStart.Value.X, current.X);
        var y = Math.Min(_dragStart.Value.Y, current.Y);
        var w = Math.Abs(current.X - _dragStart.Value.X);
        var h = Math.Abs(current.Y - _dragStart.Value.Y);

        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;

        SizeLabel.Text = $"{(int)w} × {(int)h}";
        Canvas.SetLeft(SizeLabelBorder, x + 4);
        Canvas.SetTop(SizeLabelBorder, y - 22);
    }

    private void OnMouseUp(object? sender, MouseButtonEventArgs e)
    {
        if (_dragStart is null) return;
        ReleaseMouseCapture();

        var current = e.GetPosition(OverlayCanvas);
        var dpi = VisualTreeHelper.GetDpi(this);
        var rawX = Math.Min(_dragStart.Value.X, current.X);
        var rawY = Math.Min(_dragStart.Value.Y, current.Y);
        var rawW = Math.Abs(current.X - _dragStart.Value.X);
        var rawH = Math.Abs(current.Y - _dragStart.Value.Y);

        var (vsLeft, vsTop, _, _) = VirtualScreen.GetBounds();
        var pixelX = (int)Math.Round(rawX * dpi.DpiScaleX) + vsLeft;
        var pixelY = (int)Math.Round(rawY * dpi.DpiScaleY) + vsTop;
        var pixelW = (int)Math.Round(rawW * dpi.DpiScaleX);
        var pixelH = (int)Math.Round(rawH * dpi.DpiScaleY);

        if (pixelW < 1 || pixelH < 1)
        {
            _result = null;
        }
        else
        {
            _result = new CaptureRegion(pixelX, pixelY, pixelW, pixelH);
        }
        Close();
    }
}
