using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShareQ.Editor.Rendering;

public static class CanvasPngExporter
{
    /// <summary>Renders the visual tree rooted at <paramref name="root"/> to a PNG byte[] at the
    /// given size in PHYSICAL PIXELS. The caller MUST pass the source bitmap's PixelWidth/Height —
    /// not <c>ActualWidth/Height</c>, which is affected by LayoutTransform (zoom) and would either
    /// downsample (zoom-out) or inflate (zoom-in) the export.
    ///
    /// The current LayoutTransform is also reset for the duration of the render so the visual
    /// tree paints at 1:1 with the source pixels, then restored — keeps the user's zoom UI state
    /// intact while the export is canonicalised.</summary>
    public static byte[] Export(FrameworkElement root, int pixelWidth, int pixelHeight)
    {
        var savedTransform = root.LayoutTransform;
        root.LayoutTransform = Transform.Identity;
        try
        {
            var size = new Size(pixelWidth, pixelHeight);
            root.Measure(size);
            root.Arrange(new Rect(size));
            root.UpdateLayout();

            var rtb = new RenderTargetBitmap(pixelWidth, pixelHeight, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(root);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));

            using var ms = new MemoryStream();
            encoder.Save(ms);
            return ms.ToArray();
        }
        finally
        {
            root.LayoutTransform = savedTransform;
            root.UpdateLayout();
        }
    }

    /// <summary>Legacy double-based overload kept for callers that compute size from layout
    /// values; rounds to int. New code should pass the source <see cref="BitmapSource.PixelWidth"/>
    /// directly to avoid sub-pixel rounding.</summary>
    public static byte[] Export(FrameworkElement root, double width, double height)
        => Export(root, (int)Math.Round(width), (int)Math.Round(height));
}
