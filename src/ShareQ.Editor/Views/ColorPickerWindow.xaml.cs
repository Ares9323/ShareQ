using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

/// <summary>ShareX-style color picker dialog. Uses HSV as the source of truth (H/S/V sliders + 2D
/// SV square + Hue strip). RGB / Hex / Alpha are derived; editing them syncs HSV back.</summary>
public partial class ColorPickerWindow : Window
{
    private double _h, _s, _v; // HSV in [0, 1]
    private byte _a = 255;
    private bool _suppress;

    public ColorPickerWindow(ShapeColor initial)
    {
        InitializeComponent();
        var hsv = Hsv.FromRgb(initial.R, initial.G, initial.B);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        _a = initial.A;

        WireSliderInputs();
        SizeChanged += (_, _) => UpdateAllUi();
        Loaded += (_, _) => UpdateAllUi();
    }

    /// <summary>The picked color when the dialog closes with OK. Read this only after ShowDialog() == true.</summary>
    public ShapeColor PickedColor { get; private set; } = ShapeColor.Black;

    /// <summary>Set by an external eyedropper while the dialog is open (e.g. canvas pick or screen pick).
    /// Updates the UI to reflect the sampled color.</summary>
    public void ApplySampledColor(ShapeColor c)
    {
        var hsv = Hsv.FromRgb(c.R, c.G, c.B);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        _a = c.A;
        UpdateAllUi();
    }

    /// <summary>Fired when the user clicks the eyedropper button. Host wires this to its picking flow.</summary>
    public event EventHandler? EyedropperRequested;

    private void WireSliderInputs()
    {
        HSlider.ValueChanged += (_, _) => { if (_suppress) return; _h = HSlider.Value / 360.0; PushFromHsv(); };
        SSlider.ValueChanged += (_, _) => { if (_suppress) return; _s = SSlider.Value / 100.0; PushFromHsv(); };
        VSlider.ValueChanged += (_, _) => { if (_suppress) return; _v = VSlider.Value / 100.0; PushFromHsv(); };
        RSlider.ValueChanged += (_, _) => { if (_suppress) return; PushFromRgb((byte)RSlider.Value, null, null); };
        GSlider.ValueChanged += (_, _) => { if (_suppress) return; PushFromRgb(null, (byte)GSlider.Value, null); };
        BSlider.ValueChanged += (_, _) => { if (_suppress) return; PushFromRgb(null, null, (byte)BSlider.Value); };
        ASlider.ValueChanged += (_, _) => { if (_suppress) return; _a = (byte)ASlider.Value; UpdateAllUi(); };

        HBox.LostFocus += (_, _) => { if (TryReadInt(HBox, 0, 360, out var v)) { _h = v / 360.0; PushFromHsv(); } };
        SBox.LostFocus += (_, _) => { if (TryReadInt(SBox, 0, 100, out var v)) { _s = v / 100.0; PushFromHsv(); } };
        VBox.LostFocus += (_, _) => { if (TryReadInt(VBox, 0, 100, out var v)) { _v = v / 100.0; PushFromHsv(); } };
        RBox.LostFocus += (_, _) => { if (TryReadInt(RBox, 0, 255, out var v)) PushFromRgb((byte)v, null, null); };
        GBox.LostFocus += (_, _) => { if (TryReadInt(GBox, 0, 255, out var v)) PushFromRgb(null, (byte)v, null); };
        BBox.LostFocus += (_, _) => { if (TryReadInt(BBox, 0, 255, out var v)) PushFromRgb(null, null, (byte)v); };
        ABox.LostFocus += (_, _) => { if (TryReadInt(ABox, 0, 255, out var v)) { _a = (byte)v; UpdateAllUi(); } };

        HexBox.LostFocus += (_, _) => OnHexCommitted();
        HexBox.KeyDown += (_, ev) => { if (ev.Key == Key.Enter) OnHexCommitted(); };

        // Commit numeric box on Enter
        foreach (var b in new[] { HBox, SBox, VBox, RBox, GBox, BBox, ABox })
        {
            b.KeyDown += (s, ev) => { if (ev.Key == Key.Enter) Keyboard.ClearFocus(); };
        }
    }

    private static bool TryReadInt(TextBox box, int min, int max, out int value)
    {
        if (int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            value = Math.Clamp(value, min, max);
            return true;
        }
        value = 0;
        return false;
    }

    private void PushFromHsv() => UpdateAllUi();

    private void PushFromRgb(byte? r, byte? g, byte? b)
    {
        var (cr, cg, cb) = new Hsv(_h, _s, _v).ToRgb();
        var nr = r ?? cr;
        var ng = g ?? cg;
        var nb = b ?? cb;
        var hsv = Hsv.FromRgb(nr, ng, nb);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        UpdateAllUi();
    }

