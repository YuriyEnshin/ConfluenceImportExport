using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// <see cref="IConfluenceApiClient"/> implementation for Confluence Cloud.
/// Hybrid by necessity: pages, spaces, attachments listing and versions use
/// the v2 API (<c>/wiki/api/v2</c>) — Atlassian removed the v1 content
/// endpoints from Cloud in 2025 — while CQL search, the auth ping and (later)
/// attachment uploads use the v1 endpoints Atlassian kept alive on Cloud.
/// <para>
/// Deployment-specific shapes stay inside this class: v2 payloads are mapped
/// onto the shared domain models, numeric space ids are resolved to space
/// keys (cached per client lifetime), and the v2 <c>parentId</c> field is
/// exposed through <see cref="PageData.Ancestors"/> exactly like the v1
/// client does. <see cref="PageData.ChildTypes"/> is always <c>null</c> — v2
/// has no equivalent; consumers treat <c>null</c> as "children/attachments
/// may exist".
/// </para>
/// <para>
/// Read-only phase: write operations (page create/update, attachment
/// upload/update/delete) throw <see cref="NotSupportedException"/> until the
/// Cloud write phase ships.
/// </para>
/// </summary>
public class ConfluenceCloudApiClient : IConfluenceApiClient
{
    /// <summary>
    /// Ids per bulk <c>GET /pages?id=…</c> request. The API accepts up to 250,
    /// but ~100 keeps the URL comfortably short for proxies and log lines.
    /// </summary>
    private const int BulkPageChunkSize = 100;

    private const string WriteNotSupportedMessage =
        "This operation is not supported on Confluence Cloud yet: current Cloud support is read-only " +
        "(download, compare, page content). Write support (upload/create/merge, attachments) arrives in an upcoming release.";

    private readonly HttpClient _httpClient;
    private readonly ILogger<ConfluenceCloudApiClient> _logger;

    private readonly string _siteRoot; // https://site.atlassian.net
    private readonly string _wikiRoot; // https://site.atlassian.net/wiki
    private readonly string _v2;       // {wikiRoot}/api/v2
    private readonly string _v1;       // {wikiRoot}/rest/api

    /// <summary>Numeric space id → space key, resolved once per space per client lifetime.</summary>
    private readonly ConcurrentDictionary<string, string> _spaceKeyById = new();

    /// <summary>
    /// Page id → title cache. v2 responses carry only <c>parentId</c> (no
    /// ancestor titles), so parent titles are resolved through this cache;
    /// every fetched page seeds it, which makes tree walks resolve parents
    /// without extra requests.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _pageTitleById = new();

    public ConfluenceCloudApiClient(string baseUrl, HttpClient httpClient, ILogger<ConfluenceCloudApiClient> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Accept both the site root and the wiki root as the configured base
        // URL — operators paste either form. Everything is derived from the
        // site root so URLs never end up with a doubled "/wiki".
        var trimmed = baseUrl.TrimEnd('/');
        _siteRoot = trimmed.EndsWith("/wiki", StringComparison.OrdinalIgnoreCase)
            ? trimmed[..^"/wiki".Length]
            : trimmed;
        _wikiRoot = _siteRoot + "/wiki";
        _v2 = _wikiRoot + "/api/v2";
        _v1 = _wikiRoot + "/rest/api";
    }

    // ── Pages (v2) ───────────────────────────────────────────────────────

    public async Task<PageData> GetPageByIdAsync(string pageId, CancellationToken ct = default)
    {
        var cloudPage = await FetchPageAsync(pageId, "?body-format=storage", $"Failed to fetch page {pageId}", ct);
        return await MapPageAsync(cloudPage, resolveParentTitle: true, ct);
    }

    public async Task<PageData?> TryGetPageByIdAsync(string pageId, CancellationToken ct = default)
    {
        var url = $"{_v2}/pages/{pageId}?body-format=storage";
        using var response = await _httpClient.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, $"Failed to fetch page {pageId}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        var cloudPage = JsonConvert.DeserializeObject<CloudPage>(content)
            ?? throw new InvalidOperationException($"Could not deserialize page with ID {pageId}");
        return await MapPageAsync(cloudPage, resolveParentTitle: true, ct);
    }

