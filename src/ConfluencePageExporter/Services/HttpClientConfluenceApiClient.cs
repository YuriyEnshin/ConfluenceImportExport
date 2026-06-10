using System.Net;
using System.Text;
using System.Web;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// HttpClient-based implementation of <see cref="IConfluenceApiClient"/> over the
/// Confluence REST API (v1, Server/Data Center compatible).
/// Error contract: a non-success HTTP status surfaces as a typed
/// <see cref="ConfluenceApiException"/> (409 → <see cref="ConfluenceConflictException"/>);
/// lookup methods return <c>null</c> strictly for "not found". Best-effort methods
/// (attachments listing, version history) degrade gracefully on server errors but
/// always propagate cancellation.
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

    public async Task<PageData> GetPageByIdAsync(string pageId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/api/content/{pageId}?expand=body.storage,ancestors,version,childTypes.all,space";
        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, $"Failed to fetch page {pageId}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<PageData>(content)
            ?? throw new InvalidOperationException($"Could not deserialize page with ID {pageId}");
    }

    public async Task<PageData?> TryGetPageByIdAsync(string pageId, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/rest/api/content/{pageId}?expand=body.storage,ancestors,version,childTypes.all,space";
        using var response = await _httpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await EnsureSuccessAsync(response, $"Failed to fetch page {pageId}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<PageData>(content)
            ?? throw new InvalidOperationException($"Could not deserialize page with ID {pageId}");
    }

    public async Task<List<PageData>> GetChildrenPagesAsync(string parentId, CancellationToken ct = default)
    {
        var pages = new List<PageData>();
        var start = 0;
        const int limit = 100;

        while (true)
        {
            var url = $"{_baseUrl}/rest/api/content/{parentId}/child/page?limit={limit}&start={start}&expand=body.storage,version,childTypes.all";
            using var response = await _httpClient.GetAsync(url, ct);
            await EnsureSuccessAsync(response, $"Failed to fetch children of page {parentId}", ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageData>>(content)
                ?? throw new InvalidOperationException("Could not deserialize children list");

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

    public async Task<List<AttachmentData>> GetAttachmentsAsync(string pageId, CancellationToken ct = default)
    {
        var attachments = new List<AttachmentData>();
        var start = 0;
        const int limit = 100;

        while (true)
        {
            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment?limit={limit}&start={start}&expand=extensions,version";
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch attachments for page {PageId}. Status code: {StatusCode}", pageId, response.StatusCode);
                break;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<AttachmentData>>(content)
                ?? throw new InvalidOperationException("Could not deserialize attachments list");

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

    /// <summary>
    /// Returns the found page's id, or <c>null</c> strictly when no page with
    /// this title exists. Any API failure (auth, rate limit, 5xx after the
    /// retry budget) throws instead of returning <c>null</c>: callers treat
    /// <c>null</c> as "safe to create", so masking an error here used to risk
    /// creating a duplicate page mid-walk.
    /// </summary>
    public async Task<string?> FindPageByTitleAsync(string spaceKey, string? parentId, string title, CancellationToken ct = default)
    {
        var cqlQuery = $"space=\"{EscapeCql(spaceKey)}\" AND title=\"{EscapeCql(title)}\"";
        if (!string.IsNullOrEmpty(parentId))
        {
            cqlQuery += $" AND parent={parentId}";
        }

        var url = $"{_baseUrl}/rest/api/content/search?cql={Uri.EscapeDataString(cqlQuery)}&limit=10";
        _logger.LogDebug("Searching for page with CQL query: {CqlQuery}", cqlQuery);
        _logger.LogDebug("URL: {Url}", url);

        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, $"Failed to search for page '{title}' in space '{spaceKey}'", ct);

        var content = await response.Content.ReadAsStringAsync(ct);
        _logger.LogDebug("Search response content: {Content}", content);

        var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageData>>(content)
            ?? throw new InvalidOperationException($"Could not deserialize search results for title '{title}'");

        if (result.Results.Count > 0)
        {
            var foundPage = result.Results[0];
            _logger.LogDebug("Found page with title '{FoundTitle}' and ID {FoundId}", foundPage.Title, foundPage.Id);
            return foundPage.Id;
        }

        _logger.LogDebug("No page found with title '{Title}'", title);
        return null;
    }

    public async Task<PageUpdateResult> CreatePageAsync(string spaceKey, string? parentId, string title, string content, CancellationToken ct = default)
    {
        object pageData;

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
        using var response = await _httpClient.PostAsync(url, stringContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to create page '{Title}'. Status code: {StatusCode}, Error: {Error}", title, response.StatusCode, errorContent);
            throw ApiException(response.StatusCode, errorContent, $"Failed to create page '{title}'");
        }

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var result = JsonConvert.DeserializeObject<PageResponse>(responseContent)
            ?? throw new InvalidOperationException("Could not deserialize page creation response");
        return new PageUpdateResult(result.Id, result.Version?.Number ?? 1);
    }

    public async Task<PageUpdateResult> UpdatePageAsync(string pageId, string title, string content, string? parentId, int? knownVersion = null, CancellationToken ct = default)
    {
        // The version the PUT is based on: the caller-observed one when given
        // (honest optimistic concurrency — a concurrent edit yields 409), else
        // fetched here as a fallback for callers that haven't read the page.
        int version;
        if (knownVersion is int known)
        {
            version = known + 1;
        }
        else
        {
            var getPageUrl = $"{_baseUrl}/rest/api/content/{pageId}?expand=version";
            using var getResponse = await _httpClient.GetAsync(getPageUrl, ct);
            await EnsureSuccessAsync(getResponse, $"Failed to fetch current version of page {pageId}", ct);
            var getPageContent = await getResponse.Content.ReadAsStringAsync(ct);
            var currentPage = JsonConvert.DeserializeObject<PageResponse>(getPageContent)
                ?? throw new InvalidOperationException("Could not deserialize current page");
            version = (currentPage.Version?.Number ?? 1) + 1;
        }

        object pageData;

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
        using var response = await _httpClient.PutAsync(url, stringContent, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Failed to update page '{Title}' (ID: {PageId}). Status code: {StatusCode}, Error: {Error}", title, pageId, response.StatusCode, errorContent);
            throw ApiException(response.StatusCode, errorContent, $"Failed to update page '{title}' (ID: {pageId})");
        }

        var responseContent = await response.Content.ReadAsStringAsync(ct);
        var result = JsonConvert.DeserializeObject<PageResponse>(responseContent)
            ?? throw new InvalidOperationException("Could not deserialize page update response");
        return new PageUpdateResult(result.Id, result.Version?.Number ?? version);
    }

    /// <summary>
    /// Builds a typed exception from a failed write response: 409 becomes a
    /// <see cref="ConfluenceConflictException"/> (recoverable per-page), every
    /// other status a <see cref="ConfluenceApiException"/> carrying the code
    /// and a trimmed response body for diagnostics.
    /// </summary>
    private static ConfluenceApiException ApiException(HttpStatusCode status, string responseBody, string context)
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

    /// <summary>
    /// Read-path counterpart of <see cref="ApiException"/>: replaces
    /// <c>EnsureSuccessStatusCode</c> so read failures carry the status code and
    /// a response-body snippet with the same fidelity as write failures, and so
    /// callers can react to <see cref="ConfluenceApiException.IsAuthFailure"/>
    /// uniformly (e.g. multi-tree upload aborts the batch on 401/403).
    /// </summary>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string context, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var errorContent = await response.Content.ReadAsStringAsync(ct);
        throw ApiException(response.StatusCode, errorContent, context);
    }

    /// <summary>
    /// Escapes a value for safe interpolation into a double-quoted CQL string
    /// literal. CQL uses backslash escaping inside quotes, so a space key or
    /// title containing <c>"</c> or <c>\</c> (both legal in page titles) would
    /// otherwise break the query or silently change its meaning.
    /// </summary>
    private static string EscapeCql(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    public async Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName, CancellationToken ct = default)
    {
        try
        {
            var fileContent = await File.ReadAllBytesAsync(filePath, ct);
            using var content = new MultipartFormDataContent();

            var fileContentPart = new ByteArrayContent(fileContent);
            content.Add(fileContentPart, "file", fileName);

            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment";
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Add("X-Atlassian-Token", "nocheck");

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to upload attachment '{FileName}' to page {PageId}. Status code: {StatusCode}, Error: {Error}", fileName, pageId, response.StatusCode, errorContent);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is not a per-attachment failure — propagate it
            // instead of reporting a tolerated "false" below.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading attachment '{FileName}' to page {PageId}", fileName, pageId);
            return false;
        }
    }

    public async Task<bool> UpdateAttachmentDataAsync(string pageId, string attachmentId, string filePath, string fileName, CancellationToken ct = default)
    {
        try
        {
            var fileContent = await File.ReadAllBytesAsync(filePath, ct);
            using var content = new MultipartFormDataContent();

            var fileContentPart = new ByteArrayContent(fileContent);
            content.Add(fileContentPart, "file", fileName);

            content.Add(new StringContent("true"), "minorEdit");

            var url = $"{_baseUrl}/rest/api/content/{pageId}/child/attachment/{attachmentId}/data";
            using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
            request.Headers.Add("X-Atlassian-Token", "nocheck");

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Failed to update attachment '{FileName}' (ID: {AttachmentId}) on page {PageId}. Status code: {StatusCode}, Error: {Error}",
                    fileName, attachmentId, pageId, response.StatusCode, errorContent);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating attachment '{FileName}' (ID: {AttachmentId}) on page {PageId}", fileName, attachmentId, pageId);
            return false;
        }
    }

    public async Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/rest/api/content/{attachmentId}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("X-Atlassian-Token", "nocheck");

            using var response = await _httpClient.SendAsync(request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning("Failed to delete attachment {AttachmentId} from page {PageId}. Status code: {StatusCode}, Error: {Error}", attachmentId, pageId, response.StatusCode, errorContent);
                return false;
            }

            _logger.LogInformation("Deleted attachment {AttachmentId} from page {PageId}", attachmentId, pageId);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting attachment {AttachmentId} from page {PageId}", attachmentId, pageId);
            return false;
        }
    }

    public async Task<byte[]> DownloadAttachmentAsync(string downloadUrl, CancellationToken ct = default)
    {
        var fullUrl = downloadUrl.StartsWith("http") ? downloadUrl : $"{_baseUrl}{downloadUrl}";
        using var response = await _httpClient.GetAsync(fullUrl, ct);
        await EnsureSuccessAsync(response, $"Failed to download attachment from '{downloadUrl}'", ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public async Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/rest/experimental/content/{pageId}/version?limit={limit}";
            _logger.LogDebug("Fetching version history for page {PageId}: {Url}", pageId, url);

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch version history for page {PageId}. Status: {StatusCode}",
                    pageId, response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            var result = JsonConvert.DeserializeObject<ConfluenceResponse<PageVersionSummary>>(content);
            return result?.Results ?? [];
        }
        catch (OperationCanceledException)
        {
            // Version history is best-effort, but cancellation is not a
            // tolerable failure — propagate instead of degrading to [].
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching version history for page {PageId}", pageId);
            return [];
        }
    }

    public async Task<PageData?> GetPageAtVersionAsync(string pageId, int versionNumber, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/rest/api/content/{pageId}?status=historical&version={versionNumber}&expand=ancestors,version";
            _logger.LogDebug("Fetching page {PageId} at version {Version}: {Url}", pageId, versionNumber, url);

            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Failed to fetch page {PageId} at version {Version}. Status: {StatusCode}",
                    pageId, versionNumber, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(ct);
            return JsonConvert.DeserializeObject<PageData>(content);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching page {PageId} at version {Version}", pageId, versionNumber);
            return null;
        }
    }

    public async Task<PageData> GetPageContentAtVersionAsync(string pageId, int versionNumber, CancellationToken ct = default)
    {
        // Same endpoint as GetPageAtVersionAsync, but with body.storage in
        // the expand list. Unlike GetPageAtVersionAsync we surface failures
        // (don't swallow exceptions): this method is reached only when the
        // caller explicitly asked for a specific historical version, so any
        // failure (404 = version doesn't exist, 401 = lost auth, etc.) is
        // an actionable user-visible error rather than tolerable noise.
        var url = $"{_baseUrl}/rest/api/content/{pageId}?status=historical&version={versionNumber}&expand=body.storage,version,space";
        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, $"Failed to fetch page {pageId} at version {versionNumber}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<PageData>(content)
            ?? throw new InvalidOperationException($"Could not deserialize page {pageId} at version {versionNumber}.");
    }

    public async Task<ConfluenceUser> GetCurrentUserAsync(CancellationToken ct = default)
    {
        // /rest/api/user/current is the canonical lightweight ping for
        // Confluence: it verifies both connectivity and credentials in a
        // single short request and works identically on on-prem and cloud
        // (cloud requires the /wiki prefix in the base URL, which the
        // operator already configures via Global:BaseUrl).
        var url = $"{_baseUrl}/rest/api/user/current";
        using var response = await _httpClient.GetAsync(url, ct);
        await EnsureSuccessAsync(response, "Failed to fetch the current Confluence user", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<ConfluenceUser>(content)
            ?? throw new InvalidOperationException("Confluence returned an empty user payload.");
    }

}
