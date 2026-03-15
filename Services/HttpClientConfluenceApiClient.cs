using System.Net.Http.Headers;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// HttpClient-based implementation of IConfluenceApiClient
/// This is a fallback implementation that uses direct HTTP calls.
/// Can be replaced with ConfluenceApiV2Client-based implementation when package is available.
/// </summary>
public class HttpClientConfluenceApiClient : IConfluenceApiClient
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly ILogger<HttpClientConfluenceApiClient> _logger;

    public HttpClientConfluenceApiClient(string baseUrl, HttpClient httpClient, ILogger<HttpClientConfluenceApiClient> logger)
    {
        _baseUrl = baseUrl.EndsWith("/") ? baseUrl[..^1] : baseUrl;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public HttpClientConfluenceApiClient(string baseUrl, string username, string authSecret, ILogger<HttpClientConfluenceApiClient> logger, string authType = "onprem")
        : this(baseUrl, new HttpClient(), logger)
    {
        // Basic Auth works the same for both on-prem and cloud.
        var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{authSecret}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
    }

    public async Task<PageData> GetPageByIdAsync(string pageId)
    {
        var url = $"{_baseUrl}/rest/api/content/{pageId}?expand=body.storage,ancestors,version";
        using var response = await _httpClient.GetAsync(url);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<PageData>(content) 
            ?? throw new Exception($"Could not deserialize page with ID {pageId}");
    }

    public async Task<PageData?> TryGetPageByIdAsync(string pageId)
    {
        var url = $"{_baseUrl}/rest/api/content/{pageId}?expand=body.storage,ancestors,version";
        using var response = await _httpClient.GetAsync(url);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<PageData>(content);
    }

    public async Task<List<PageData>> GetChildrenPagesAsync(string parentId)
    {
        var pages = new List<PageData>();
        var start = 0;
        const int limit = 100;

        while (true)
        {
            var url = $"{_baseUrl}/rest/api/content/{parentId}/child/page?limit={limit}&start={start}&expand=body.storage,version";
            using var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageData>>(content) 
                ?? throw new Exception("Could not deserialize children list");

            pages.AddRange(result.Results);

            // Check if there are more pages to fetch
            if (result.Links?.Next == null || result.Results.Count < limit)
            {
                break;
            }

            start += limit;
        }

        return pages;
    }

    public async Task<List<AttachmentData>> GetAttachmentsAsync(string pageId)
    {
        var attachments = new List<AttachmentData>();
        var start = 0;
        const int limit = 100;

        while (true)
        {
            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment?limit={limit}&start={start}";
            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch attachments for page {PageId}. Status code: {StatusCode}", pageId, response.StatusCode);
                break;
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<AttachmentData>>(content) 
                ?? throw new Exception("Could not deserialize attachments list");

            attachments.AddRange(result.Results);

            // Check if there are more attachments to fetch
            if (result.Links?.Next == null || result.Results.Count < limit)
            {
                break;
            }

            start += limit;
        }

        return attachments;
    }

    public async Task<string?> FindPageByTitleAsync(string spaceKey, string? parentId, string title)
    {
        try
        {
            var cqlQuery = $"space=\"{spaceKey}\" AND title=\"{title}\"";
            if (!string.IsNullOrEmpty(parentId))
            {
                cqlQuery += $" AND parent={parentId}";
            }

            var url = $"{_baseUrl}/rest/api/content/search?cql={Uri.EscapeDataString(cqlQuery)}&limit=10";
            _logger.LogDebug("Searching for page with CQL query: {CqlQuery}", cqlQuery);
            _logger.LogDebug("URL: {Url}", url);
            
            using var response = await _httpClient.GetAsync(url);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to search for existing page with title '{Title}'. Status code: {StatusCode}", title, response.StatusCode);
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Error response content: {ErrorContent}", errorContent);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.LogDebug("Search response content: {Content}", content);
            
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageData>>(content) 
                ?? throw new Exception("Could not deserialize search results");

            if (result.Results.Count > 0)
            {
                var foundPage = result.Results[0];
                _logger.LogDebug("Found page with title '{FoundTitle}' and ID {FoundId}", foundPage.Title, foundPage.Id);
                return foundPage.Id;
            }

            _logger.LogDebug("No page found with title '{Title}'", title);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for page with title {Title}", title);
            return null;
        }
    }

    public async Task<string?> CreatePageAsync(string spaceKey, string? parentId, string title, string content)
    {
        try
        {
            object pageData;

            // Add parent if specified
            if (!string.IsNullOrEmpty(parentId))
            {
                pageData = new
                {
                    type = "page",
                    title = title,
                    space = new { key = spaceKey },
                    body = new
                    {
                        storage = new
                        {
                            value = content,
                            representation = "storage"
                        }
                    },
                    ancestors = new[] { new { id = parentId } }
                };
            }
            else
            {
                pageData = new
                {
                    type = "page",
                    title = title,
                    space = new { key = spaceKey },
                    body = new
                    {
                        storage = new
                        {
                            value = content,
                            representation = "storage"
                        }
                    }
                };
            }

            var json = JsonConvert.SerializeObject(pageData);
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_baseUrl}/rest/api/content";
            using var response = await _httpClient.PostAsync(url, stringContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create page '{Title}'. Status code: {StatusCode}, Error: {Error}", title, response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<PageResponse>(responseContent) 
                ?? throw new Exception("Could not deserialize page creation response");
            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating page with title {Title}", title);
            return null;
        }
    }

    public async Task<string?> UpdatePageAsync(string pageId, string title, string content, string? parentId)
    {
        try
        {
            // First get the current page version
            var getPageUrl = $"{_baseUrl}/rest/api/content/{pageId}?expand=version";
            using var getResponse = await _httpClient.GetAsync(getPageUrl);
            getResponse.EnsureSuccessStatusCode();
            var getPageContent = await getResponse.Content.ReadAsStringAsync();
            var currentPage = JsonConvert.DeserializeObject<PageResponse>(getPageContent) 
                ?? throw new Exception("Could not deserialize current page");

            var version = currentPage.Version?.Number ?? 1;
            version++; // Increment version for update

            object pageData;

            // Add parent if specified
            if (!string.IsNullOrEmpty(parentId))
            {
                pageData = new
                {
                    id = pageId,
                    type = "page",
                    title = title,
                    body = new
                    {
                        storage = new
                        {
                            value = content,
                            representation = "storage"
                        }
                    },
                    ancestors = new[] { new { id = parentId } },
                    version = new { number = version }
                };
            }
            else
            {
                pageData = new
                {
                    id = pageId,
                    type = "page",
                    title = title,
                    body = new
                    {
                        storage = new
                        {
                            value = content,
                            representation = "storage"
                        }
                    },
                    version = new { number = version }
                };
            }

            var json = JsonConvert.SerializeObject(pageData);
            var stringContent = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{_baseUrl}/rest/api/content/{pageId}";
            using var response = await _httpClient.PutAsync(url, stringContent);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to update page '{Title}' (ID: {PageId}). Status code: {StatusCode}, Error: {Error}", title, pageId, response.StatusCode, errorContent);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<PageResponse>(responseContent) 
                ?? throw new Exception("Could not deserialize page update response");
            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating page with ID {PageId} and title {Title}", pageId, title);
            return null;
        }
    }

    public async Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName)
    {
        try
        {
            var fileContent = await File.ReadAllBytesAsync(filePath);
            using var content = new MultipartFormDataContent();

            // Add file content
            var fileContentPart = new ByteArrayContent(fileContent);
            content.Add(fileContentPart, "file", fileName);

            // Add comment
            var commentContent = new StringContent($"Uploaded attachment: {fileName}");
            content.Add(commentContent, "comment");

            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment";
            using var response = await _httpClient.PostAsync(url, content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to upload attachment '{FileName}' to page {PageId}. Status code: {StatusCode}, Error: {Error}", fileName, pageId, response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("Uploaded attachment '{FileName}' to page {PageId}", fileName, pageId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading attachment '{FileName}' to page {PageId}", fileName, pageId);
            return false;
        }
    }

    public async Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId)
    {
        try
        {
            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment/{attachmentId}";
            using var response = await _httpClient.DeleteAsync(url);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to delete attachment {AttachmentId} from page {PageId}. Status code: {StatusCode}, Error: {Error}", attachmentId, pageId, response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("Deleted attachment {AttachmentId} from page {PageId}", attachmentId, pageId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting attachment {AttachmentId} from page {PageId}", attachmentId, pageId);
            return false;
        }
    }

    public async Task<byte[]> DownloadAttachmentAsync(string downloadUrl)
    {
        var fullUrl = downloadUrl.StartsWith("http") ? downloadUrl : $"{_baseUrl}{downloadUrl}";
        using var response = await _httpClient.GetAsync(fullUrl);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10)
    {
        try
        {
            var url = $"{_baseUrl}/rest/experimental/content/{pageId}/version?limit={limit}";
            _logger.LogDebug("Fetching version history for page {PageId}: {Url}", pageId, url);

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch version history for page {PageId}. Status: {StatusCode}",
                    pageId, response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageVersionSummary>>(content);
            return result?.Results ?? [];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching version history for page {PageId}", pageId);
            return [];
        }
    }

    public async Task<PageData?> GetPageAtVersionAsync(string pageId, int versionNumber)
    {
        try
        {
            var url = $"{_baseUrl}/rest/api/content/{pageId}?status=historical&version={versionNumber}&expand=ancestors,version";
            _logger.LogDebug("Fetching page {PageId} at version {Version}: {Url}", pageId, versionNumber, url);

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch page {PageId} at version {Version}. Status: {StatusCode}",
                    pageId, versionNumber, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonConvert.DeserializeObject<PageData>(content);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching page {PageId} at version {Version}", pageId, versionNumber);
            return null;
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
