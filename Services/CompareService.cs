using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public class CompareService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<CompareService> _logger;

    public CompareService(IConfluenceApiClient apiClient, ILogger<CompareService> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<CompareReport> CompareAsync(
        string spaceKey,
        string? pageId,
        string? pageTitle,
        string outputDir,
        bool recursive,
        bool matchByTitleWhenNoId = false)
    {
        var report = new CompareReport();
        var resolvedRootPageId = await LocalStorageHelper.ResolvePageIdAsync(_apiClient, spaceKey, pageId, pageTitle);
        if (resolvedRootPageId == null)
            throw new InvalidOperationException("Could not resolve root page for comparison.");

        var rootPage = await _apiClient.GetPageByIdAsync(resolvedRootPageId);
        var rootRelativePath = LocalStorageHelper.NormalizeRelativePath(LocalStorageHelper.SanitizeFileName(rootPage.Title));

        var remotePages = new Dictionary<string, RemotePageSnapshot>(StringComparer.OrdinalIgnoreCase);
        await CollectRemotePagesAsync(rootPage, rootRelativePath, recursive, remotePages);

        var localSnapshot = BuildLocalPagesForComparison(
            outputDir,
            resolvedRootPageId,
            rootRelativePath,
            recursive,
            matchByTitleWhenNoId,
            report);

        foreach (var remote in remotePages.Values.OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            LocalPageSnapshot? localById = null;
            LocalPathSnapshot? localByTitlePath = null;

            if (localSnapshot.PagesById.TryGetValue(remote.PageId, out var matchedById))
            {
                localById = matchedById;
            }
            else if (matchByTitleWhenNoId
                     && localSnapshot.PagesByPath.TryGetValue(remote.RelativePath, out var matchedByPath)
                     && string.IsNullOrEmpty(matchedByPath.PageIdFromMarker))
            {
                localByTitlePath = matchedByPath;
            }

            if (localById == null && localByTitlePath == null)
            {
                report.AddedInConfluence.Add(new ComparePageInfo
                {
                    PageId = remote.PageId,
                    Title = remote.Title,
                    Path = remote.RelativePath
                });
                continue;
            }

            if (localById != null
                && !string.Equals(localById.RelativePath, remote.RelativePath, StringComparison.OrdinalIgnoreCase))
            {
                report.RenamedOrMovedInConfluence.Add(new CompareRenamedOrMovedPageInfo
                {
                    PageId = remote.PageId,
                    Title = remote.Title,
                    LocalPath = localById.RelativePath,
                    ConfluencePath = remote.RelativePath
                });
            }

            var localContent = localById != null
                ? await LocalStorageHelper.ReadLocalPageContentOrNull(localById.DirectoryPath)
                : await LocalStorageHelper.ReadLocalPageContentOrNull(localByTitlePath!.DirectoryPath);

            if (!string.Equals(localContent, remote.Content, StringComparison.Ordinal))
            {
                report.ContentChanged.Add(new CompareContentChangedPageInfo
                {
                    PageId = remote.PageId,
                    Title = remote.Title,
                    Path = remote.RelativePath
                });
            }
        }

        foreach (var local in localSnapshot.PagesById.Values.OrderBy(p => p.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            if (remotePages.ContainsKey(local.PageId))
                continue;

            report.DeletedInConfluence.Add(new ComparePageInfo
            {
                PageId = local.PageId,
                Title = local.Title,
                Path = local.RelativePath
            });
        }

        return report;
    }

    private async Task CollectRemotePagesAsync(
        PageData page,
        string relativePath,
        bool recursive,
        Dictionary<string, RemotePageSnapshot> remotePages)
    {
        remotePages[page.Id] = new RemotePageSnapshot(
            page.Id,
            page.Title,
            relativePath,
            page.Body.Storage.Value);

        if (!recursive)
            return;

        var children = await _apiClient.GetChildrenPagesAsync(page.Id);
        foreach (var child in children)
        {
            var childRelativePath = LocalStorageHelper.NormalizeRelativePath(
                Path.Combine(relativePath, LocalStorageHelper.SanitizeFileName(child.Title)));
            await CollectRemotePagesAsync(child, childRelativePath, recursive, remotePages);
        }
    }

    private LocalComparisonSnapshot BuildLocalPagesForComparison(
        string outputDir,
        string rootPageId,
        string rootRelativePath,
        bool recursive,
        bool matchByTitleWhenNoId,
        CompareReport report)
    {
        var pagesById = new Dictionary<string, LocalPageSnapshot>(StringComparer.OrdinalIgnoreCase);
        var pagesByPath = new Dictionary<string, LocalPathSnapshot>(StringComparer.OrdinalIgnoreCase);

        var allLocalPages = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
        string? localRootDir = null;

        if (allLocalPages.TryGetValue(rootPageId, out var rootById) && Directory.Exists(rootById))
        {
            localRootDir = Path.GetFullPath(rootById);
        }
        else if (matchByTitleWhenNoId)
        {
            var candidateByTitle = Path.GetFullPath(Path.Combine(outputDir, rootRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(candidateByTitle))
            {
                localRootDir = candidateByTitle;
                report.Notes.Add("Local root page was matched by title/folder name because .id marker was not found.");
            }
        }

        if (string.IsNullOrEmpty(localRootDir))
        {
            report.Notes.Add(matchByTitleWhenNoId
                ? "Local snapshot root was not found by .id marker or by title/folder name. Treating local tree as empty."
                : "Local snapshot root was not found by .id marker. Treating local tree as empty; all remote pages are reported as added.");
            return new LocalComparisonSnapshot(pagesById, pagesByPath);
        }

        foreach (var markerFile in Directory.EnumerateFiles(localRootDir, ".id*", SearchOption.AllDirectories))
        {
            var markerName = Path.GetFileName(markerFile);
            if (!markerName.StartsWith(".id", StringComparison.OrdinalIgnoreCase) || markerName.Length <= 3)
                continue;

            var pageId = markerName[3..];
            var pageDir = Path.GetDirectoryName(markerFile);
            if (string.IsNullOrEmpty(pageDir))
                continue;

            var normalizedDir = Path.GetFullPath(pageDir);
            var relativePath = LocalStorageHelper.NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = Path.GetFileName(normalizedDir);

            if (pagesById.ContainsKey(pageId))
            {
                report.Notes.Add($"Duplicate local .id marker found for page ID {pageId}. Only the first one is used.");
                continue;
            }

            pagesById[pageId] = new LocalPageSnapshot(pageId, title, normalizedDir, relativePath);
        }

        foreach (var localDir in LocalStorageHelper.EnumeratePageDirectories(localRootDir))
        {
            var normalizedDir = Path.GetFullPath(localDir);
            var relativePath = LocalStorageHelper.NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = Path.GetFileName(normalizedDir);
            var localPageId = LocalStorageHelper.ReadPageIdFromMarker(normalizedDir);

            if (pagesByPath.ContainsKey(relativePath))
            {
                report.Notes.Add($"Duplicate local path detected for comparison: {relativePath}. Only the first one is used.");
                continue;
            }

            pagesByPath[relativePath] = new LocalPathSnapshot(relativePath, title, normalizedDir, localPageId);
        }

        if (!recursive)
        {
            pagesById = pagesById.TryGetValue(rootPageId, out var onlyRootById)
                ? new Dictionary<string, LocalPageSnapshot>(StringComparer.OrdinalIgnoreCase) { [rootPageId] = onlyRootById }
                : new Dictionary<string, LocalPageSnapshot>(StringComparer.OrdinalIgnoreCase);

            pagesByPath = pagesByPath.TryGetValue(rootRelativePath, out var onlyRootByPath)
                ? new Dictionary<string, LocalPathSnapshot>(StringComparer.OrdinalIgnoreCase) { [rootRelativePath] = onlyRootByPath }
                : new Dictionary<string, LocalPathSnapshot>(StringComparer.OrdinalIgnoreCase);
        }

        return new LocalComparisonSnapshot(pagesById, pagesByPath);
    }
}
