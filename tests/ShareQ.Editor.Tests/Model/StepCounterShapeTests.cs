using ShareQ.Editor.Model;
using Xunit;

namespace ShareQ.Editor.Tests.Model;

public class StepCounterShapeTests
{
    [Fact]
    public void StepCounter_carries_center_radius_number()
    {
        var s = new StepCounterShape(50, 60, 18, 3, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.Equal(50, s.CenterX);
        Assert.Equal(60, s.CenterY);
        Assert.Equal(18, s.Radius);
        Assert.Equal(3, s.Number);
    }

    [Fact]
    public void StepCounter_record_equality_compares_by_value()
    {
        var a = new StepCounterShape(0, 0, 16, 1, ShapeColor.Red, ShapeColor.Transparent, 2);
        var b = new StepCounterShape(0, 0, 16, 1, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.Equal(a, b);
    }
}