    private void OnHexCommitted()
    {
        var s = HexBox.Text.Trim().TrimStart('#');
        try
        {
            byte a = _a, r, g, b;
            if (s.Length == 6)
            {
                r = Convert.ToByte(s[..2], 16); g = Convert.ToByte(s[2..4], 16); b = Convert.ToByte(s[4..6], 16);
            }
            else if (s.Length == 8)
            {
                a = Convert.ToByte(s[..2], 16); r = Convert.ToByte(s[2..4], 16);
                g = Convert.ToByte(s[4..6], 16); b = Convert.ToByte(s[6..8], 16);
            }
            else { return; }
            _a = a;
            var hsv = Hsv.FromRgb(r, g, b);
            _h = hsv.H; _s = hsv.S; _v = hsv.V;
            UpdateAllUi();
        }
        catch (FormatException) { }
        catch (OverflowException) { }
    }

    private void UpdateAllUi()
    {
        _suppress = true;
        try
        {
            var (r, g, b) = new Hsv(_h, _s, _v).ToRgb();

            HSlider.Value = _h * 360.0;
            SSlider.Value = _s * 100.0;
            VSlider.Value = _v * 100.0;
            RSlider.Value = r;
            GSlider.Value = g;
            BSlider.Value = b;
            ASlider.Value = _a;

            HBox.Text = ((int)Math.Round(_h * 360)).ToString(CultureInfo.InvariantCulture);
            SBox.Text = ((int)Math.Round(_s * 100)).ToString(CultureInfo.InvariantCulture);
            VBox.Text = ((int)Math.Round(_v * 100)).ToString(CultureInfo.InvariantCulture);
            RBox.Text = r.ToString(CultureInfo.InvariantCulture);
            GBox.Text = g.ToString(CultureInfo.InvariantCulture);
            BBox.Text = b.ToString(CultureInfo.InvariantCulture);
            ABox.Text = _a.ToString(CultureInfo.InvariantCulture);
            HexBox.Text = $"#{_a:X2}{r:X2}{g:X2}{b:X2}";

            // SV square hue base = pure hue at S=1, V=1
            var (hr, hg, hb) = new Hsv(_h, 1, 1).ToRgb();
            SvHueRect.Fill = new SolidColorBrush(Color.FromRgb(hr, hg, hb));

            // Cursors
            if (SvHost.ActualWidth > 0)
            {
                Canvas.SetLeft(SvCursor, _s * SvHost.ActualWidth - 6);
                Canvas.SetTop(SvCursor, (1 - _v) * SvHost.ActualHeight - 6);
            }
            if (HueHost.ActualHeight > 0)
            {
                Canvas.SetTop(HueCursor, _h * HueHost.ActualHeight - 1);
                HueCursor.Width = HueHost.ActualWidth;
            }

            PreviewBrush.Color = Color.FromArgb(_a, r, g, b);
        }
        finally { _suppress = false; }
    }

    // SV square mouse handling
    private void OnSvMouseDown(object sender, MouseButtonEventArgs e)
    {
        SvHost.CaptureMouse();
        UpdateSvFromMouse(e.GetPosition(SvHost));
    }
    private void OnSvMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !SvHost.IsMouseCaptured) return;
        UpdateSvFromMouse(e.GetPosition(SvHost));
    }
    private void OnSvMouseUp(object sender, MouseButtonEventArgs e) => SvHost.ReleaseMouseCapture();
    private void UpdateSvFromMouse(System.Windows.Point p)
    {
        var w = Math.Max(1, SvHost.ActualWidth);
        var h = Math.Max(1, SvHost.ActualHeight);
        _s = Math.Clamp(p.X / w, 0, 1);
        _v = 1 - Math.Clamp(p.Y / h, 0, 1);
        UpdateAllUi();
    }

    // Hue strip mouse handling
    private void OnHueMouseDown(object sender, MouseButtonEventArgs e)
    {
        HueHost.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueHost));
    }
    private void OnHueMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || !HueHost.IsMouseCaptured) return;
        UpdateHueFromMouse(e.GetPosition(HueHost));
    }
    private void OnHueMouseUp(object sender, MouseButtonEventArgs e) => HueHost.ReleaseMouseCapture();
    private void UpdateHueFromMouse(System.Windows.Point p)
    {
        var h = Math.Max(1, HueHost.ActualHeight);
        _h = Math.Clamp(p.Y / h, 0, 1);
        UpdateAllUi();
    }

    private void OnEyedropperClicked(object sender, RoutedEventArgs e) => EyedropperRequested?.Invoke(this, EventArgs.Empty);

    private void OnOkClicked(object sender, RoutedEventArgs e)
    {
        var (r, g, b) = new Hsv(_h, _s, _v).ToRgb();
        PickedColor = new ShapeColor(_a, r, g, b);
        DialogResult = true;
        Close();
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
