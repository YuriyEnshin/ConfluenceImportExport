using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Tests.Helpers;
using ConfluencePageExporter.Tools;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Tools;

/// <summary>
/// Tests for the error-classification + message-flattening logic that backs
/// every MCP tool's <c>catch</c> block. The shape that reaches the agent is
/// only as informative as <see cref="McpToolResult.Classify"/> and
/// <see cref="McpToolResult.FromException"/> make it.
/// </summary>
public class McpToolResultTests
{
    [Fact]
    public void Classify_ShouldFlattenInnerExceptionChain()
    {
        // Mimics the real failure mode reported from production:
        //   HttpRequestException
        //     → IOException ("Authentication failed because...")
        //       → SocketException ("Received an unexpected EOF...")
        var sock = new SocketException((int)SocketError.ConnectionAborted, "Received an unexpected EOF from the transport stream.");
        var io   = new IOException("Authentication failed because the remote party closed the transport.", sock);
        var http = new HttpRequestException("The SSL connection could not be established, see inner exception.", io);

        var (code, message) = McpToolResult.Classify(http);

        code.Should().Be("NETWORK_ERROR");
        message.Should().Contain("The SSL connection could not be established");
        message.Should().Contain("Authentication failed because");
        message.Should().Contain("Received an unexpected EOF");
        message.Should().Contain("→");
    }

    [Fact]
    public void Classify_ShouldMapHttp401_ToAuthFailed()
    {
        var ex = new HttpRequestException("Unauthorized", inner: null, HttpStatusCode.Unauthorized);
        var (code, _) = McpToolResult.Classify(ex);
        code.Should().Be("AUTH_FAILED");
    }

    [Fact]
    public void Classify_ShouldMapHttp404_ToPageNotFound()
    {
        var ex = new HttpRequestException("Not Found", inner: null, HttpStatusCode.NotFound);
        var (code, _) = McpToolResult.Classify(ex);
        code.Should().Be("PAGE_NOT_FOUND");
    }

    [Fact]
    public void Classify_ShouldMapNakedHttpRequestException_ToNetworkError()
    {
        // HttpRequestException with no StatusCode is what you get from
        // genuine network failures (DNS, TCP refusal, SSL EOF, etc.).
        var ex = new HttpRequestException("Some network failure");
        var (code, _) = McpToolResult.Classify(ex);
        code.Should().Be("NETWORK_ERROR");
    }

    [Fact]
    public void Classify_ShouldMapOutOfSandbox()
    {
        var ex = new OutOfSandboxException("Path escapes sandbox root");
        var (code, message) = McpToolResult.Classify(ex);
        code.Should().Be("OUT_OF_SANDBOX");
        message.Should().Be("Path escapes sandbox root");
    }

    [Fact]
    public void Classify_ShouldNotDuplicate_WhenInnerMessageEqualsOuter()
    {
        var inner = new InvalidOperationException("Same message");
        var outer = new InvalidOperationException("Same message", inner);

        var (_, message) = McpToolResult.Classify(outer);

        message.Should().Be("Same message");
        message.Should().NotContain("→");
    }

    [Fact]
    public void FromException_ShouldReturnErrorEnvelope_WithFlattenedMessage()
    {
        var inner = new IOException("inner cause");
        var outer = new HttpRequestException("outer wrapper", inner);

        var result = McpToolResult.FromException(outer, LoggerTestHelper.CreateLoggerFactory(), new List<string>());

        var success = result.GetType().GetProperty("success")!.GetValue(result);
        var errorCode = result.GetType().GetProperty("errorCode")!.GetValue(result) as string;
        var error = result.GetType().GetProperty("error")!.GetValue(result) as string;

        success.Should().Be(false);
        errorCode.Should().Be("NETWORK_ERROR");
        error.Should().Be("outer wrapper → inner cause");
    }
}
