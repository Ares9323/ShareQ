using ShareQ.Editor.Model;
using ShareQ.Editor.Tools;
using Xunit;

namespace ShareQ.Editor.Tests.Tools;

public class EllipseToolTests
{
    [Fact]
    public void Commit_AfterDrag_ReturnsEllipseShape()
    {
        var tool = new EllipseTool();
        tool.Begin(20, 20, ShapeColor.Red, ShapeColor.Transparent, 2);
        tool.Update(80, 60);
        var shape = tool.Commit(80, 60);

        var e = Assert.IsType<EllipseShape>(shape);
        Assert.Equal(60, e.Width);
        Assert.Equal(40, e.Height);
    }

    [Fact]
    public void Commit_ZeroSize_ReturnsNull()
    {
        var tool = new EllipseTool();
        tool.Begin(20, 20, ShapeColor.Red, ShapeColor.Transparent, 2);
        Assert.Null(tool.Commit(20, 20));
    }
}
