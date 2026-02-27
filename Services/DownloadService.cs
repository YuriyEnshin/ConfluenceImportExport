using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public class DownloadService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<DownloadService> _logger;
    private readonly bool _dryRun;

    public DownloadService(IConfluenceApiClient apiClient, ILogger<DownloadService> logger, bool dryRun = false)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dryRun = dryRun;
    }

    public async Task DownloadAsync(string spaceKey, string? pageId, string? pageTitle, string outputDir, bool recursive, string overwriteStrategy = "fail")
    {
        var resolvedPageId = await LocalStorageHelper.ResolvePageIdAsync(_apiClient, spaceKey, pageId, pageTitle);
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

        var pageDirectoryIndex = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
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

    private string ResolvePageDirectoryForDownload(
        PageData page,
        string parentDir,
        Dictionary<string, string> pageDirectoryIndex)
    {
        var expectedDir = Path.GetFullPath(Path.Combine(parentDir, LocalStorageHelper.SanitizeFileName(page.Title)));
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

        if (LocalStorageHelper.PathsEqual(normalizedExistingDir, expectedDir))
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
                var markerAtExpected = LocalStorageHelper.ReadPageIdFromMarker(expectedDir);
                if (string.Equals(markerAtExpected, page.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(normalizedExistingDir, true);
                    LocalStorageHelper.UpdateDirectoryIndexPaths(pageDirectoryIndex, normalizedExistingDir, expectedDir);
                    pageDirectoryIndex[page.Id] = expectedDir;
                    return expectedDir;
                }

                var backupPath = $"{expectedDir}.conflict_{DateTime.UtcNow:yyyyMMddHHmmssfff}";
                Directory.Move(expectedDir, backupPath);
                _logger.LogWarning(
                    "Target directory already existed and was moved aside: {ExpectedDir} -> {BackupPath}",
                    expectedDir,
                    backupPath);
                LocalStorageHelper.UpdateDirectoryIndexPaths(pageDirectoryIndex, expectedDir, backupPath);
            }

            Directory.Move(normalizedExistingDir, expectedDir);
        }

        LocalStorageHelper.UpdateDirectoryIndexPaths(pageDirectoryIndex, normalizedExistingDir, expectedDir);
        pageDirectoryIndex[page.Id] = expectedDir;
        return expectedDir;
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
            var filePath = Path.Combine(pageDir, LocalStorageHelper.SanitizeFileName(att.Title));

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
}
