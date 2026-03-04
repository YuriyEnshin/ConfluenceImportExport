using ConfluencePageExporter;
using FluentAssertions;

namespace ConfluencePageExporter.Tests;

public class ExceptionHandlingTests
{
    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForDirectoryNotFoundException()
    {
        var ex = new DirectoryNotFoundException("Source directory does not exist: /invalid/path");

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Be("Source directory does not exist: /invalid/path");
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForFileNotFoundException()
    {
        var ex = new FileNotFoundException("No index.html found in source directory: /path");

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Be("No index.html found in source directory: /path");
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForInvalidOperationException()
    {
        var ex = new InvalidOperationException("Page with ID '123' not found in Confluence");

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Be("Page with ID '123' not found in Confluence");
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForArgumentException()
    {
        var ex = new ArgumentException("Invalid value", "paramName");

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Contain("Invalid value");
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForHttpRequestException()
    {
        var ex = new HttpRequestException("Connection failed", new Exception("Connection refused"));

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Be("Connection failed (Connection refused)");
    }

    [Fact]
    public void GetUserFriendlyErrorMessage_ShouldReturnMessage_ForUnknownException()
    {
        var ex = new InvalidTimeZoneException("Custom error");

        var result = ExceptionHandling.GetUserFriendlyErrorMessage(ex);

        result.Should().Be("Custom error");
    }
}
