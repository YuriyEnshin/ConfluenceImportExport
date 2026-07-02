using System.Net;
using System.Net.Http.Headers;

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

    private const string DefaultAttachmentMediaType = "application/octet-stream";

    /// <summary>
    /// Minimal extension → media-type map for the attachment upload path.
    /// Confluence infers an attachment's media type from the multipart part's
    /// Content-Type; when we send none it falls back to the filename
    /// extension, which has no answer for extensionless files. This covers
    /// the common attachment kinds so a new attachment keeps its proper type,
    /// with <see cref="DefaultAttachmentMediaType"/> as the catch-all.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> ExtensionMediaTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".bmp"] = "image/bmp",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".pdf"] = "application/pdf",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".xml"] = "application/xml",
            [".json"] = "application/json",
            [".html"] = "text/html",
            [".htm"] = "text/html",
            [".zip"] = "application/zip",
            [".drawio"] = "application/vnd.jgraph.mxfile",
        };

    /// <summary>
    /// Resolves the media type to stamp on an uploaded attachment part.
    /// Prefers the caller-supplied type (on update: the existing attachment's
    /// stored server type, so the update never changes it), then the extension
    /// map, and finally <see cref="DefaultAttachmentMediaType"/> for
    /// extensionless/unknown files.
    /// </summary>
    private static string ResolveAttachmentContentType(string fileName, string? preferredMediaType)
    {
        if (!string.IsNullOrWhiteSpace(preferredMediaType))
            return preferredMediaType;

        var ext = Path.GetExtension(fileName);
        if (!string.IsNullOrEmpty(ext) && ExtensionMediaTypes.TryGetValue(ext, out var mapped))
            return mapped;

        return DefaultAttachmentMediaType;
    }

    /// <summary>
    /// Sets the multipart file part's Content-Type from
    /// <see cref="ResolveAttachmentContentType"/>, falling back to
    /// <see cref="DefaultAttachmentMediaType"/> if the resolved value is not a
    /// valid media-type header (e.g. an odd server-reported value).
    /// </summary>
    internal static void ApplyAttachmentContentType(ByteArrayContent part, string fileName, string? preferredMediaType)
    {
        var resolved = ResolveAttachmentContentType(fileName, preferredMediaType);
        if (!MediaTypeHeaderValue.TryParse(resolved, out var parsed))
            MediaTypeHeaderValue.TryParse(DefaultAttachmentMediaType, out parsed);
        part.Headers.ContentType = parsed;
    }
}