    public async Task<List<PageData>> GetChildrenPagesAsync(string parentId, CancellationToken ct = default)
    {
        // Two-step on Cloud: the v2 children endpoint returns stubs without
        // bodies, so stub ids are bulk-resolved via GET /pages?id=… with
        // storage bodies. Child order follows the stub listing.
        var stubs = await GetAllPagesAsync<CloudPage>(
            $"{_v2}/pages/{parentId}/children", "?limit=250",
            $"Failed to fetch children of page {parentId}", ct);
        if (stubs.Count == 0)
            return [];

        var fullById = new Dictionary<string, CloudPage>(StringComparer.Ordinal);
        foreach (var chunk in stubs.Chunk(BulkPageChunkSize))
        {
            var ids = string.Join(",", chunk.Select(s => s.Id));
            var fetched = await GetAllPagesAsync<CloudPage>(
                $"{_v2}/pages", $"?id={ids}&body-format=storage&limit=250",
                $"Failed to fetch children bodies of page {parentId}", ct);
            foreach (var page in fetched)
                fullById[page.Id] = page;
        }

        var pages = new List<PageData>(stubs.Count);
        foreach (var stub in stubs)
        {
            if (!fullById.TryGetValue(stub.Id, out var full))
            {
                // Listed but not bulk-fetchable (e.g. deleted mid-walk) —
                // skip rather than fail the whole listing.
                _logger.LogWarning("Child page {PageId} ('{Title}') was listed but not returned by the bulk fetch; skipping.",
                    stub.Id, stub.Title);
                continue;
            }
            // Parent titles for children come from the cache only: they are
            // not consumed downstream (only ParentId is), and the parent is
            // almost always already cached by the walk that got us here.
            pages.Add(await MapPageAsync(full, resolveParentTitle: false, ct));
        }
        return pages;
    }

