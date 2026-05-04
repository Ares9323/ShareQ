using ShareQ.ImageEffects.Parameters;
using SkiaSharp;

namespace ShareQ.ImageEffects.Drawings;

/// <summary>Ported from ShareX (GPL v3) — ImageEditor Drawings/DrawImageEffect.cs. Composites
/// an external image onto the source bitmap (logo / watermark). MVP scope: anchor + offset
/// + opacity + DontResize / AbsoluteSize. Full feature set (RotateFlip / Tile / Interpolation
/// modes / Compositing) is post-MVP. <see cref="ImageLocation"/> can be either an absolute
/// path or a path relative to the .sxie package's extraction folder; the importer rewrites
/// relative ones to absolute on load.</summary>
public sealed class DrawImageImageEffect : DrawingImageEffectBase
{
    public override string Id => "draw_image";
    public override string Name => "Image";

    public string ImageLocation { get; set; } = string.Empty;
    public TextPlacement Placement { get; set; } = TextPlacement.TopLeft;

    [EffectParameter(-2000, 2000, DisplayName = "Offset X")]
    public int OffsetX { get; set; }

    [EffectParameter(-2000, 2000, DisplayName = "Offset Y")]
    public int OffsetY { get; set; }

    [EffectParameter(0, 100, DisplayName = "Opacity")]
    public int Opacity { get; set; } = 100;

    [EffectParameter(0, 4000, DisplayName = "Width (0 = auto)")]
    public int Width { get; set; }

    [EffectParameter(0, 4000, DisplayName = "Height (0 = auto)")]
    public int Height { get; set; }

    public override SKBitmap Apply(SKBitmap source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrEmpty(ImageLocation) || !System.IO.File.Exists(ImageLocation)) return source.Copy();
        if (Opacity <= 0) return source.Copy();

        SKBitmap? overlay;
        try { overlay = SKBitmap.Decode(ImageLocation); }
        catch { return source.Copy(); }
        if (overlay is null) return source.Copy();

        try
        {
            // Resize when at least one dimension is explicitly set. Width=0/Height=0 keeps
            // the original on that axis (the user might want "fit to height, keep aspect").
            if (Width > 0 || Height > 0)
            {
                var w = Width > 0 ? Width : overlay.Width * (Height > 0 ? Height / (float)overlay.Height : 1f);
                var h = Height > 0 ? Height : overlay.Height * (Width > 0 ? Width / (float)overlay.Width : 1f);
                using var resized = overlay.Resize(new SKSizeI((int)w, (int)h), SKSamplingOptions.Default);
                if (resized is null) return source.Copy();
                overlay.Dispose();
                overlay = resized.Copy();
            }

            var anchor = ResolveAnchor(source.Width, source.Height, overlay.Width, overlay.Height);
            var result = source.Copy();
            using var canvas = new SKCanvas(result);
            using var paint = new SKPaint { Color = new SKColor(255, 255, 255, (byte)(255 * Opacity / 100)) };
            canvas.DrawBitmap(overlay, anchor.X, anchor.Y, paint);
            return result;
        }
        finally
        {
            overlay?.Dispose();
        }
    }

    private SKPoint ResolveAnchor(int canvasW, int canvasH, int imageW, int imageH)
    {
        // Offset is "distance from the anchor edge" — for *Right OffsetX pushes LEFT, for
        // Bottom* OffsetY pushes UP. Mirrors ShareX Helpers.GetPosition exactly so .sxie
        // packages with non-zero offsets land where their authors intended. Same convention
        // we already adopted in DrawTextExImageEffect.
        switch (Placement)
        {
            case TextPlacement.TopLeft:
                return new SKPoint(OffsetX, OffsetY);
            case TextPlacement.TopCenter:
                return new SKPoint((canvasW - imageW) / 2, OffsetY);
            case TextPlacement.TopRight:
                return new SKPoint(canvasW - imageW - OffsetX, OffsetY);
            case TextPlacement.MiddleLeft:
                return new SKPoint(OffsetX, (canvasH - imageH) / 2);
            case TextPlacement.MiddleCenter:
                return new SKPoint((canvasW - imageW) / 2, (canvasH - imageH) / 2);
            case TextPlacement.MiddleRight:
                return new SKPoint(canvasW - imageW - OffsetX, (canvasH - imageH) / 2);
            case TextPlacement.BottomLeft:
                return new SKPoint(OffsetX, canvasH - imageH - OffsetY);
            case TextPlacement.BottomCenter:
                return new SKPoint((canvasW - imageW) / 2, canvasH - imageH - OffsetY);
            case TextPlacement.BottomRight:
                return new SKPoint(canvasW - imageW - OffsetX, canvasH - imageH - OffsetY);
            default:
                return new SKPoint(OffsetX, OffsetY);
        }
    }
}
