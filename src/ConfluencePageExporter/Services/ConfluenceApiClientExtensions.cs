namespace ConfluencePageExporter.Services;

public static class ConfluenceApiClientExtensions
{
    /// <summary>
    /// Resolves a page id from an explicit id or, failing that, a title lookup
    /// in the given space. Returns null when neither identifier is provided or
    /// no page carries the title.
    /// </summary>
    public static async Task<string?> ResolvePageIdAsync(
        this IConfluenceApiClient apiClient,
        string spaceKey,
        string? pageId,
        string? pageTitle,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(pageId))
            return pageId;

        if (!string.IsNullOrEmpty(pageTitle))
            return await apiClient.FindPageByTitleAsync(spaceKey, null, pageTitle, ct);

        return null;
    }
}
