using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class ArgValidationTests
{
    [Fact]
    public void RequireExactlyOne_ShouldPass_WhenExactlyOneIsSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", "x"), ("b", null));
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireExactlyOne_ShouldThrow_WhenNoneIsSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", null), ("b", ""));
        act.Should().Throw<ArgumentException>().WithMessage("*a*b*");
    }

    [Fact]
    public void RequireExactlyOne_ShouldThrow_WhenBothAreSet()
    {
        Action act = () => ArgValidation.RequireExactlyOne(("a", "x"), ("b", "y"));
        act.Should().Throw<ArgumentException>().WithMessage("*mutually exclusive*");
    }

    [Fact]
    public void RequireAtMostOne_ShouldPass_WhenNoneIsSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", null), ("b", null));
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireAtMostOne_ShouldPass_WhenOneIsSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", "x"), ("b", null));
        act.Should().NotThrow();
    }

    [Fact]
    public void RequireAtMostOne_ShouldThrow_WhenBothAreSet()
    {
        Action act = () => ArgValidation.RequireAtMostOne(("a", "x"), ("b", "y"));
        act.Should().Throw<ArgumentException>().WithMessage("*mutually exclusive*");
    }
}
