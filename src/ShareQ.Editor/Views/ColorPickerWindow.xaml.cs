using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ShareQ.Editor.Model;

namespace ShareQ.Editor.Views;

/// <summary>ShareX-style HSB/RGB/CMYK colour picker with Unreal-inspired gradient sliders. Single
/// source of truth is HSV (<see cref="_h"/>/<see cref="_s"/>/<see cref="_v"/>) + alpha; every other
/// representation is computed from those on demand. <see cref="_suppress"/> guards every UI sync
/// pass against feedback loops (a slider firing ValueChanged → updating HSV → updating sliders →
/// firing ValueChanged again).</summary>
public partial class ColorPickerWindow : Window
{
    // Authoritative state in HSV [0,1] + alpha byte. Everything else is derived.
    private double _h, _s, _v;
    private byte _a = 255;
    private bool _suppress;
    private readonly ShapeColor _originalColor;

    public ColorPickerWindow(ShapeColor initial)
    {
        InitializeComponent();
        _originalColor = initial;
        var hsv = Hsv.FromRgb(initial.R, initial.G, initial.B);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        _a = initial.A;

        WireSliderInputs();
        SizeChanged += (_, _) => UpdateAllUi();
        Loaded += (_, _) =>
        {
            // Original swatch is a one-time snapshot — never changes after the window opens.
            OriginalPreviewBrush.Color = Color.FromArgb(initial.A, initial.R, initial.G, initial.B);
            LoadPalette();
            UpdateAllUi();
        };
    }

    /// <summary>The picked color when the dialog closes with OK. Read this only after ShowDialog() == true.</summary>
    public ShapeColor PickedColor { get; private set; } = ShapeColor.Black;

    public void ApplySampledColor(ShapeColor c)
    {
        var hsv = Hsv.FromRgb(c.R, c.G, c.B);
        _h = hsv.H;
        _s = hsv.S;
        _v = hsv.V;
        _a = c.A;
        UpdateAllUi();
    }

    public event EventHandler? EyedropperRequested;

    public event EventHandler<ShapeColor>? ColorChanged;

    // ── Wiring ─────────────────────────────────────────────────────────────────────

