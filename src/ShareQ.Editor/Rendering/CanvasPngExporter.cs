using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShareQ.Editor.Rendering;

public static class CanvasPngExporter
{
    /// <summary>Renders the visual tree rooted at <paramref name="root"/> to a PNG byte[] at the given size.</summary>
    public static byte[] Export(FrameworkElement root, double width, double height)
    {
        var size = new Size(width, height);
        root.Measure(size);
        root.Arrange(new Rect(size));
        root.UpdateLayout();

        var rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));

        using var ms = new MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }
}
