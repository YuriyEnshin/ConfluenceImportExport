using System.Diagnostics;
using System.Security.Cryptography;
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

    public async Task<SyncReport> DownloadUpdateAsync(
        string spaceKey, string? pageId, string? pageTitle,
        string outputDir, bool recursive)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();
        var resolvedPageId = await ResolvePageId(spaceKey, pageId, pageTitle);

        var pageDirectoryIndex = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
        var page = await _apiClient.GetPageByIdAsync(resolvedPageId);
        await DownloadPageUpdateAsync(page, outputDir, recursive, pageDirectoryIndex, report);
        _logger.LogInformation(
            "[PROFILE] DownloadUpdate completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    public async Task<SyncReport> DownloadMergeAsync(
        string spaceKey, string? pageId, string? pageTitle,
        string outputDir, bool recursive, ChangeSourceAnalyzer analyzer)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();
        var resolvedPageId = await ResolvePageId(spaceKey, pageId, pageTitle);

        var pageDirectoryIndex = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
        var page = await _apiClient.GetPageByIdAsync(resolvedPageId);
        await DownloadPageMergeAsync(page, outputDir, recursive, pageDirectoryIndex, analyzer, report);
        _logger.LogInformation(
            "[PROFILE] DownloadMerge completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    private async Task<string> ResolvePageId(string spaceKey, string? pageId, string? pageTitle)
    {
        var resolved = await LocalStorageHelper.ResolvePageIdAsync(_apiClient, spaceKey, pageId, pageTitle);
        return resolved ?? throw new InvalidOperationException(
            $"Could not resolve page. ID: '{pageId}', Title: '{pageTitle}'");
    }

    private async Task DownloadPageUpdateAsync(
        PageData page, string parentDir, bool recursive,
        Dictionary<string, string> pageDirectoryIndex, SyncReport report)
    {
        var pageDir = ResolvePageDirectoryForDownload(page, parentDir, pageDirectoryIndex);

        if (!_dryRun)
            Directory.CreateDirectory(pageDir);

        await SavePageContentForUpdate(page, pageDir);
        await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title);

        var attachments = await _apiClient.GetAttachmentsAsync(page.Id);
        await SaveAttachments(attachments, pageDir);

        if (recursive)
        {
            var children = await _apiClient.GetChildrenPagesAsync(page.Id);
            foreach (var child in children)
                await DownloadPageUpdateAsync(child, pageDir, recursive, pageDirectoryIndex, report);
        }
    }

    private async Task DownloadPageMergeAsync(
        PageData page, string parentDir, bool recursive,
        Dictionary<string, string> pageDirectoryIndex,
        ChangeSourceAnalyzer analyzer, SyncReport report)
    {
        var pageDir = ResolvePageDirectoryForDownload(page, parentDir, pageDirectoryIndex);

        if (!_dryRun)
            Directory.CreateDirectory(pageDir);

        var serverContent = page.Body.Storage.Value;
        var localContent = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir);

        if (localContent == null)
        {
            await WritePageContent(page.Title, pageDir, serverContent);
            await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title);
        }
        else if (StorageFormatNormalizer.ContentEquals(localContent, serverContent))
        {
            _logger.LogDebug("Page '{Title}' content is unchanged, skipping", page.Title);
            await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title);
        }
        else
        {
            var markerInfo = LocalStorageHelper.ReadPageMarkerInfo(pageDir);
            var syncTime = LocalStorageHelper.GetMarkerFileTimeUtc(pageDir);
            var indexPath = Path.Combine(pageDir, "index.html");
            DateTime? localFileTime = File.Exists(indexPath) ? File.GetLastWriteTimeUtc(indexPath) : null;

            var sourceInfo = analyzer.AnalyzeContentChange(
                page.Version?.When?.ToUniversalTime(), localFileTime,
                markerInfo?.Version, page.Version?.Number, syncTime);

            switch (sourceInfo.Origin)
            {
                case ChangeOrigin.Server:
                    _logger.LogInformation("Page '{Title}' changed on server, downloading", page.Title);
                    await WritePageContent(page.Title, pageDir, serverContent);
                    await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title);
                    break;

                case ChangeOrigin.Local:
                    _logger.LogInformation("Page '{Title}' changed locally, skipping download", page.Title);
                    report.AddSkipped(page.Id, page.Title, sourceInfo.Reason);
                    break;

                case ChangeOrigin.Conflict:
                    _logger.LogWarning("CONFLICT: Page '{Title}' changed both locally and on server", page.Title);
                    report.AddConflict(page.Id, page.Title, sourceInfo.Reason);
                    break;

                default:
                    _logger.LogWarning("Page '{Title}' change source unknown, skipping download", page.Title);
                    report.AddSkipped(page.Id, page.Title, sourceInfo.Reason);
                    break;
            }
        }

        var attachments = await _apiClient.GetAttachmentsAsync(page.Id);
        await SaveAttachments(attachments, pageDir);

        if (recursive)
        {
            var children = await _apiClient.GetChildrenPagesAsync(page.Id);
            foreach (var child in children)
                await DownloadPageMergeAsync(child, pageDir, recursive, pageDirectoryIndex, analyzer, report);
        }
    }

    private async Task SavePageContentForUpdate(PageData page, string pageDir)
    {
        var filePath = Path.Combine(pageDir, "index.html");
        var content = page.Body.Storage.Value;

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would save page '{Title}' -> {File}", page.Title, filePath);
            return;
        }

        if (File.Exists(filePath))
        {
            var existingContent = await File.ReadAllTextAsync(filePath);
            if (StorageFormatNormalizer.ContentEquals(existingContent, content))
            {
                _logger.LogDebug("Page '{Title}' content is unchanged, skipping rewrite", page.Title);
                return;
            }
        }

        await File.WriteAllTextAsync(filePath, content);
        _logger.LogInformation("Saved page '{Title}' -> {File}", page.Title, filePath);
    }

    private async Task WritePageContent(string title, string pageDir, string content)
    {
        var filePath = Path.Combine(pageDir, "index.html");

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would save page '{Title}' -> {File}", title, filePath);
            return;
        }

        await File.WriteAllTextAsync(filePath, content);
        _logger.LogInformation("Saved page '{Title}' -> {File}", title, filePath);
    }

    private string ResolvePageDirectoryForDownload(
        PageData page, string parentDir,
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
            Directory.CreateDirectory(expectedParent);

        _logger.LogInformation(
            "Page {PageId} location changed on Confluence. Moving local directory: {OldPath} -> {NewPath}",
            page.Id, normalizedExistingDir, expectedDir);

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
                    expectedDir, backupPath);
                LocalStorageHelper.UpdateDirectoryIndexPaths(pageDirectoryIndex, expectedDir, backupPath);
            }

            Directory.Move(normalizedExistingDir, expectedDir);
        }

        LocalStorageHelper.UpdateDirectoryIndexPaths(pageDirectoryIndex, normalizedExistingDir, expectedDir);
        pageDirectoryIndex[page.Id] = expectedDir;
        return expectedDir;
    }

    private async Task SavePageIdMarker(string pageId, int? version, string pageDir, string? originalTitle = null)
    {
        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would create ID marker: .id{PageId}_{Version}", pageId, version);
            return;
        }

        var existing = LocalStorageHelper.ReadPageMarkerInfo(pageDir);
        var existingTitle = LocalStorageHelper.ReadOriginalTitle(pageDir);
        if (existing != null
            && string.Equals(existing.PageId, pageId, StringComparison.OrdinalIgnoreCase)
            && existing.Version == version
            && existingTitle != null)
        {
            return;
        }

        await LocalStorageHelper.WritePageIdMarkerAsync(pageDir, pageId, version, originalTitle);
    }

    private async Task SaveAttachments(List<AttachmentData> attachments, string pageDir)
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

                if (IsLocalFileSizeMatch(filePath, att))
                {
                    _logger.LogDebug(
                        "Attachment '{Title}' is up to date (size: {Size}), skipping download",
                        att.Title, att.Extensions!.FileSize);
                    continue;
                }

                var fileContent = await _apiClient.DownloadAttachmentAsync(att.Links.DownloadUrl);

                if (await IsLocalContentMatchAsync(filePath, fileContent))
                {
                    _logger.LogDebug(
                        "Attachment '{Title}' content is unchanged after download (API fileSize mismatch: {ApiSize} vs actual {ActualSize}), skipping rewrite",
                        att.Title, att.Extensions?.FileSize, fileContent.Length);
                    continue;
                }

                await File.WriteAllBytesAsync(filePath, fileContent);
                _logger.LogInformation("Downloaded attachment '{Title}' -> {Path}", att.Title, filePath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download attachment: {Title}", att.Title);
            }
        }
    }

    private static bool IsLocalFileSizeMatch(string filePath, AttachmentData serverAttachment)
    {
        if (!File.Exists(filePath))
            return false;

        if (serverAttachment.Extensions?.FileSize is not long remoteSize)
            return false;

        return new FileInfo(filePath).Length == remoteSize;
    }

    private async Task<bool> IsLocalContentMatchAsync(string filePath, byte[] downloadedContent)
    {
        if (!File.Exists(filePath))
            return false;

        var localFileInfo = new FileInfo(filePath);
        if (localFileInfo.Length != downloadedContent.Length)
            return false;

        var started = Stopwatch.GetTimestamp();
        var downloadedHash = SHA256.HashData(downloadedContent);
        await using var stream = File.OpenRead(filePath);
        var localHash = await SHA256.HashDataAsync(stream);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        _logger.LogDebug(
            "[PROFILE] SHA256 compared ({Bytes} bytes) in {ElapsedMs}ms: {Path}",
            downloadedContent.Length, elapsedMs, filePath);

        return localHash.AsSpan().SequenceEqual(downloadedHash);
    }
}
