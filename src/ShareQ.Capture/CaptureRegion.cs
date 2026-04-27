namespace ShareQ.Capture;

/// <summary>A rectangle in virtual-screen coordinates. <see cref="Width"/> and <see cref="Height"/> are positive.</summary>
public sealed record CaptureRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
}
