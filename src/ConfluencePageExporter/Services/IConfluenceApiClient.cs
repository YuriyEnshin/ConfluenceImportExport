using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Interface for Confluence API client operations
/// </summary>
public interface IConfluenceApiClient
{
    Task<PageData> GetPageByIdAsync(string pageId, CancellationToken ct = default);

    /// <summary>
    /// Returns the page, or <c>null</c> strictly when it does not exist (404).
    /// Any other API failure throws <see cref="ConfluenceApiException"/>.
    /// </summary>
    Task<PageData?> TryGetPageByIdAsync(string pageId, CancellationToken ct = default);
    Task<List<PageData>> GetChildrenPagesAsync(string parentId, CancellationToken ct = default);
    Task<List<AttachmentData>> GetAttachmentsAsync(string pageId, CancellationToken ct = default);

    /// <summary>
    /// Returns the found page's id, or <c>null</c> strictly when no page with
    /// this title exists. Any API failure throws <see cref="ConfluenceApiException"/>
    /// rather than returning <c>null</c> — callers treat <c>null</c> as "safe to
    /// create", so a masked error would risk creating a duplicate page.
    /// </summary>
    Task<string?> FindPageByTitleAsync(string spaceKey, string? parentId, string title, CancellationToken ct = default);
    /// <summary>
    /// Creates a page. Returns the created page's id/version, or throws
    /// <see cref="ConfluenceApiException"/> (incl. <see cref="ConfluenceConflictException"/>
    /// for 409) when Confluence rejects the request.
    /// </summary>
    Task<PageUpdateResult> CreatePageAsync(string spaceKey, string? parentId, string title, string content, CancellationToken ct = default);

    /// <summary>
    /// Updates a page (content/title/parent). Returns the new id/version, or
    /// throws <see cref="ConfluenceApiException"/> on failure —
    /// <see cref="ConfluenceConflictException"/> specifically for 409 version
    /// conflicts, which callers surface as a conflict rather than aborting.
    /// </summary>
    Task<PageUpdateResult> UpdatePageAsync(string pageId, string title, string content, string? parentId, CancellationToken ct = default);
    Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName, CancellationToken ct = default);
    Task<bool> UpdateAttachmentDataAsync(string pageId, string attachmentId, string filePath, string fileName, CancellationToken ct = default);
    Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId, CancellationToken ct = default);
    Task<byte[]> DownloadAttachmentAsync(string downloadUrl, CancellationToken ct = default);
    Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10, CancellationToken ct = default);
    Task<PageData?> GetPageAtVersionAsync(string pageId, int versionNumber, CancellationToken ct = default);

    /// <summary>
    /// Like <see cref="GetPageAtVersionAsync"/>, but expands <c>body.storage</c>
    /// so the caller gets the actual page content for the historical version.
    /// Separated from the lightweight version because hot callers
    /// (<c>ChangeSourceAnalyzer</c>) walk many historical versions only for
    /// metadata and don't need the payload.
    /// </summary>
    Task<PageData> GetPageContentAtVersionAsync(string pageId, int versionNumber, CancellationToken ct = default);

    /// <summary>
    /// Returns the authenticated user. Used by the MCP <c>confluence_ping</c>
    /// diagnostic to verify connectivity and credentials with a single
    /// lightweight call to <c>/rest/api/user/current</c>.
    /// </summary>
    Task<ConfluenceUser> GetCurrentUserAsync(CancellationToken ct = default);
}