    public async Task<List<PageVersionSummary>> GetPageVersionsAsync(string pageId, int limit = 10, CancellationToken ct = default)
    {
        try
        {
            // -modified-date = newest first, matching the v1 (experimental)
            // endpoint's ordering that ChangeSourceAnalyzer walks.
            var url = $"{_v2}/pages/{pageId}/versions?limit={Math.Clamp(limit, 1, 250)}&sort=-modified-date";
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
            var result = JsonConvert.DeserializeObject<CloudResponse<CloudVersion>>(content);
            return result?.Results
                .Select(v => new PageVersionSummary
                {
                    Number = v.Number,
                    When = v.CreatedAt,
                    Message = v.Message,
                    MinorEdit = v.MinorEdit,
                })
                .ToList() ?? [];
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
            var cloudPage = await FetchPageAsync(pageId, $"?version={versionNumber}",
                $"Failed to fetch page {pageId} at version {versionNumber}", ct);
            return await MapPageAsync(cloudPage, resolveParentTitle: true, ct);
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
        // ⚠ Pending live verification: the v2 spec documents `?version=N` as
        // returning the previous version's details, but old-version storage
        // bodies have not been confirmed against a real Cloud site yet.
        // Fallback if it under-delivers: GET /pages/{id}/versions with
        // body-format=storage embeds per-version bodies.
        var cloudPage = await FetchPageAsync(pageId, $"?version={versionNumber}&body-format=storage",
            $"Failed to fetch page {pageId} at version {versionNumber}", ct);
        return await MapPageAsync(cloudPage, resolveParentTitle: true, ct);
    }

    // ── Search & ping (v1 endpoints kept on Cloud) ───────────────────────

    /// <summary>
    /// Returns the found page's id, or <c>null</c> strictly when no page with
    /// this title exists; any API failure throws (callers treat <c>null</c>
    /// as "safe to create"). CQL content search is one of the v1 endpoints
    /// Atlassian kept on Cloud — there is no v2 CQL, and the v2 alternative
    /// (<c>GET /pages?title=…&amp;space-id=…</c>) cannot filter by parent.
    /// </summary>
    public async Task<string?> FindPageByTitleAsync(string spaceKey, string? parentId, string title, CancellationToken ct = default)
    {
        var cqlQuery = $"space=\"{ConfluenceApiHelpers.EscapeCql(spaceKey)}\" AND title=\"{ConfluenceApiHelpers.EscapeCql(title)}\"";
        if (!string.IsNullOrEmpty(parentId))
        {
            cqlQuery += $" AND parent={parentId}";
        }

        var url = $"{_v1}/content/search?cql={Uri.EscapeDataString(cqlQuery)}&limit=10";
        _logger.LogDebug("Searching for page with CQL query: {CqlQuery}", cqlQuery);

        using var response = await _httpClient.GetAsync(url, ct);
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, $"Failed to search for page '{title}' in space '{spaceKey}'", ct);

        var content = await response.Content.ReadAsStringAsync(ct);
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

    public async Task<ConfluenceUser> GetCurrentUserAsync(CancellationToken ct = default)
    {
        // /user/current is one of the v1 endpoints kept on Cloud; v2 has no
        // current-user equivalent, so this stays the canonical auth ping.
        var url = $"{_v1}/user/current";
        using var response = await _httpClient.GetAsync(url, ct);
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, "Failed to fetch the current Confluence user", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<ConfluenceUser>(content)
            ?? throw new InvalidOperationException("Confluence returned an empty user payload.");
    }

    // ── Attachments (v2 reads) ───────────────────────────────────────────

    public async Task<List<AttachmentData>> GetAttachmentsAsync(string pageId, CancellationToken ct = default)
    {
        try
        {
            var cloudAttachments = await GetAllPagesAsync<CloudAttachment>(
                $"{_v2}/pages/{pageId}/attachments", "?limit=250",
                $"Failed to fetch attachments for page {pageId}", ct);
            return cloudAttachments.Select(MapAttachment).ToList();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Best-effort listing, matching the v1 client's contract.
            _logger.LogWarning(ex, "Failed to fetch attachments for page {PageId}", pageId);
            return [];
        }
    }

    public async Task<byte[]> DownloadAttachmentAsync(string downloadUrl, CancellationToken ct = default)
    {
        // v2 downloadLink is relative to the /wiki context ("/download/…");
        // links already carrying "/wiki/…" or a full URL are honoured as-is.
        var fullUrl = downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? downloadUrl
            : downloadUrl.StartsWith("/wiki/", StringComparison.OrdinalIgnoreCase) ? _siteRoot + downloadUrl
            : _wikiRoot + downloadUrl;
        using var response = await _httpClient.GetAsync(fullUrl, ct);
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, $"Failed to download attachment from '{downloadUrl}'", ct);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    // ── Write operations: not supported in the read-only Cloud phase ─────

    public Task<PageUpdateResult> CreatePageAsync(string spaceKey, string? parentId, string title, string content, CancellationToken ct = default) =>
        throw new NotSupportedException(WriteNotSupportedMessage);

    public Task<PageUpdateResult> UpdatePageAsync(string pageId, string title, string content, string? parentId, int? knownVersion = null, CancellationToken ct = default) =>
        throw new NotSupportedException(WriteNotSupportedMessage);

    public Task<bool> UploadAttachmentAsync(string pageId, string filePath, string fileName, CancellationToken ct = default) =>
        throw new NotSupportedException(WriteNotSupportedMessage);

    public Task<bool> UpdateAttachmentDataAsync(string pageId, string attachmentId, string filePath, string fileName, string? contentType = null, CancellationToken ct = default) =>
        throw new NotSupportedException(WriteNotSupportedMessage);

    public Task<bool> DeleteAttachmentAsync(string pageId, string attachmentId, CancellationToken ct = default) =>
        throw new NotSupportedException(WriteNotSupportedMessage);

    // ── v2 plumbing ──────────────────────────────────────────────────────

    private async Task<CloudPage> FetchPageAsync(string pageId, string query, string context, CancellationToken ct)
    {
        var url = $"{_v2}/pages/{pageId}{query}";
        using var response = await _httpClient.GetAsync(url, ct);
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, context, ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        return JsonConvert.DeserializeObject<CloudPage>(content)
            ?? throw new InvalidOperationException($"Could not deserialize page with ID {pageId}");
    }

    /// <summary>
    /// Collects every page of a v2 collection endpoint. v2 pagination is
    /// cursor-based: each response carries <c>_links.next</c>, a relative URL
    /// whose base has varied across Atlassian changes — so only its query
    /// string is trusted and re-applied to the caller's absolute endpoint URL.
    /// </summary>
    private async Task<List<T>> GetAllPagesAsync<T>(string endpointUrl, string firstQuery, string context, CancellationToken ct)
    {
        var results = new List<T>();
        var query = firstQuery;
        while (true)
        {
            using var response = await _httpClient.GetAsync(endpointUrl + query, ct);
            await ConfluenceApiHelpers.EnsureSuccessAsync(response, context, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            var page = JsonConvert.DeserializeObject<CloudResponse<T>>(content)
                ?? throw new InvalidOperationException($"{context}: could not deserialize the collection response.");
            results.AddRange(page.Results);

            var next = page.Links?.Next;
            if (string.IsNullOrEmpty(next))
                break;

            var queryStart = next.IndexOf('?');
            if (queryStart < 0)
            {
                _logger.LogWarning("Unexpected cursor link '{Next}' (no query part); stopping pagination for {Context}.", next, context);
                break;
            }
            query = next[queryStart..];
        }
        return results;
    }

    // ── Mapping onto the shared domain models ────────────────────────────

    private async Task<PageData> MapPageAsync(CloudPage page, bool resolveParentTitle, CancellationToken ct)
    {
        _pageTitleById[page.Id] = page.Title;

        string? spaceKey = null;
        if (!string.IsNullOrEmpty(page.SpaceId))
            spaceKey = await GetSpaceKeyByIdAsync(page.SpaceId, ct);

        var ancestors = new List<PageAncestor>();
        if (!string.IsNullOrEmpty(page.ParentId))
        {
            var parentTitle = resolveParentTitle
                ? await ResolvePageTitleAsync(page.ParentId, ct)
                : (_pageTitleById.TryGetValue(page.ParentId, out var cached) ? cached : "");
            ancestors.Add(new PageAncestor { Id = page.ParentId, Title = parentTitle });
        }

        return new PageData
        {
            Id = page.Id,
            Title = page.Title,
            Body = new Body
            {
                Storage = new StorageContent
                {
                    Value = page.Body?.Storage?.Value ?? "",
                    Representation = page.Body?.Storage?.Representation ?? "",
                },
            },
            Version = MapVersion(page.Version),
            Ancestors = ancestors,
            // v2 has no childTypes; null = "unknown", consumers assume
            // children/attachments may exist and check for real.
            ChildTypes = null,
            Space = spaceKey is null ? null : new SpaceInfo { Key = spaceKey },
            Links = new Links { WebUi = page.Links?.WebUi ?? "" },
        };
    }

    private static AttachmentData MapAttachment(CloudAttachment attachment) => new()
    {
        Id = attachment.Id,
        Title = attachment.Title,
        MediaType = attachment.MediaType ?? "",
        // v2 returns fileSize/mediaType/comment flat; expose them through
        // Extensions so size checks and EffectiveMediaType work unchanged.
        Extensions = new AttachmentExtensions
        {
            FileSize = attachment.FileSize,
            MediaType = attachment.MediaType,
            Comment = attachment.Comment,
        },
        Version = MapVersion(attachment.Version),
        Links = new AttachmentLinks { DownloadUrl = attachment.DownloadLink ?? "" },
    };

    private static VersionInfo? MapVersion(CloudVersion? version) =>
        version is null
            ? null
            : new VersionInfo { Number = version.Number, When = version.CreatedAt, MinorEdit = version.MinorEdit };

    /// <summary>
    /// Resolves a numeric space id to its key, cached per client lifetime.
    /// Failing to resolve throws: the space key feeds the cross-space guards
    /// and the <c>.id</c> markers, so a silently missing key would corrupt
    /// sync state rather than merely degrade display.
    /// </summary>
    private async Task<string?> GetSpaceKeyByIdAsync(string spaceId, CancellationToken ct)
    {
        if (_spaceKeyById.TryGetValue(spaceId, out var cached))
            return cached;

        var url = $"{_v2}/spaces/{spaceId}";
        using var response = await _httpClient.GetAsync(url, ct);
        await ConfluenceApiHelpers.EnsureSuccessAsync(response, $"Failed to resolve space {spaceId}", ct);
        var content = await response.Content.ReadAsStringAsync(ct);
        var space = JsonConvert.DeserializeObject<CloudSpace>(content)
            ?? throw new InvalidOperationException($"Could not deserialize space {spaceId}");

        _spaceKeyById[space.Id] = space.Key;
        return space.Key;
    }

    /// <summary>
    /// Resolves a page title by id via the cache, fetching the bare page on a
    /// miss. Best-effort: an inaccessible parent (deleted, no permission)
    /// yields <c>""</c> rather than failing the fetch of the page itself —
    /// only ancestor display names degrade.
    /// </summary>
    private async Task<string> ResolvePageTitleAsync(string pageId, CancellationToken ct)
    {
        if (_pageTitleById.TryGetValue(pageId, out var cached))
            return cached;

        try
        {
            var url = $"{_v2}/pages/{pageId}";
            using var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Could not resolve title of page {PageId}: HTTP {Status}", pageId, (int)response.StatusCode);
                return "";
            }
            var content = await response.Content.ReadAsStringAsync(ct);
            var page = JsonConvert.DeserializeObject<CloudPage>(content);
            if (page is null)
                return "";
            _pageTitleById[page.Id] = page.Title;
            return page.Title;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not resolve title of page {PageId}", pageId);
            return "";
        }
    }
}
