using System.Text.Json;
using ConfluencePageExporter.Infrastructure;
using Shouldly;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class ToolExceptionFilterTests
{
    [Fact]
    public async Task Handle_ShouldPassThroughSuccessfulResult()
    {
        var expected = new CallToolResult
        {
            Content = [new TextContentBlock { Text = "ok" }],
            IsError = false,
        };
        McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, _) => ValueTask.FromResult(expected);

        var handler = ToolExceptionFilter.Handle(next);
        var result = await handler(CreateContext("confluence_ping"), CancellationToken.None);

        result.ShouldBeSameAs(expected);
    }

    [Fact]
    public async Task Handle_ShouldCatchArgumentException_AndReturnEnvelope()
    {
        McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, _) =>
            throw new ArgumentException("The arguments dictionary is missing a value for the required parameter 'pageTitle'.");

        var handler = ToolExceptionFilter.Handle(next);
        var result = await handler(CreateContext("confluence_compare"), CancellationToken.None);

        result.IsError.ShouldBe(true);
        result.Content.ShouldHaveSingleItem();

        var text = (result.Content[0] as TextContentBlock)!.Text;
        var envelope = JsonDocument.Parse(text).RootElement;
        envelope.GetProperty("success").GetBoolean().ShouldBeFalse();
        envelope.GetProperty("errorCode").GetString().ShouldBe("INVALID_ARGS");
        envelope.GetProperty("error").GetString()!.ShouldContain("pageTitle");
    }

    [Fact]
    public async Task Handle_ShouldCatchHttpRequestException_AndReturnNetworkError()
    {
        McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, _) =>
            throw new System.Net.Http.HttpRequestException("Connection refused");

        var handler = ToolExceptionFilter.Handle(next);
        var result = await handler(CreateContext("confluence_get_page_content"), CancellationToken.None);

        result.IsError.ShouldBe(true);
        var text = (result.Content[0] as TextContentBlock)!.Text;
        var envelope = JsonDocument.Parse(text).RootElement;
        envelope.GetProperty("errorCode").GetString().ShouldBe("NETWORK_ERROR");
    }

    [Fact]
    public async Task Handle_ShouldRethrowOperationCanceled_WhenTokenCancelled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new CallToolResult());
        };

        var handler = ToolExceptionFilter.Handle(next);

        var act = () => handler(CreateContext("confluence_ping"), cts.Token).AsTask();
        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task Handle_ShouldCatchOutOfSandboxException()
    {
        McpRequestHandler<CallToolRequestParams, CallToolResult> next = (_, _) =>
            throw new OutOfSandboxException("Path '/etc/passwd' resolves outside sandbox root '/data'.");

        var handler = ToolExceptionFilter.Handle(next);
        var result = await handler(CreateContext("confluence_download_update"), CancellationToken.None);

        result.IsError.ShouldBe(true);
        var text = (result.Content[0] as TextContentBlock)!.Text;
        var envelope = JsonDocument.Parse(text).RootElement;
        envelope.GetProperty("errorCode").GetString().ShouldBe("OUT_OF_SANDBOX");
        envelope.GetProperty("error").GetString()!.ShouldContain("/etc/passwd");
    }

    private static RequestContext<CallToolRequestParams> CreateContext(string toolName)
    {
        var server = new Mock<McpServer>() { CallBase = false }.Object;
        return new RequestContext<CallToolRequestParams>(
            server, new JsonRpcRequest { Method = "tools/call" }, new CallToolRequestParams { Name = toolName });
    }
}
