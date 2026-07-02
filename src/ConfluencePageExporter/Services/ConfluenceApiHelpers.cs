using System.Net;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Plumbing shared by the API client implementations (Server/DC v1 and
/// Cloud). Keeps the error contract identical across deployments: a
/// non-success status surfaces as a typed <see cref="ConfluenceApiException"/>
/// (409 → <see cref="ConfluenceConflictException"/>) carrying the status code
/// and a trimmed response-body snippet, so callers can react uniformly (e.g.
/// <see cref="ConfluenceApiException.IsAuthFailure"/> aborts multi-tree
/// batches regardless of which client produced it).
/// </summary>
internal static class ConfluenceApiHelpers
{
    internal static ConfluenceApiException CreateException(HttpStatusCode status, string responseBody, string context)
    {
        var trimmed = responseBody?.Trim() ?? string.Empty;
        var snippet = trimmed.Length == 0
            ? string.Empty
            : " — " + (trimmed.Length > 500 ? trimmed[..500] + "…" : trimmed);
        var message = $"{context}: HTTP {(int)status} {status}{snippet}";
        return status == HttpStatusCode.Conflict
            ? new ConfluenceConflictException(message, responseBody)
            : new ConfluenceApiException(status, message, responseBody);
    }

    internal static async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errorContent = await response.Content.ReadAsStringAsync(ct);
        throw CreateException(response.StatusCode, errorContent, context);
    }

    /// <summary>
    /// Escapes a value for safe interpolation into a double-quoted CQL string
    /// literal. CQL uses backslash escaping inside quotes, so a space key or
    /// title containing <c>"</c> or <c>\</c> (both legal in page titles) would
    /// otherwise break the query or silently change its meaning.
    /// </summary>
    internal static string EscapeCql(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
