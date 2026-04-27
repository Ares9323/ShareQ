using ShareQ.Editor.HitTesting;
using ShareQ.Editor.Model;
using Xunit;

namespace ShareQ.Editor.Tests.HitTesting;

public class HitTesterAnnotationTests
{
    [Fact]
    public void TextShape_is_hit_inside_approximate_bounding_box()
    {
        var t = new TextShape(0, 0, "Hello",
            new TextStyle("Segoe UI", 20, false, false, ShapeColor.Red, TextAlign.Left),
            ShapeColor.Red, ShapeColor.Transparent, 1);
        Assert.True(ShapeHitTester.IsHit(t, 5, 10));
    }

    [Fact]
    public void TextShape_is_not_hit_far_away()
    {
        var t = new TextShape(0, 0, "Hi", TextStyle.Default, ShapeColor.Red, ShapeColor.Transparent, 1);
        Assert.False(ShapeHitTester.IsHit(t, 500, 500));
    }

    [Fact]
    public void TextShape_multiline_bounds_use_max_line_width_and_total_height()
    {
        // Old (single-line) approx counted "\n" as 1 char and made the bbox a wide ribbon.
        // New approx uses max line length × fontSize × 0.55 × lines.Count for height.
        var t = new TextShape(0, 0, "ab\nabcdef", TextStyle.Default, ShapeColor.Red, ShapeColor.Transparent, 1);
        // FontSize=18: width ≈ 6 * 18 * 0.55 ≈ 59.4, height ≈ 2 * 18 * 1.2 = 43.2.
        Assert.True(ShapeHitTester.IsHit(t, 40, 30));   // inside bbox of 2nd line
        Assert.False(ShapeHitTester.IsHit(t, 95, 30));  // past width
        Assert.False(ShapeHitTester.IsHit(t, 40, 60));  // past height (3rd-line area)
    }

    [Fact]
    public void StepCounter_is_hit_inside_circle()
    {
        var s = new StepCounterShape(100, 100, 20, 1, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.True(ShapeHitTester.IsHit(s, 110, 100));
    }

    [Fact]
    public void StepCounter_is_not_hit_outside_circle()
    {
        var s = new StepCounterShape(100, 100, 20, 1, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.False(ShapeHitTester.IsHit(s, 200, 100));
    }
}
