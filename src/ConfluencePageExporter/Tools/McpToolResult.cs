using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Infrastructure;

namespace ConfluencePageExporter.Tools;

/// <summary>
/// Uniform JSON envelope returned by every MCP tool. Errors are returned as
/// values rather than thrown so that agents see one stable shape regardless
/// of failure mode.
/// </summary>
public static class McpToolResult
{
    public static object Success(string summary, object? report, IReadOnlyList<string> logs) => new
    {
        success = true,
        summary,
        report,
        logs,
    };

    public static object Error(string errorCode, string message, IReadOnlyList<string> logs) => new
    {
        success = false,
        errorCode,
        error = message,
        logs,
    };

    /// <summary>
    /// Convenience wrapper used by every <c>catch</c> block in MCP tool methods:
    /// classifies the exception, writes a full diagnostic (including the inner-
    /// exception chain and stack trace) to stderr via the logger, and returns
    /// the error envelope the agent will see.
    /// </summary>
    /// <remarks>
    /// Logging the full exception to stderr is intentional defense-in-depth.
    /// Even if the envelope were swallowed or reshaped somewhere down the MCP
    /// transport, the operator can still find the root cause in the MCP host's
    /// log file (Claude Code / Cursor / Codex all capture child-process stderr).
    /// The agent, in turn, gets a richer message than bare
    /// <see cref="Exception.Message"/> — inner exceptions are flattened in so
    /// that things like "see inner exception" become readable.
    /// </remarks>
    public static object FromException(
        Exception ex,
        ILoggerFactory loggerFactory,
        IReadOnlyList<string> logs,
        [CallerMemberName] string toolName = "")
    {
        var (code, message) = Classify(ex);
        var logger = loggerFactory.CreateLogger("ConfluencePageExporter.Tools.ConfluenceMcpTools");
        logger.LogError(ex, "MCP tool {ToolName} failed: {Code} — {Message}", toolName, code, message);
        return Error(code, message, logs);
    }

    /// <summary>
    /// Maps an exception thrown by a service / handler to a stable error code,
    /// and produces a flattened message that walks the
    /// <see cref="Exception.InnerException"/> chain. We intentionally surface
    /// the message so agents can include it in their reasoning, but no stack
    /// traces are leaked through the envelope.
    /// </summary>
    public static (string Code, string Message) Classify(Exception ex)
    {
        var code = ex switch
        {
            OutOfSandboxException => "OUT_OF_SANDBOX",
            UnauthorizedAccessException => "AUTH_FAILED",
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.Unauthorized => "AUTH_FAILED",
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.Forbidden => "AUTH_FAILED",
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.NotFound => "PAGE_NOT_FOUND",
            HttpRequestException => "NETWORK_ERROR",
            DirectoryNotFoundException => "DIRECTORY_NOT_FOUND",
            FileNotFoundException => "FILE_NOT_FOUND",
            ArgumentException => "INVALID_ARGS",
            InvalidOperationException ioe when ioe.Message.Contains("Could not resolve page", StringComparison.OrdinalIgnoreCase)
                => "PAGE_NOT_FOUND",
            InvalidOperationException => "INVALID_STATE",
            IOException => "IO_ERROR",
            _ => "INTERNAL",
        };
        return (code, FlattenMessage(ex));
    }

    /// <summary>
    /// Joins <see cref="Exception.Message"/> with every non-empty inner
    /// exception's message via " → ", so callers see the actual cause instead
    /// of generic wrappers like "The SSL connection could not be established,
    /// see inner exception.". Adjacent duplicates are deduplicated and the
    /// chain is capped at 8 levels to avoid pathological cycles.
    /// </summary>
    private static string FlattenMessage(Exception ex)
    {
        var sb = new StringBuilder(ex.Message);
        var previous = ex.Message;
        var inner = ex.InnerException;
        var depth = 0;
        while (inner != null && depth < 8)
        {
            if (!string.IsNullOrEmpty(inner.Message)
                && !string.Equals(inner.Message, previous, StringComparison.Ordinal))
            {
                sb.Append(" → ").Append(inner.Message);
                previous = inner.Message;
            }
            inner = inner.InnerException;
            depth++;
        }
        return sb.ToString();
    }
}
