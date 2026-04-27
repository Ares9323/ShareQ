using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShareQ.App.Views;

/// <summary>Full-screen transparent overlay that shows a 10× magnifier of the area under the cursor.
/// Click samples the center pixel; Esc / right-click cancels. Returns the picked hex via
/// <see cref="PickedHex"/> after <see cref="ShowDialog"/> returns true.</summary>
public partial class ScreenColorPickerOverlay : Window
{
    private const int SampleHalf = 5;       // 11×11 sample
    private const int SampleSize = SampleHalf * 2 + 1;
    private const int MagnifierOffsetX = 24;
    private const int MagnifierOffsetY = 24;

    public ScreenColorPickerOverlay()
    {
        InitializeComponent();
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;
        // Force keyboard focus aggressively — Esc must always close the overlay even when focus
        // policy gets weird with topmost transparent windows.
        Loaded += (_, _) => { Activate(); Focus(); Keyboard.Focus(this); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; Close(); e.Handled = true; } };
    }

    /// <summary>Set when ShowDialog returns true. Format: "#RRGGBB".</summary>
    public string? PickedHex { get; private set; }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // WPF coordinates are in DIPs (96 DPI). Graphics.CopyFromScreen wants physical pixels — they
        // diverge on monitors with scaling ≠ 100%. We grab the physical cursor position from Win32
        // directly to avoid the conversion entirely.
        if (!GetCursorPos(out var native)) return;
        var p = e.GetPosition(this);
        UpdateMagnifier(native.X, native.Y, p);
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!GetCursorPos(out var native)) { Close(); return; }
        var color = SamplePixel(native.X, native.Y);
        if (color is null) { Close(); return; }
        PickedHex = $"#{color.Value.R:X2}{color.Value.G:X2}{color.Value.B:X2}";
        DialogResult = true;
        Close();
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }

    private void UpdateMagnifier(int screenX, int screenY, System.Windows.Point cursorInWindow)
    {
        // Capture an 11×11 region around the cursor.
        var captured = CaptureScreenRegion(screenX - SampleHalf, screenY - SampleHalf, SampleSize, SampleSize);
        if (captured.bitmap is not null)
        {
            MagnifierImage.Source = captured.bitmap;
        }

        if (captured.centerPixel is { } px)
        {
            HexLabel.Text = $"#{px.R:X2}{px.G:X2}{px.B:X2}";
            RgbLabel.Text = $"{px.R}, {px.G}, {px.B}";
        }

        // Position the magnifier panel near the cursor. Flip to the other side near screen edges.
        var posX = cursorInWindow.X + MagnifierOffsetX;
        var posY = cursorInWindow.Y + MagnifierOffsetY;
        if (posX + MagnifierBorder.Width > Width) posX = cursorInWindow.X - MagnifierOffsetX - MagnifierBorder.Width;
        if (posY + MagnifierBorder.Height > Height) posY = cursorInWindow.Y - MagnifierOffsetY - MagnifierBorder.Height;
        Canvas.SetLeft(MagnifierBorder, posX);
        Canvas.SetTop(MagnifierBorder, posY);
    }

    private static (BitmapSource? bitmap, (byte R, byte G, byte B)? centerPixel) CaptureScreenRegion(int x, int y, int w, int h)
    {
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(w, h));
            }
            var center = bmp.GetPixel(w / 2, h / 2);
            var hbm = bmp.GetHbitmap();
            try
            {
                var src = Imaging.CreateBitmapSourceFromHBitmap(hbm, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return (src, (center.R, center.G, center.B));
            }
            finally { _ = DeleteObject(hbm); }
        }
        catch (System.ComponentModel.Win32Exception) { return (null, null); }
        catch (ArgumentException) { return (null, null); }
    }

    private static (byte R, byte G, byte B)? SamplePixel(int x, int y)
    {
        var (_, p) = CaptureScreenRegion(x, y, 1, 1);
        return p;
    }

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);
}
