using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class PathNormalizerTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)] // whitespace-only returns null-ish via IsNullOrWhiteSpace
    [InlineData("  ", null)]
    public void Normalize_ShouldReturnInput_WhenNullOrWhitespace(string? input, string? _)
    {
        PathNormalizer.Normalize(input).Should().Be(input);
    }

    [Theory]
    [InlineData("\"/tmp/My Project\"", "/tmp/My Project")]
    [InlineData("'/tmp/My Project'", "/tmp/My Project")]
    [InlineData("/tmp/My\\ New\\ Project", "/tmp/My New Project")]
    [InlineData("  \"/tmp/A B\"  ", "/tmp/A B")]
    [InlineData("/tmp/normal", "/tmp/normal")]
    [InlineData("\"single\"", "single")]
    [InlineData("'single'", "single")]
    public void Normalize_ShouldHandleQuotesAndEscapedSpaces(string input, string expected)
    {
        PathNormalizer.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_ShouldNotStripMismatchedQuotes()
    {
        PathNormalizer.Normalize("\"mixed'").Should().Be("\"mixed'");
    }

    [Fact]
    public void Normalize_ShouldTrimWhitespace()
    {
        PathNormalizer.Normalize("  /tmp/dir  ").Should().Be("/tmp/dir");
    }
}
