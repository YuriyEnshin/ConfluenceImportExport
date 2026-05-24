using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Interface for Confluence API client operations
/// </summary>
public interface IConfluenceApiClient
{
    Task<PageData> GetPageByIdAsync(string pageId);
    Task<PageData?> TryGetPageByIdAsync(string pageId);
    Task<List<PageData>> GetChildrenPagesAsync(string parentId);
    Task<List<AttachmentData>> GetAttachmentsAsync(string pageId);
    Task<string?> FindPageByTitleAsync(string spaceKey, string? parentId, string title);
    Task<PageUpdateResult?> CreatePageAsync(string spaceKey, string? parentId, string title, string content);
    Task<PageUpdateResult?> UpdatePageAsync(string pageId, string title, string content, string? parentId);
    Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName);
    Task<bool> UpdateAttachmentDataAsync(string pageId, string attachmentId, string filePath, string fileName);
    Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId);
    Task<byte[]> DownloadAttachmentAsync(string downloadUrl);
    Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10);
    Task<PageData?> GetPageAtVersionAsync(string pageId, int versionNumber);

    /// <summary>
    /// Like <see cref="GetPageAtVersionAsync"/>, but expands <c>body.storage</c>
    /// so the caller gets the actual page content for the historical version.
    /// Separated from the lightweight version because hot callers
    /// (<c>ChangeSourceAnalyzer</c>) walk many historical versions only for
    /// metadata and don't need the payload.
    /// </summary>
    Task<PageData> GetPageContentAtVersionAsync(string pageId, int versionNumber);

    /// <summary>
    /// Returns the authenticated user. Used by the MCP <c>confluence_ping</c>
    /// diagnostic to verify connectivity and credentials with a single
    /// lightweight call to <c>/rest/api/user/current</c>.
    /// </summary>
    Task<ConfluenceUser> GetCurrentUserAsync();
}
