using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using Xunit;

namespace ShareQ.Editor.Tests.Tools;

public class ArrowToolTests
{
    [Fact]
    public void Commit_AfterDrag_ReturnsArrowShape()
    {
        var tool = new ArrowTool();
        tool.Begin(10, 10, ShapeColor.Red, ShapeColor.Transparent, 2);
        tool.Update(50, 60);
        var shape = tool.Commit(50, 60);

        var arrow = Assert.IsType<ArrowShape>(shape);
        Assert.Equal(10, arrow.FromX);
        Assert.Equal(50, arrow.ToX);
    }

    [Fact]
    public void Commit_TooClose_ReturnsNull()
    {
        var tool = new ArrowTool();
        tool.Begin(10, 10, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.Null(tool.Commit(11, 11));
    }
}
