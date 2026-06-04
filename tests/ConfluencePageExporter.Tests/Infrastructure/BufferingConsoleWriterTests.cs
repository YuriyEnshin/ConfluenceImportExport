using ConfluencePageExporter.Infrastructure;
using Shouldly;

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

        w.Lines.ShouldBe(["first", "second", ""]);
    }

    [Fact]
    public void Write_FollowedByWriteLine_ShouldConcatenateOntoSameLine()
    {
        var w = new BufferingConsoleWriter();
        w.Write("hello ");
        w.Write("world");
        w.WriteLine("!");

        w.Lines.ShouldBe(["hello world!"]);
    }

    [Fact]
    public void Write_WithoutWriteLine_ShouldSurfacePartialLineInSnapshot()
    {
        var w = new BufferingConsoleWriter();
        w.WriteLine("done");
        w.Write("partial");

        w.Lines.ShouldBe(["done", "partial"]);
    }

    [Fact]
    public void Lines_ShouldBeEmpty_OnFreshInstance()
    {
        new BufferingConsoleWriter().Lines.ShouldBeEmpty();
    }
}
