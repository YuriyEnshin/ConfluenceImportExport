using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public class Exporter
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<Exporter> _logger;
    private readonly bool _dryRun;

    public Exporter(string baseUrl, string username, string authSecret, ILogger<Exporter>? logger, string authType = "onprem", bool dryRun = false)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dryRun = dryRun;

        var apiClientLogger = new LoggerWrapper(logger);
        _apiClient = new HttpClientConfluenceApiClient(baseUrl, username, authSecret, apiClientLogger, authType);
    }

    public async Task DownloadAsync(string spaceKey, string? pageId, string? pageTitle, string outputDir, bool recursive, string overwriteStrategy = "fail")
    {
        var resolvedPageId = await ResolvePageIdAsync(spaceKey, pageId, pageTitle);
        if (resolvedPageId == null)
        {
            _logger.LogError("Could not resolve page. ID: {PageId}, Title: {PageTitle}", pageId, pageTitle);
            return;
        }

        if (Directory.Exists(outputDir) && Directory.EnumerateFileSystemEntries(outputDir).Any())
        {
            switch (overwriteStrategy.ToLower())
            {
                case "fail":
                    throw new InvalidOperationException(
                        $"Output directory is not empty: {outputDir}. Use --overwrite-strategy to specify how to handle this.");
                case "skip":
                    _logger.LogInformation("Output directory is not empty. Skipping download (strategy: skip).");
                    return;
                case "overwrite":
                    _logger.LogInformation("Output directory is not empty. Overwriting existing files.");
                    break;
            }
        }

        var pageDirectoryIndex = BuildPageDirectoryIndex(outputDir);
        var page = await _apiClient.GetPageByIdAsync(resolvedPageId);
        await DownloadPageAsync(page, outputDir, recursive, overwriteStrategy, pageDirectoryIndex);
    }

    private async Task DownloadPageAsync(
        PageData page,
        string parentDir,
        bool recursive,
        string overwriteStrategy,
        Dictionary<string, string> pageDirectoryIndex)
    {
        var pageDir = ResolvePageDirectoryForDownload(page, parentDir, pageDirectoryIndex);

        if (!_dryRun)
        {
            Directory.CreateDirectory(pageDir);
        }

        await SavePageContent(page, pageDir, overwriteStrategy);
        await SavePageIdMarker(page.Id, pageDir);

        var attachments = await _apiClient.GetAttachmentsAsync(page.Id);
        await SaveAttachments(attachments, pageDir, overwriteStrategy);

        if (recursive)
        {
            var children = await _apiClient.GetChildrenPagesAsync(page.Id);
            foreach (var child in children)
            {
                await DownloadPageAsync(child, pageDir, recursive, overwriteStrategy, pageDirectoryIndex);
            }
        }
    }

    private Dictionary<string, string> BuildPageDirectoryIndex(string rootDir)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDir))
            return index;

        foreach (var markerFile in Directory.EnumerateFiles(rootDir, ".id*", SearchOption.AllDirectories))
        {
            var markerName = Path.GetFileName(markerFile);
            if (!markerName.StartsWith(".id", StringComparison.OrdinalIgnoreCase) || markerName.Length <= 3)
                continue;

            var pageId = markerName[3..];
            var pageDir = Path.GetDirectoryName(markerFile);
            if (string.IsNullOrEmpty(pageDir))
                continue;

            var normalizedPageDir = Path.GetFullPath(pageDir);
            if (!index.TryAdd(pageId, normalizedPageDir))
            {
                _logger.LogWarning(
                    "Found duplicate page marker for ID {PageId}. Keeping first path {KeptPath}, ignoring {IgnoredPath}",
                    pageId,
                    index[pageId],
                    normalizedPageDir);
            }
        }

        return index;
    }

    private string ResolvePageDirectoryForDownload(
        PageData page,
        string parentDir,
        Dictionary<string, string> pageDirectoryIndex)
    {
        var expectedDir = Path.GetFullPath(Path.Combine(parentDir, SanitizeFileName(page.Title)));
        if (!pageDirectoryIndex.TryGetValue(page.Id, out var existingDir))
        {
            pageDirectoryIndex[page.Id] = expectedDir;
            return expectedDir;
        }

        var normalizedExistingDir = Path.GetFullPath(existingDir);
        if (!Directory.Exists(normalizedExistingDir))
        {
            pageDirectoryIndex[page.Id] = expectedDir;
            return expectedDir;
        }

        if (PathsEqual(normalizedExistingDir, expectedDir))
        {
            pageDirectoryIndex[page.Id] = expectedDir;
            return expectedDir;
        }

        var expectedParent = Path.GetDirectoryName(expectedDir);
        if (!string.IsNullOrEmpty(expectedParent) && !_dryRun)
        {
            Directory.CreateDirectory(expectedParent);
        }

        _logger.LogInformation(
            "Page {PageId} location changed on Confluence. Moving local directory: {OldPath} -> {NewPath}",
            page.Id,
            normalizedExistingDir,
            expectedDir);

        if (!_dryRun)
        {
            if (Directory.Exists(expectedDir))
            {
                var markerAtExpected = ReadPageIdFromMarker(expectedDir);
                if (string.Equals(markerAtExpected, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    // Duplicate directory for same page ID; keep expected path and remove stale old path.
                    Directory.Delete(normalizedExistingDir, true);
                    UpdateDirectoryIndexPaths(pageDirectoryIndex, normalizedExistingDir, expectedDir);
                    pageDirectoryIndex[page.Id] = expectedDir;
                    return expectedDir;
                }

                var backupPath = $"{expectedDir}.conflict_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                Directory.Move(expectedDir, backupPath);
                _logger.LogWarning(
                    "Target directory already existed and was moved aside: {ExpectedDir} -> {BackupPath}",
                    expectedDir,
                    backupPath);
                UpdateDirectoryIndexPaths(pageDirectoryIndex, expectedDir, backupPath);
            }

            Directory.Move(normalizedExistingDir, expectedDir);
        }

        UpdateDirectoryIndexPaths(pageDirectoryIndex, normalizedExistingDir, expectedDir);
        pageDirectoryIndex[page.Id] = expectedDir;
        return expectedDir;
    }

    private static void UpdateDirectoryIndexPaths(
        Dictionary<string, string> pageDirectoryIndex,
        string oldRootDir,
        string newRootDir)
    {
        var oldRoot = Path.GetFullPath(oldRootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var newRoot = Path.GetFullPath(newRootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var oldPrefix = oldRoot + Path.DirectorySeparatorChar;
        var oldAltPrefix = oldRoot + Path.AltDirectorySeparatorChar;

        foreach (var key in pageDirectoryIndex.Keys.ToList())
        {
            var currentPath = Path.GetFullPath(pageDirectoryIndex[key])
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (currentPath.Equals(oldRoot, StringComparison.OrdinalIgnoreCase))
            {
                pageDirectoryIndex[key] = newRoot;
                continue;
            }

            if (currentPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
                || currentPath.StartsWith(oldAltPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = currentPath[oldRoot.Length..];
                pageDirectoryIndex[key] = newRoot + suffix;
            }
        }
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
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
        var resolvedRootPageId = await ResolvePageIdAsync(spaceKey, pageId, pageTitle);
        if (resolvedRootPageId == null)
            throw new InvalidOperationException("Could not resolve root page for comparison.");

        var rootPage = await _apiClient.GetPageByIdAsync(resolvedRootPageId);
        var rootRelativePath = NormalizeRelativePath(SanitizeFileName(rootPage.Title));

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
                ? await ReadLocalPageContentOrNull(localById.DirectoryPath)
                : await ReadLocalPageContentOrNull(localByTitlePath!.DirectoryPath);

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
            var childRelativePath = NormalizeRelativePath(Path.Combine(relativePath, SanitizeFileName(child.Title)));
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

        var allLocalPages = BuildPageDirectoryIndex(outputDir);
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
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = Path.GetFileName(normalizedDir);

            if (pagesById.ContainsKey(pageId))
            {
                report.Notes.Add($"Duplicate local .id marker found for page ID {pageId}. Only the first one is used.");
                continue;
            }

            pagesById[pageId] = new LocalPageSnapshot(pageId, title, normalizedDir, relativePath);
        }

        foreach (var localDir in EnumeratePageDirectories(localRootDir))
        {
            var normalizedDir = Path.GetFullPath(localDir);
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = Path.GetFileName(normalizedDir);
            var localPageId = ReadPageIdFromMarker(normalizedDir);

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

    private static async Task<string?> ReadLocalPageContentOrNull(string pageDirectory)
    {
        var indexPath = Path.Combine(pageDirectory, "index.html");
        if (!File.Exists(indexPath))
            return null;

        return await File.ReadAllTextAsync(indexPath);
    }

    private static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    private static IEnumerable<string> EnumeratePageDirectories(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories).Prepend(rootDir))
        {
            if (File.Exists(Path.Combine(dir, "index.html")))
                yield return dir;
        }
    }

    private sealed record RemotePageSnapshot(
        string PageId,
        string Title,
        string RelativePath,
        string Content);

    private sealed record LocalComparisonSnapshot(
        Dictionary<string, LocalPageSnapshot> PagesById,
        Dictionary<string, LocalPathSnapshot> PagesByPath);

    private sealed record LocalPageSnapshot(
        string PageId,
        string Title,
        string DirectoryPath,
        string RelativePath);

    private sealed record LocalPathSnapshot(
        string RelativePath,
        string Title,
        string DirectoryPath,
        string? PageIdFromMarker);

    #region Upload Update

    public async Task UploadUpdateAsync(string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle, bool recursive, string onError = "abort")
    {
        ValidateSourceDirectory(sourceDir);

        // Resolve the root page — explicit params take priority, no parent check for root
        var rootPageId = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle);

        await UpdatePageContentAndAttachments(spaceKey, rootPageId, sourceDir);

        if (recursive)
        {
            foreach (var childDir in GetPageSubdirectories(sourceDir))
            {
                await ProcessChildForUpdate(spaceKey, childDir, rootPageId, onError);
            }
        }
    }

    private async Task<string> ResolveRootPageForUpdate(string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle)
    {
        // Priority 1: explicit --page-id
        if (!string.IsNullOrEmpty(explicitPageId))
        {
            var page = await _apiClient.TryGetPageByIdAsync(explicitPageId);
            if (page == null)
                throw new InvalidOperationException($"Page with ID '{explicitPageId}' not found in Confluence");
            return page.Id;
        }

        // Priority 2: explicit --page-title
        if (!string.IsNullOrEmpty(explicitPageTitle))
        {
            var foundId = await _apiClient.FindPageByTitleAsync(spaceKey, null, explicitPageTitle);
            if (foundId == null)
                throw new InvalidOperationException($"Page with title '{explicitPageTitle}' not found in space '{spaceKey}'");
            return foundId;
        }

        // Priority 3: .id marker file
        var markerPageId = ReadPageIdFromMarker(sourceDir);
        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
                return page.Id;
            _logger.LogWarning("Page with ID '{PageId}' from .id marker not found, falling back to title search", markerPageId);
        }

        // Priority 4: folder name as title
        var folderName = Path.GetFileName(sourceDir);
        var foundByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName);
        if (foundByTitle != null)
            return foundByTitle;

        throw new InvalidOperationException(
            $"Could not find a matching Confluence page for '{folderName}'. " +
            "Specify --page-id or --page-title, or use 'upload create' for new pages.");
    }

    private async Task ProcessChildForUpdate(string spaceKey, string childDir, string parentPageId, string onError)
    {
        var folderName = Path.GetFileName(childDir);
        var markerPageId = ReadPageIdFromMarker(childDir);
        string? resolvedPageId = null;
        bool shouldCreate = false;

        // Step 1: resolve by .id file
        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
            {
                if (page.ParentId == parentPageId)
                {
                    resolvedPageId = page.Id;
                }
                else
                {
                    var msg = $"Page '{page.Title}' (ID: {page.Id}) exists but under a different parent " +
                              $"(expected parent {parentPageId}, actual parent {page.ParentId})";
                    _logger.LogError("{Message}", msg);
                    if (onError == "abort")
                        throw new InvalidOperationException(msg);
                    return;
                }
            }
            // page deleted → fall through to title search
        }

        // Step 2: resolve by title (if not yet resolved)
        if (resolvedPageId == null)
        {
            var foundUnderParent = await _apiClient.FindPageByTitleAsync(spaceKey, parentPageId, folderName);
            if (foundUnderParent != null)
            {
                resolvedPageId = foundUnderParent;
            }
            else
            {
                var foundGlobally = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName);
                if (foundGlobally != null)
                {
                    var msg = $"Page '{folderName}' exists in space '{spaceKey}' but under a different parent";
                    _logger.LogError("{Message}", msg);
                    if (onError == "abort")
                        throw new InvalidOperationException(msg);
                    return;
                }
                shouldCreate = true;
            }
        }

        // Step 3: create or update, then recurse
        if (shouldCreate)
        {
            resolvedPageId = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
            if (resolvedPageId == null) return;

            // Children of a newly created page follow the create path
            foreach (var grandchildDir in GetPageSubdirectories(childDir))
            {
                await ProcessChildForCreate(spaceKey, grandchildDir, resolvedPageId);
            }
        }
        else
        {
            await UpdatePageContentAndAttachments(spaceKey, resolvedPageId!, childDir);

            foreach (var grandchildDir in GetPageSubdirectories(childDir))
            {
                await ProcessChildForUpdate(spaceKey, grandchildDir, resolvedPageId!, onError);
            }
        }
    }

    #endregion

    #region Upload Create

    public async Task UploadCreateAsync(string spaceKey, string sourceDir, string? parentId, string? parentTitle, bool recursive)
    {
        ValidateSourceDirectory(sourceDir);

        // Resolve parent page if specified
        string? resolvedParentId = null;
        if (!string.IsNullOrEmpty(parentId) || !string.IsNullOrEmpty(parentTitle))
        {
            resolvedParentId = await ResolvePageIdAsync(spaceKey, parentId, parentTitle);
            if (resolvedParentId == null)
                throw new InvalidOperationException(
                    $"Parent page not found. ID: '{parentId}', Title: '{parentTitle}'");
        }

        var createdPageId = await CreatePageFromDirectory(spaceKey, sourceDir, resolvedParentId);
        if (createdPageId == null) return;

        if (recursive)
        {
            foreach (var childDir in GetPageSubdirectories(sourceDir))
            {
                await ProcessChildForCreate(spaceKey, childDir, createdPageId);
            }
        }
    }

    private async Task ProcessChildForCreate(string spaceKey, string childDir, string? parentPageId)
    {
        var createdPageId = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
        if (createdPageId == null) return;

        foreach (var grandchildDir in GetPageSubdirectories(childDir))
        {
            await ProcessChildForCreate(spaceKey, grandchildDir, createdPageId);
        }
    }

    #endregion

    #region Upload Helpers

    private async Task<string?> CreatePageFromDirectory(string spaceKey, string pageDir, string? parentId)
    {
        var title = Path.GetFileName(pageDir);
        var content = await ReadPageContent(pageDir);

        // Check title uniqueness within the space
        var existingId = await _apiClient.FindPageByTitleAsync(spaceKey, null, title);
        if (existingId != null)
        {
            _logger.LogError("Cannot create page '{Title}': a page with this title already exists (ID: {ExistingId})", title, existingId);
            return null;
        }

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would create page '{Title}' under parent {ParentId}", title, parentId ?? "(space root)");
            LogDryRunAttachments(pageDir);
            return $"dry-run-{title}";
        }

        var createdId = await _apiClient.CreatePageAsync(spaceKey, parentId, title, content);
        if (createdId == null)
        {
            _logger.LogError("Failed to create page '{Title}'", title);
            return null;
        }

        _logger.LogInformation("Created page '{Title}' with ID {PageId}", title, createdId);
        await UploadPageAttachments(createdId, pageDir);
        return createdId;
    }

    private async Task UpdatePageContentAndAttachments(string spaceKey, string pageId, string pageDir)
    {
        var title = Path.GetFileName(pageDir);
        var content = await ReadPageContent(pageDir);

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would update page {PageId} with title '{Title}'", pageId, title);

            // Check if title rename would cause a conflict
            var existingByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, title);
            if (existingByTitle != null && existingByTitle != pageId)
            {
                _logger.LogWarning("DRY RUN: Renaming page {PageId} to '{Title}' would conflict with existing page {ConflictId}",
                    pageId, title, existingByTitle);
            }

            LogDryRunAttachments(pageDir);
            return;
        }

        var updatedId = await _apiClient.UpdatePageAsync(pageId, title, content, null);
        if (updatedId == null)
        {
            _logger.LogError("Failed to update page {PageId} with title '{Title}'", pageId, title);
            return;
        }

        _logger.LogInformation("Updated page {PageId} with title '{Title}'", pageId, title);
        await UploadPageAttachments(pageId, pageDir);
    }

    private async Task UploadPageAttachments(string pageId, string pageDir)
    {
        var files = GetAttachmentFiles(pageDir).ToList();
        if (files.Count == 0) return;

        var existingAttachments = await _apiClient.GetAttachmentsAsync(pageId);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var existing = existingAttachments.FirstOrDefault(
                a => a.Title.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
                await _apiClient.DeleteAttachmentAsync(pageId, existing.Id);

            await _apiClient.UploadAttachmentAsync(pageId, file, fileName);
            _logger.LogInformation("Uploaded attachment '{FileName}' to page {PageId}", fileName, pageId);
        }
    }

    private void LogDryRunAttachments(string pageDir)
    {
        foreach (var file in GetAttachmentFiles(pageDir))
        {
            _logger.LogInformation("DRY RUN: Would upload attachment '{FileName}'", Path.GetFileName(file));
        }
    }

    private static async Task<string> ReadPageContent(string pageDir)
    {
        var indexPath = Path.Combine(pageDir, "index.html");
        if (!File.Exists(indexPath))
            throw new InvalidOperationException($"No index.html found in '{pageDir}'");
        return await File.ReadAllTextAsync(indexPath);
    }

    private static string? ReadPageIdFromMarker(string pageDir)
    {
        if (!Directory.Exists(pageDir)) return null;

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".id") && fileName.Length > 3)
                return fileName[3..];
        }
        return null;
    }

    private static IEnumerable<string> GetAttachmentFiles(string pageDir)
    {
        return Directory.GetFiles(pageDir)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(".id");
            });
    }

    private static IEnumerable<string> GetPageSubdirectories(string pageDir)
    {
        return Directory.Exists(pageDir) ? Directory.GetDirectories(pageDir) : [];
    }

    private static void ValidateSourceDirectory(string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");

        if (!File.Exists(Path.Combine(sourceDir, "index.html")))
            throw new FileNotFoundException($"No index.html found in source directory: {sourceDir}");
    }

    #endregion

    private async Task<string?> ResolvePageIdAsync(string spaceKey, string? pageId, string? pageTitle)
    {
        if (!string.IsNullOrEmpty(pageId))
            return pageId;

        if (!string.IsNullOrEmpty(pageTitle))
            return await _apiClient.FindPageByTitleAsync(spaceKey, null, pageTitle);

        return null;
    }

    private async Task SavePageContent(PageData page, string pageDir, string overwriteStrategy)
    {
        var filePath = Path.Combine(pageDir, "index.html");
        var content = page.Body.Storage.Value;

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would save page '{Title}' -> {File}", page.Title, filePath);
            return;
        }

        if (File.Exists(filePath) && overwriteStrategy == "skip")
        {
            _logger.LogInformation("Skipping existing file: {File}", filePath);
            return;
        }

        if (File.Exists(filePath))
        {
            var existingContent = await File.ReadAllTextAsync(filePath);
            if (string.Equals(existingContent, content, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Page '{Title}' content is unchanged. Keeping existing file without rewrite: {File}",
                    page.Title,
                    filePath);
                return;
            }
        }

        await File.WriteAllTextAsync(filePath, content);
        _logger.LogInformation("Saved page '{Title}' -> {File}", page.Title, filePath);
    }

    private async Task SavePageIdMarker(string pageId, string pageDir)
    {
        var filePath = Path.Combine(pageDir, $".id{pageId}");

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would create ID marker: {File}", filePath);
            return;
        }

        if (!File.Exists(filePath))
        {
            await File.WriteAllTextAsync(filePath, string.Empty);
        }
    }

    private async Task SaveAttachments(List<AttachmentData> attachments, string pageDir, string overwriteStrategy)
    {
        foreach (var att in attachments)
        {
            var filePath = Path.Combine(pageDir, SanitizeFileName(att.Title));

            try
            {
                if (_dryRun)
                {
                    _logger.LogInformation("DRY RUN: Would download attachment '{Title}' -> {Path}", att.Title, filePath);
                    continue;
                }

                if (File.Exists(filePath) && overwriteStrategy == "skip")
                {
                    _logger.LogInformation("Skipping existing attachment: {Path}", filePath);
                    continue;
                }

                var fileContent = await _apiClient.DownloadAttachmentAsync(att.Links.DownloadUrl);
                await File.WriteAllBytesAsync(filePath, fileContent);
                _logger.LogInformation("Downloaded attachment '{Title}' -> {Path}", att.Title, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download attachment: {Title}", att.Title);
            }
        }
    }

    private static string SanitizeFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", title.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }
}
