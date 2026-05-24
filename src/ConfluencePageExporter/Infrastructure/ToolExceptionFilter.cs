using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using ConfluencePageExporter.Tools;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// MCP call-tool filter that catches unhandled exceptions escaping from the
/// SDK's parameter-binding / invocation pipeline and converts them into the
/// same structured JSON envelope (<see cref="McpToolResult"/>) that tool
/// methods produce from their own <c>catch</c> blocks.
/// <para>
/// Without this filter the SDK returns a generic
/// <c>"An error occurred invoking 'tool_name'."</c> message with
/// <c>IsError = true</c>, hiding the actual exception from the agent.
/// This is a known limitation of the ModelContextProtocol C# SDK —
/// non-<c>McpException</c> exceptions are sanitised to prevent leaking
/// internal details. We deliberately opt in to exposing classified error
/// info because our agents need actionable diagnostics.
/// </para>
/// </summary>
public static class ToolExceptionFilter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Handle(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, cancellationToken) =>
        {
            try
            {
                return await next(request, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var toolName = request.Params?.Name ?? "unknown";
                var logger = request.Services?.GetService<ILoggerFactory>()
                    ?.CreateLogger("ConfluencePageExporter.ToolExceptionFilter");
                logger?.LogError(ex, "Unhandled exception in tool '{ToolName}' caught by filter", toolName);

                var (code, message) = McpToolResult.Classify(ex);
                var envelope = new
                {
                    success = false,
                    errorCode = code,
                    error = message,
                    logs = Array.Empty<string>(),
                };

                return new CallToolResult
                {
                    Content = [new TextContentBlock { Text = JsonSerializer.Serialize(envelope, JsonOptions) }],
                    IsError = true,
                };
            }
        };
}
