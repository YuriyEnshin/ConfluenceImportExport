using System.Net;
using System.Net.Http;
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
    /// Maps an exception thrown by a service / handler to a stable error code.
    /// Unknown exceptions become <c>INTERNAL</c> with the raw message; we
    /// intentionally surface the message so agents can include it in their
    /// reasoning, but no stack traces are leaked.
    /// </summary>
    public static (string Code, string Message) Classify(Exception ex)
    {
        return ex switch
        {
            OutOfSandboxException => ("OUT_OF_SANDBOX", ex.Message),
            UnauthorizedAccessException => ("AUTH_FAILED", ex.Message),
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.Unauthorized => ("AUTH_FAILED", hre.Message),
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.Forbidden => ("AUTH_FAILED", hre.Message),
            HttpRequestException hre when hre.StatusCode == HttpStatusCode.NotFound => ("PAGE_NOT_FOUND", hre.Message),
            DirectoryNotFoundException => ("DIRECTORY_NOT_FOUND", ex.Message),
            FileNotFoundException => ("FILE_NOT_FOUND", ex.Message),
            ArgumentException => ("INVALID_ARGS", ex.Message),
            InvalidOperationException when ex.Message.Contains("Could not resolve page", StringComparison.OrdinalIgnoreCase)
                => ("PAGE_NOT_FOUND", ex.Message),
            InvalidOperationException => ("INVALID_STATE", ex.Message),
            IOException => ("IO_ERROR", ex.Message),
            _ => ("INTERNAL", ex.Message),
        };
    }
}
