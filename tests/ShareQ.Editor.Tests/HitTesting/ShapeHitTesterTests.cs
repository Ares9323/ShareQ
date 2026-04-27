using ShareQ.Editor.HitTesting;
using ShareQ.Editor.Model;
using Xunit;

namespace ShareQ.Editor.Tests.HitTesting;

public class ShapeHitTesterTests
{
    private static readonly ShapeColor Red = ShapeColor.Red;
    private static readonly ShapeColor Tx = ShapeColor.Transparent;

    [Fact]
    public void RectangleFill_HitsInsideBody()
    {
        var r = new RectangleShape(10, 10, 100, 50, Red, Red, 2);
        Assert.True(ShapeHitTester.IsHit(r, 50, 30));
    }

    [Fact]
    public void RectangleNoFill_OnlyHitsStroke()
    {
        var r = new RectangleShape(10, 10, 100, 50, Red, Tx, 2);
        Assert.True(ShapeHitTester.IsHit(r, 11, 30));
        Assert.False(ShapeHitTester.IsHit(r, 50, 30));
    }

    [Fact]
    public void Line_HitsNearSegment()
    {
        var l = new LineShape(0, 0, 100, 0, Red, 4);
        Assert.True(ShapeHitTester.IsHit(l, 50, 1));
        Assert.False(ShapeHitTester.IsHit(l, 50, 50));
    }

    [Fact]
    public void Ellipse_FilledHitsInside()
    {
        var e = new EllipseShape(0, 0, 100, 50, Red, Red, 2);
        Assert.True(ShapeHitTester.IsHit(e, 50, 25));
    }

    [Fact]
    public void Ellipse_NoFill_OnlyHitsStroke()
    {
        var e = new EllipseShape(0, 0, 100, 50, Red, Tx, 2);
        Assert.True(ShapeHitTester.IsHit(e, 100, 25));
        Assert.False(ShapeHitTester.IsHit(e, 50, 25));
    }

    [Fact]
    public void HitTest_TopShapeWins()
    {
        var lower = new RectangleShape(0, 0, 100, 100, Red, Red, 2);
        var upper = new RectangleShape(20, 20, 30, 30, Red, ShapeColor.Black, 2);
        var shapes = new List<Shape> { lower, upper };

        var hit = ShapeHitTester.HitTest(shapes, 30, 30);

        Assert.Equal(upper, hit);
    }
}
