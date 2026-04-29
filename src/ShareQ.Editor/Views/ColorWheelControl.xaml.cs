using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

/// <summary>Hue × Saturation disc (Unreal-style colour wheel). Hue runs around the perimeter,
/// saturation grows from centre to edge. Brightness (V) is NOT shown on the disc itself — it's
/// applied as a black-overlay tint so we don't have to re-rasterise the whole bitmap when the
/// user drags the V slider. Click + drag selects an (H, S) pair; the host wires V separately.</summary>
public partial class ColorWheelControl : UserControl
{
    /// <summary>Bitmap pixel size — rendered once and scaled up. Big enough that scaling looks
    /// crisp on a 280-DIP wheel at 200% DPI without breaking the bank on memory (~520KB).</summary>
    private const int BitmapSize = 360;

    private WriteableBitmap? _disc;

    /// <summary>Optional gamma applied to every pixel during render. Set by the host (the picker
    /// reuses its sRGB-toggle transform here too) so the wheel stays visually consistent with
    /// the sliders / swatches / preview when the user flips the sRGB toggle. Setting this
    /// re-rasterises the disc — cheap (~few ms for 360² pixels), only happens on toggle click.</summary>
    public Func<byte, byte, byte, (byte R, byte G, byte B)>? PixelGamma
    {
        get => _pixelGamma;
        set
        {
            _pixelGamma = value;
            if (_disc is not null) RenderDiscAtFullValue();
        }
    }
    private Func<byte, byte, byte, (byte R, byte G, byte B)>? _pixelGamma;

    public ColorWheelControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            EnsureDiscRendered();
            UpdateValueOverlay();
            UpdateCursor();
        };
        SizeChanged += (_, _) => UpdateCursor();
    }

    public static readonly DependencyProperty HProperty = DependencyProperty.Register(
        nameof(H), typeof(double), typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ColorWheelControl)d).UpdateCursor()));

    public static readonly DependencyProperty SProperty = DependencyProperty.Register(
        nameof(S), typeof(double), typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ColorWheelControl)d).UpdateCursor()));

    public static readonly DependencyProperty VProperty = DependencyProperty.Register(
        nameof(V), typeof(double), typeof(ColorWheelControl),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((ColorWheelControl)d).UpdateValueOverlay()));

    public double H { get => (double)GetValue(HProperty); set => SetValue(HProperty, value); }
    public double S { get => (double)GetValue(SProperty); set => SetValue(SProperty, value); }
    public double V { get => (double)GetValue(VProperty); set => SetValue(VProperty, value); }

    /// <summary>Fired on every mouse-driven update of <see cref="H"/>/<see cref="S"/> — hosts that
    /// don't bind two-way can listen here and do their own thing (e.g. our picker recomputes
    /// every other channel display from HSV after each tick).</summary>
    public event EventHandler? PointPicked;

    private void EnsureDiscRendered()
    {
        if (_disc is not null) return;
        _disc = new WriteableBitmap(BitmapSize, BitmapSize, 96, 96, System.Windows.Media.PixelFormats.Pbgra32, null);
        WheelImage.Source = _disc;
        RenderDiscAtFullValue();
    }

    /// <summary>Fills the bitmap once with the H×S disc at V=1. Anything outside the inscribed
    /// circle is fully transparent so the parent's background shows through (preserves the
    /// rounded look without anti-aliasing artefacts).</summary>
    private void RenderDiscAtFullValue()
    {
        if (_disc is null) return;
        const int w = BitmapSize, h = BitmapSize;
        const double cx = w / 2.0, cy = h / 2.0;
        const double radius = w / 2.0;
        var pixels = new uint[w * h];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var dx = x - cx + 0.5;     // sample at pixel centre to avoid a hard inner edge
                var dy = y - cy + 0.5;
                var dist = Math.Sqrt(dx * dx + dy * dy);
                if (dist > radius)
                {
                    pixels[y * w + x] = 0;   // fully transparent (Pbgra32 — A premultiplied)
                    continue;
                }
                var sat = Math.Min(1.0, dist / (radius - 1));
                // atan2 returns -π…π; remap to 0…1 with red at the right (theta=0).
                var theta = Math.Atan2(-dy, dx);
                if (theta < 0) theta += 2 * Math.PI;
                var hue = theta / (2 * Math.PI);

                var (r, g, b) = new Hsv(hue, sat, 1.0).ToRgb();
                if (_pixelGamma is not null) (r, g, b) = _pixelGamma(r, g, b);
                pixels[y * w + x] = ((uint)0xFF << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
            }
        }
        _disc.WritePixels(new Int32Rect(0, 0, w, h), pixels, w * 4, 0);
    }

    private void UpdateValueOverlay()
    {
        // Adjust the FILL alpha (not the element Opacity) so the Stroke stays at full opacity —
        // gives the disc a permanent black outline regardless of V, hiding the antialias seam
        // between the round bitmap area and the rectangular Image element's transparent corners.
        var alpha = (byte)Math.Clamp(Math.Round((1.0 - V) * 255.0), 0, 255);
        ValueOverlayBrush.Color = System.Windows.Media.Color.FromArgb(alpha, 0, 0, 0);
    }

    private void UpdateCursor()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = Math.Min(ActualWidth, ActualHeight);
        var cx = ActualWidth / 2.0;
        var cy = ActualHeight / 2.0;
        var radius = size / 2.0 - 1;            // -1: keep inside the visual edge
        var theta = H * 2 * Math.PI;
        var r = S * radius;
        var px = cx + r * Math.Cos(theta);
        var py = cy - r * Math.Sin(theta);       // -sin: y axis flipped
        Canvas.SetLeft(WheelCursor, px - WheelCursor.Width / 2);
        Canvas.SetTop(WheelCursor, py - WheelCursor.Height / 2);
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        Root.CaptureMouse();
        UpdateFromMouse(e.GetPosition(Root));
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !Root.IsMouseCaptured) return;
        UpdateFromMouse(e.GetPosition(Root));
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => Root.ReleaseMouseCapture();

    /// <summary>Map mouse pos → (H, S). Clamps saturation at 1 so dragging outside the disc
    /// snaps to the edge instead of returning >1 garbage; the angle keeps tracking the cursor
    /// even past the rim, which feels natural during quick rotations.</summary>
    private void UpdateFromMouse(Point p)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        var size = Math.Min(ActualWidth, ActualHeight);
        var cx = ActualWidth / 2.0;
        var cy = ActualHeight / 2.0;
        var radius = size / 2.0;
        var dx = p.X - cx;
        var dy = p.Y - cy;
        var dist = Math.Sqrt(dx * dx + dy * dy);
        var theta = Math.Atan2(-dy, dx);
        if (theta < 0) theta += 2 * Math.PI;
        H = theta / (2 * Math.PI);
        S = Math.Clamp(dist / radius, 0, 1);
        PointPicked?.Invoke(this, EventArgs.Empty);
    }
}
