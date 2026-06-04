using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class ArgValidationTests
{
    [Fact]
    public void RequireExactlyOne_ShouldPass_WhenExactlyOneIsSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", "x"), ("b", null));
        Should.NotThrow(act);
    }

    [Fact]
    public void RequireExactlyOne_ShouldThrow_WhenNoneIsSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", null), ("b", ""));
        Should.Throw<ArgumentException>(act).Message.ShouldMatch("a.*b");
    }

    [Fact]
    public void RequireExactlyOne_ShouldThrow_WhenBothAreSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", "x"), ("b", "y"));
        Should.Throw<ArgumentException>(act).Message.ShouldContain("mutually exclusive");
    }

    [Fact]
    public void RequireAtMostOne_ShouldPass_WhenNoneIsSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", null), ("b", null));
        Should.NotThrow(act);
    }

    [Fact]
    public void RequireAtMostOne_ShouldPass_WhenOneIsSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", "x"), ("b", null));
        Should.NotThrow(act);
    }

    [Fact]
    public void RequireAtMostOne_ShouldThrow_WhenBothAreSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", "x"), ("b", "y"));
        Should.Throw<ArgumentException>(act).Message.ShouldContain("mutually exclusive");
    }
}
