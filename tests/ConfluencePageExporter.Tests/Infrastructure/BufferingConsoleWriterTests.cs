using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class BufferingConsoleWriterTests
{
    [Fact]
    public void WriteLine_ShouldAccumulateLines()
    {
        var w = new BufferingConsoleWriter();
        w.WriteLine("first");
        w.WriteLine("second");
        w.WriteLine();

        w.Lines.Should().Equal("first", "second", "");
    }

    [Fact]
    public void Write_FollowedByWriteLine_ShouldConcatenateOntoSameLine()
    {
        var w = new BufferingConsoleWriter();
        w.Write("hello ");
        w.Write("world");
        w.WriteLine("!");

        w.Lines.Should().Equal("hello world!");
    }

    [Fact]
    public void Write_WithoutWriteLine_ShouldSurfacePartialLineInSnapshot()
    {
        var w = new BufferingConsoleWriter();
        w.WriteLine("done");
        w.Write("partial");

        w.Lines.Should().Equal("done", "partial");
    }

    [Fact]
    public void Lines_ShouldBeEmpty_OnFreshInstance()
    {
        new BufferingConsoleWriter().Lines.Should().BeEmpty();
    }
}
