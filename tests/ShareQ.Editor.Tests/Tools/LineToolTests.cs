using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using Xunit;

namespace ShareQ.Editor.Tests.Tools;

public class LineToolTests
{
    [Fact]
    public void Commit_AfterDrag_ReturnsLineShape()
    {
        var tool = new LineTool();
        tool.Begin(10, 10, ShapeColor.Red, ShapeColor.Transparent, 2);
        tool.Update(50, 60);
        var shape = tool.Commit(50, 60);

        var line = Assert.IsType<LineShape>(shape);
        Assert.Equal(10, line.FromX);
        Assert.Equal(60, line.ToY);
    }

    [Fact]
    public void Commit_TooClose_ReturnsNull()
    {
        var tool = new LineTool();
        tool.Begin(10, 10, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.Null(tool.Commit(11, 11));
    }
}
