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
    Task<string?> CreatePageAsync(string spaceKey, string? parentId, string title, string content);
    Task<string?> UpdatePageAsync(string pageId, string title, string content, string? parentId);
    Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName);
    Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId);
    Task<byte[]> DownloadAttachmentAsync(string downloadUrl);
    Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10);
    Task<PageData?> GetPageAtVersionAsync(string pageId, int versionNumber);
}