    private void WireSliderInputs()
    {
        HSlider.ValueChanged += (_, v) => { if (_suppress) return; _h = v / 360.0; UpdateAllUi(); };
        SSlider.ValueChanged += (_, v) => { if (_suppress) return; _s = v / 100.0; UpdateAllUi(); };
        VSlider.ValueChanged += (_, v) => { if (_suppress) return; _v = v / 100.0; UpdateAllUi(); };
        RSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromRgb((byte)Math.Round(v), null, null); };
        GSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromRgb(null, (byte)Math.Round(v), null); };
        BSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromRgb(null, null, (byte)Math.Round(v)); };
        ASlider.ValueChanged += (_, v) => { if (_suppress) return; _a = (byte)Math.Round(v); UpdateAllUi(); };

        CSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromCmyk(v, null, null, null); };
        MSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromCmyk(null, v, null, null); };
        YSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromCmyk(null, null, v, null); };
        KSlider.ValueChanged += (_, v) => { if (_suppress) return; PushFromCmyk(null, null, null, v); };

        HBox.LostFocus += (_, _) => { if (TryReadInt(HBox, 0, 360, out var v)) { _h = v / 360.0; UpdateAllUi(); } };
        SBox.LostFocus += (_, _) => { if (TryReadInt(SBox, 0, 100, out var v)) { _s = v / 100.0; UpdateAllUi(); } };
        VBox.LostFocus += (_, _) => { if (TryReadInt(VBox, 0, 100, out var v)) { _v = v / 100.0; UpdateAllUi(); } };
        RBox.LostFocus += (_, _) => { if (TryReadInt(RBox, 0, 255, out var v)) PushFromRgb((byte)v, null, null); };
        GBox.LostFocus += (_, _) => { if (TryReadInt(GBox, 0, 255, out var v)) PushFromRgb(null, (byte)v, null); };
        BBox.LostFocus += (_, _) => { if (TryReadInt(BBox, 0, 255, out var v)) PushFromRgb(null, null, (byte)v); };
        ABox.LostFocus += (_, _) => { if (TryReadInt(ABox, 0, 255, out var v)) { _a = (byte)v; UpdateAllUi(); } };
        CBox.LostFocus += (_, _) => { if (TryReadInt(CBox, 0, 100, out var v)) PushFromCmyk(v, null, null, null); };
        MBox.LostFocus += (_, _) => { if (TryReadInt(MBox, 0, 100, out var v)) PushFromCmyk(null, v, null, null); };
        YBox.LostFocus += (_, _) => { if (TryReadInt(YBox, 0, 100, out var v)) PushFromCmyk(null, null, v, null); };
        KBox.LostFocus += (_, _) => { if (TryReadInt(KBox, 0, 100, out var v)) PushFromCmyk(null, null, null, v); };

        // Hex + Decimal commit on every keystroke (with _suppress guard so the brush-update
        // round-trip from UpdateAllUi → set HexBox.Text doesn't re-enter). The user complaint
        // "set Hex doesn't update the preview" was the LostFocus-only handler not firing while
        // the user kept typing/clicking inside the box.
        HexBox.TextChanged += (_, _) => { if (!_suppress) OnHexCommitted(); };
        HexBox.KeyDown     += (_, ev) => { if (ev.Key == Key.Enter) Keyboard.ClearFocus(); };
        DecBox.TextChanged += (_, _) => { if (!_suppress) OnDecimalCommitted(); };
        DecBox.KeyDown     += (_, ev) => { if (ev.Key == Key.Enter) Keyboard.ClearFocus(); };
        SrgbToggle.Click += (_, _) => UpdateAllUi();   // gamma-flip the preview brushes immediately

        foreach (var b in new[] { HBox, SBox, VBox, RBox, GBox, BBox, ABox, CBox, MBox, YBox, KBox })
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

    // ── State conversions ─────────────────────────────────────────────────────────

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

    private void PushFromCmyk(double? c, double? m, double? y, double? k)
    {
        var (cur_c, cur_m, cur_y, cur_k) = RgbToCmyk(new Hsv(_h, _s, _v).ToRgb());
        var nc = (c ?? cur_c) / 100.0;
        var nm = (m ?? cur_m) / 100.0;
        var ny = (y ?? cur_y) / 100.0;
        var nk = (k ?? cur_k) / 100.0;
        var (r, g, b) = CmykToRgb(nc, nm, ny, nk);
        var hsv = Hsv.FromRgb(r, g, b);
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

    /// <summary>Parses a packed-AARRGGBB integer (the same form <see cref="FormatDecimal"/>
    /// emits) and pushes it through HSV. Silent on parse failure so partial typing — e.g. the
    /// user halfway through entering a 10-digit number — doesn't mangle state.</summary>
    private void OnDecimalCommitted()
    {
        if (!uint.TryParse(DecBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var packed))
            return;
        var a = (byte)((packed >> 24) & 0xFF);
        var r = (byte)((packed >> 16) & 0xFF);
        var g = (byte)((packed >> 8) & 0xFF);
        var b = (byte)(packed & 0xFF);
        _a = a;
        var hsv = Hsv.FromRgb(r, g, b);
        _h = hsv.H; _s = hsv.S; _v = hsv.V;
        UpdateAllUi();
    }

    /// <summary>Standard CMYK conversion. Returns C/M/Y/K as percentages 0-100.</summary>
    private static (double C, double M, double Y, double K) RgbToCmyk((byte R, byte G, byte B) rgb)
    {
        double r = rgb.R / 255.0, g = rgb.G / 255.0, b = rgb.B / 255.0;
        var k = 1.0 - Math.Max(r, Math.Max(g, b));
        if (k >= 0.99999) return (0, 0, 0, 100);   // pure black — undefined CMY
        var c = (1 - r - k) / (1 - k);
        var m = (1 - g - k) / (1 - k);
        var y = (1 - b - k) / (1 - k);
        return (c * 100.0, m * 100.0, y * 100.0, k * 100.0);
    }

    /// <summary>Standard CMYK→RGB conversion. Inputs are 0-1.</summary>
    private static (byte R, byte G, byte B) CmykToRgb(double c, double m, double y, double k)
    {
        var r = (byte)Math.Round(255 * (1 - c) * (1 - k));
        var g = (byte)Math.Round(255 * (1 - m) * (1 - k));
        var b = (byte)Math.Round(255 * (1 - y) * (1 - k));
        return (r, g, b);
    }

    // ── UI sync ─────────────────────────────────────────────────────────────────────

    private void UpdateAllUi()
    {
        _suppress = true;
        try
        {
            var (r, g, b) = new Hsv(_h, _s, _v).ToRgb();
            var (c, m, y, k) = RgbToCmyk((r, g, b));

            HSlider.Value = _h * 360.0;
            SSlider.Value = _s * 100.0;
            VSlider.Value = _v * 100.0;
            RSlider.Value = r;
            GSlider.Value = g;
            BSlider.Value = b;
            ASlider.Value = _a;
            CSlider.Value = c;
            MSlider.Value = m;
            YSlider.Value = y;
            KSlider.Value = k;

            HBox.Text = ((int)Math.Round(_h * 360)).ToString(CultureInfo.InvariantCulture);
            SBox.Text = ((int)Math.Round(_s * 100)).ToString(CultureInfo.InvariantCulture);
            VBox.Text = ((int)Math.Round(_v * 100)).ToString(CultureInfo.InvariantCulture);
            RBox.Text = r.ToString(CultureInfo.InvariantCulture);
            GBox.Text = g.ToString(CultureInfo.InvariantCulture);
            BBox.Text = b.ToString(CultureInfo.InvariantCulture);
            ABox.Text = _a.ToString(CultureInfo.InvariantCulture);
            CBox.Text = ((int)Math.Round(c)).ToString(CultureInfo.InvariantCulture);
            MBox.Text = ((int)Math.Round(m)).ToString(CultureInfo.InvariantCulture);
            YBox.Text = ((int)Math.Round(y)).ToString(CultureInfo.InvariantCulture);
            KBox.Text = ((int)Math.Round(k)).ToString(CultureInfo.InvariantCulture);
            HexBox.Text = $"#{_a:X2}{r:X2}{g:X2}{b:X2}";
            DecBox.Text = (((uint)_a << 24) | ((uint)r << 16) | ((uint)g << 8) | b)
                .ToString(CultureInfo.InvariantCulture);

            // SV square hue base = pure hue at S=1, V=1
            var (hr, hg, hb) = new Hsv(_h, 1, 1).ToRgb();
            SvHueRect.Fill = new SolidColorBrush(Color.FromRgb(hr, hg, hb));

            if (SvHost.ActualWidth > 0)
            {
                Canvas.SetLeft(SvCursor, _s * SvHost.ActualWidth - 6);
                Canvas.SetTop(SvCursor, (1 - _v) * SvHost.ActualHeight - 6);
            }

            // Preview swatches respect the sRGB toggle: ON = display the byte values straight (the
            // OS / display already applies sRGB gamma during compositing). OFF = treat the bytes
            // as if they represented LINEAR values, applying linear→sRGB so the user sees the
            // perceptually-different rendering you'd get if you handed those bytes to a renderer
            // that interprets them linearly (e.g. an Unreal material). This is exactly the
            // behaviour the user expects from the Unreal-style "sRGB Preview" checkbox.
            var (pr, pg, pb) = ApplyPreviewGamma(r, g, b);
            NewPreviewBrush.Color = Color.FromArgb(_a, pr, pg, pb);
            var (opr, opg, opb) = ApplyPreviewGamma(_originalColor.R, _originalColor.G, _originalColor.B);
            OriginalPreviewBrush.Color = Color.FromArgb(_originalColor.A, opr, opg, opb);

            RefreshGradients(r, g, b, c, m, y, k);
        }
        finally { _suppress = false; }

        var current = new ShapeColor(_a, ((Hsv)new Hsv(_h, _s, _v)).ToRgb().R,
                                          ((Hsv)new Hsv(_h, _s, _v)).ToRgb().G,
                                          ((Hsv)new Hsv(_h, _s, _v)).ToRgb().B);
        ColorChanged?.Invoke(this, current);
    }

    /// <summary>Recompute every slider's gradient track based on the current state of the OTHER
    /// channels — this is what makes the sliders Unreal-style "see what dragging will produce".
    /// Cheap to do every UpdateAllUi: just builds 11 LinearGradientBrushes.</summary>
    private void RefreshGradients(byte r, byte g, byte b, double c, double m, double y, double k)
    {
        // Hue is rainbow regardless of other channels.
        HSlider.TrackBrush = HueRainbow();

        // Saturation: from grayscale-at-current-V to full-color-at-current-H+V.
        var (greyR, greyG, greyB) = new Hsv(_h, 0, _v).ToRgb();
        var (fullR, fullG, fullB) = new Hsv(_h, 1, _v).ToRgb();
        SSlider.TrackBrush = HorizontalGradient(
            Color.FromRgb(greyR, greyG, greyB),
            Color.FromRgb(fullR, fullG, fullB));

        // Brightness: from black to full-saturation-color-at-current-H+S.
        var (vTopR, vTopG, vTopB) = new Hsv(_h, _s, 1).ToRgb();
        VSlider.TrackBrush = HorizontalGradient(
            Color.FromRgb(0, 0, 0),
            Color.FromRgb(vTopR, vTopG, vTopB));

        // R/G/B: each one shows itself sliding 0→255 with the OTHER two held constant.
        RSlider.TrackBrush = HorizontalGradient(Color.FromRgb(0,   g, b), Color.FromRgb(255, g, b));
        GSlider.TrackBrush = HorizontalGradient(Color.FromRgb(r,   0, b), Color.FromRgb(r, 255, b));
        BSlider.TrackBrush = HorizontalGradient(Color.FromRgb(r,   g, 0), Color.FromRgb(r, g, 255));

        // Alpha: transparent → fully-opaque current colour. The slider shows a checker behind so
        // you can see the result through the gradient.
        ASlider.TrackBrush = HorizontalGradient(
            Color.FromArgb(0,   r, g, b),
            Color.FromArgb(255, r, g, b));

        // CMYK: sliding each channel 0→100% with others held.
        var c01 = c / 100.0; var m01 = m / 100.0; var y01 = y / 100.0; var k01 = k / 100.0;
        var (cMin_R, cMin_G, cMin_B) = CmykToRgb(0, m01, y01, k01);
        var (cMax_R, cMax_G, cMax_B) = CmykToRgb(1, m01, y01, k01);
        CSlider.TrackBrush = HorizontalGradient(Color.FromRgb(cMin_R, cMin_G, cMin_B), Color.FromRgb(cMax_R, cMax_G, cMax_B));

        var (mMin_R, mMin_G, mMin_B) = CmykToRgb(c01, 0, y01, k01);
        var (mMax_R, mMax_G, mMax_B) = CmykToRgb(c01, 1, y01, k01);
        MSlider.TrackBrush = HorizontalGradient(Color.FromRgb(mMin_R, mMin_G, mMin_B), Color.FromRgb(mMax_R, mMax_G, mMax_B));

        var (yMin_R, yMin_G, yMin_B) = CmykToRgb(c01, m01, 0, k01);
        var (yMax_R, yMax_G, yMax_B) = CmykToRgb(c01, m01, 1, k01);
        YSlider.TrackBrush = HorizontalGradient(Color.FromRgb(yMin_R, yMin_G, yMin_B), Color.FromRgb(yMax_R, yMax_G, yMax_B));

        var (kMin_R, kMin_G, kMin_B) = CmykToRgb(c01, m01, y01, 0);
        KSlider.TrackBrush = HorizontalGradient(Color.FromRgb(kMin_R, kMin_G, kMin_B), Color.FromRgb(0, 0, 0));
    }

    private static Brush HorizontalGradient(Color start, Color end)
    {
        var b = new LinearGradientBrush(start, end, new Point(0, 0.5), new Point(1, 0.5));
        b.Freeze();
        return b;
    }

    private static Brush HueRainbow()
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 0.000),
                new GradientStop(Color.FromRgb(0xFF, 0xFF, 0x00), 0.167),
                new GradientStop(Color.FromRgb(0x00, 0xFF, 0x00), 0.333),
                new GradientStop(Color.FromRgb(0x00, 0xFF, 0xFF), 0.500),
                new GradientStop(Color.FromRgb(0x00, 0x00, 0xFF), 0.667),
                new GradientStop(Color.FromRgb(0xFF, 0x00, 0xFF), 0.833),
                new GradientStop(Color.FromRgb(0xFF, 0x00, 0x00), 1.000),
            }
        };
        b.Freeze();
        return b;
    }

    // ── SV square ─────────────────────────────────────────────────────────────────

    private void OnSvMouseDown(object sender, MouseButtonEventArgs e) { SvHost.CaptureMouse(); UpdateSvFromMouse(e.GetPosition(SvHost)); }
    private void OnSvMouseMove(object sender, MouseEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed && SvHost.IsMouseCaptured) UpdateSvFromMouse(e.GetPosition(SvHost)); }
    private void OnSvMouseUp(object sender, MouseButtonEventArgs e) => SvHost.ReleaseMouseCapture();
    private void UpdateSvFromMouse(Point p)
    {
        var w = Math.Max(1, SvHost.ActualWidth);
        var h = Math.Max(1, SvHost.ActualHeight);
        _s = Math.Clamp(p.X / w, 0, 1);
        _v = 1 - Math.Clamp(p.Y / h, 0, 1);
        UpdateAllUi();
    }

    // ── sRGB / linear preview gamma ─────────────────────────────────────────────────

    /// <summary>When the sRGB toggle is ON we leave the byte values alone (the display already
    /// applies sRGB gamma — what the user sees IS sRGB). When OFF we treat the same bytes as
    /// LINEAR values and apply linear→sRGB so the swatch shows the perceptually-different result
    /// you'd get if a renderer interpreted those bytes linearly (Unreal-style).</summary>
    private (byte R, byte G, byte B) ApplyPreviewGamma(byte r, byte g, byte b)
    {
        if (SrgbToggle is null || SrgbToggle.IsChecked == true) return (r, g, b);
        return (LinearToSrgb(r), LinearToSrgb(g), LinearToSrgb(b));
    }

    /// <summary>Standard sRGB EOTF (linear → sRGB), expecting 0–255 byte input. Piecewise to
    /// match the official IEC 61966-2-1 transfer function — gamma 2.2 alone would visibly
    /// disagree near the toe.</summary>
    private static byte LinearToSrgb(byte channel)
    {
        var c = channel / 255.0;
        var s = c <= 0.0031308 ? c * 12.92 : 1.055 * Math.Pow(c, 1.0 / 2.4) - 0.055;
        return (byte)Math.Clamp(Math.Round(s * 255), 0, 255);
    }

    // ── RGB / CMYK tab ─────────────────────────────────────────────────────────────

    private void OnTabChanged(object sender, RoutedEventArgs e)
    {
        // Wired in XAML before fields are initialized, so guard against null UI.
        if (RgbBlock is null || CmykBlock is null) return;
        RgbBlock.Visibility  = RgbTab.IsChecked == true  ? Visibility.Visible : Visibility.Collapsed;
        CmykBlock.Visibility = CmykTab.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Palette ─────────────────────────────────────────────────────────────────────

    private readonly ObservableCollection<PaletteEntry> _paletteItems = [];

    private void LoadPalette()
    {
        PaletteHost.ItemsSource = _paletteItems;
        RebuildPalette();
    }

    private void OnPaletteSourceChanged(object sender, RoutedEventArgs e) => RebuildPalette();

    private void RebuildPalette()
    {
        if (_paletteItems is null) return;
        _paletteItems.Clear();
        var source = PaletteRecent.IsChecked == true
            ? ColorSwatchButton.CurrentRecents
            : (IReadOnlyList<ShapeColor>)StandardColors.Palette;
        // Pad / truncate to 28 swatches so the grid stays a tidy 14×2 even when recents are sparse.
        for (var i = 0; i < 28; i++)
        {
            var color = i < source.Count ? source[i] : ShapeColor.Transparent;
            _paletteItems.Add(new PaletteEntry(color, color.IsTransparent
                ? (Brush)Application.Current.Resources["CheckerBrush"]
                : new SolidColorBrush(Color.FromArgb(color.A, color.R, color.G, color.B))));
        }
    }

    private void OnPaletteSwatchClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not ShapeColor c || c.IsTransparent) return;
        ApplySampledColor(c);
        if (e.ClickCount >= 2)
        {
            CommitOk();
        }
    }

    private sealed record PaletteEntry(ShapeColor Color, Brush Brush);

    // ── Copy formatters ─────────────────────────────────────────────────────────────

    private (byte R, byte G, byte B) CurrentRgb() => new Hsv(_h, _s, _v).ToRgb();

    private string FormatRgb()
    {
        var (r, g, b) = CurrentRgb();
        return _a == 255
            ? $"rgb({r}, {g}, {b})"
            : $"rgba({r}, {g}, {b}, {(_a / 255.0).ToString("0.##", CultureInfo.InvariantCulture)})";
    }

    private string FormatHex()
    {
        var (r, g, b) = CurrentRgb();
        return $"#{_a:X2}{r:X2}{g:X2}{b:X2}";
    }

    private string FormatCmyk()
    {
        var (c, m, y, k) = RgbToCmyk(CurrentRgb());
        return $"cmyk({c.ToString("0.#", CultureInfo.InvariantCulture)}%, " +
               $"{m.ToString("0.#", CultureInfo.InvariantCulture)}%, " +
               $"{y.ToString("0.#", CultureInfo.InvariantCulture)}%, " +
               $"{k.ToString("0.#", CultureInfo.InvariantCulture)}%)";
    }

    private string FormatHsb() =>
        $"hsb({(int)Math.Round(_h * 360)}°, {(int)Math.Round(_s * 100)}%, {(int)Math.Round(_v * 100)}%)";

    private string FormatDecimal()
    {
        var (r, g, b) = CurrentRgb();
        var packed = ((uint)_a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
        return packed.ToString(CultureInfo.InvariantCulture);
    }

    private void OnCopyAllClicked(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder()
            .AppendLine(FormatRgb())
            .AppendLine(FormatHex())
            .AppendLine(FormatCmyk())
            .AppendLine(FormatHsb())
            .Append(FormatDecimal());
        SafeCopy(sb.ToString());
    }
    private void OnCopyRgbClicked(object sender, RoutedEventArgs e)     => SafeCopy(FormatRgb());
    private void OnCopyHexClicked(object sender, RoutedEventArgs e)     => SafeCopy(FormatHex());
    private void OnCopyCmykClicked(object sender, RoutedEventArgs e)    => SafeCopy(FormatCmyk());
    private void OnCopyHsbClicked(object sender, RoutedEventArgs e)     => SafeCopy(FormatHsb());
    private void OnCopyDecimalClicked(object sender, RoutedEventArgs e) => SafeCopy(FormatDecimal());

    private static void SafeCopy(string text)
    {
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard may be locked momentarily — silent retry isn't worth it */ }
    }

    // ── Final actions ───────────────────────────────────────────────────────────────

    private void OnEyedropperClicked(object sender, RoutedEventArgs e) => EyedropperRequested?.Invoke(this, EventArgs.Empty);

    private void OnOkClicked(object sender, RoutedEventArgs e) => CommitOk();

    private void CommitOk()
    {
        var (r, g, b) = CurrentRgb();
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
