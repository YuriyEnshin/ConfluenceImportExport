using System.Net;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Raised when a Confluence API call returns a non-success HTTP status —
/// writes (create/update) and reads (fetch/search) alike. Carries the status
/// code and a snippet of the response body so inbound adapters (CLI / MCP)
/// can classify the failure into a stable error contract instead of seeing
/// an undifferentiated <c>null</c> or a bare <see cref="HttpRequestException"/>.
/// </summary>
public class ConfluenceApiException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string? ResponseBody { get; }

    public ConfluenceApiException(
        HttpStatusCode? statusCode,
        string message,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    /// <summary>
    /// 401/403 are global, non-recoverable failures (bad or insufficient
    /// credentials): a per-page sync loop should abort rather than keep
    /// hammering the server. Distinguished here so callers don't hard-code
    /// status checks.
    /// </summary>
    public bool IsAuthFailure => StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;
}

/// <summary>
/// Raised when Confluence rejects a page update with <c>409 Conflict</c> —
/// the page's version moved underneath us (a concurrent edit on the server).
/// For a sync tool this is a first-class, recoverable outcome: the caller
/// records it as a conflict in the report and continues with the rest of the
/// tree rather than aborting the whole operation.
/// </summary>
public sealed class ConfluenceConflictException : ConfluenceApiException
{
    public ConfluenceConflictException(
        string message,
        string? responseBody = null,
        Exception? innerException = null)
        : base(HttpStatusCode.Conflict, message, responseBody, innerException)
    {
    }
}
