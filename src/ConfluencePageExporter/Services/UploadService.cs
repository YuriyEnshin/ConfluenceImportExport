using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public class UploadService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<UploadService> _logger;
    private readonly bool _dryRun;
    private readonly int _maxParallelism;

    public UploadService(
        IConfluenceApiClient apiClient,
        ILogger<UploadService> logger,
        bool dryRun = false,
        int maxParallelism = 8)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dryRun = dryRun;
        _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
    }

    // ── upload update (force: local → server) ─────────────────────────

    public async Task<SyncReport> UploadUpdateAsync(
        string spaceKey, string sourceDir, string? explicitPageId,
        string? explicitPageTitle, bool recursive)
    {
        var report = new SyncReport();
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        var (rootPageId, _) = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle);

        var moveToParentId = await DetectRootPageMoveAsync(rootPageId, sourceDir);
        var (result, effectiveTitle) = await UpdatePageContentAndAttachments(spaceKey, rootPageId, sourceDir, moveToParentId);
        if (result != null)
            await UpdatePageIdMarker(sourceDir, result.Id, result.VersionNumber, effectiveTitle);

        if (recursive)
        {
            foreach (var childDir in LocalStorageHelper.GetPageSubdirectories(sourceDir))
                await ProcessChildForUpdate(spaceKey, childDir, rootPageId, report);
        }

        return report;
    }

    // ── upload merge (smart: local → server, only local-newer) ────────

    public async Task<SyncReport> UploadMergeAsync(
        string spaceKey, string sourceDir, string? explicitPageId,
        string? explicitPageTitle, bool recursive, ChangeSourceAnalyzer analyzer)
    {
        var report = new SyncReport();
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        var (rootPageId, _) = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle);

        await MergeUploadPageAsync(spaceKey, rootPageId, sourceDir, analyzer, report);

        if (recursive)
        {
            foreach (var childDir in LocalStorageHelper.GetPageSubdirectories(sourceDir))
                await ProcessChildForMerge(spaceKey, childDir, rootPageId, analyzer, report);
        }

        return report;
    }

    // ── upload create (unchanged) ─────────────────────────────────────

    public async Task UploadCreateAsync(string spaceKey, string sourceDir, string? parentId, string? parentTitle, bool recursive)
    {
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        string? resolvedParentId = null;
        if (!string.IsNullOrEmpty(parentId) || !string.IsNullOrEmpty(parentTitle))
        {
            resolvedParentId = await LocalStorageHelper.ResolvePageIdAsync(_apiClient, spaceKey, parentId, parentTitle);
            if (resolvedParentId == null)
                throw new InvalidOperationException(
                    $"Parent page not found. ID: '{parentId}', Title: '{parentTitle}'");
        }

        var (createResult, effectiveTitle) = await CreatePageFromDirectory(spaceKey, sourceDir, resolvedParentId);
        if (createResult == null) return;
        await UpdatePageIdMarker(sourceDir, createResult.Id, createResult.VersionNumber, effectiveTitle);

        if (recursive)
        {
            foreach (var childDir in LocalStorageHelper.GetPageSubdirectories(sourceDir))
                await ProcessChildForCreate(spaceKey, childDir, createResult.Id);
        }
    }

    // ── update internals ──────────────────────────────────────────────

    private async Task<string?> DetectRootPageMoveAsync(string rootPageId, string sourceDir)
    {
        var parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir));
        if (string.IsNullOrEmpty(parentDir))
            return null;

        var localParentPageId = LocalStorageHelper.ReadPageIdFromMarker(parentDir);
        if (localParentPageId == null)
            return null;

        var rootPage = await _apiClient.GetPageByIdAsync(rootPageId);
        if (string.Equals(rootPage.ParentId, localParentPageId, StringComparison.OrdinalIgnoreCase))
            return null;

        _logger.LogInformation(
            "Root page '{Title}' (ID: {PageId}) will be moved from parent {OldParent} to {NewParent}",
            rootPage.Title, rootPageId, rootPage.ParentId, localParentPageId);
        return localParentPageId;
    }

    private async Task<(string PageId, bool ResolvedByTitle)> ResolveRootPageForUpdate(
        string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle)
    {
        if (!string.IsNullOrEmpty(explicitPageId))
        {
            var page = await _apiClient.TryGetPageByIdAsync(explicitPageId);
            if (page == null)
                throw new InvalidOperationException($"Page with ID '{explicitPageId}' not found in Confluence");
            return (page.Id, false);
        }

        if (!string.IsNullOrEmpty(explicitPageTitle))
        {
            var foundId = await _apiClient.FindPageByTitleAsync(spaceKey, null, explicitPageTitle);
            if (foundId == null)
                throw new InvalidOperationException($"Page with title '{explicitPageTitle}' not found in space '{spaceKey}'");
            return (foundId, true);
        }

        var markerPageId = LocalStorageHelper.ReadPageIdFromMarker(sourceDir);
        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
                return (page.Id, false);
            _logger.LogWarning("Page with ID '{PageId}' from .id marker not found, falling back to title search", markerPageId);
        }

        var folderName = LocalStorageHelper.GetPageTitle(sourceDir);
        var foundByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName);
        if (foundByTitle != null)
            return (foundByTitle, true);

        throw new InvalidOperationException(
            $"Could not find a matching Confluence page for '{folderName}'. " +
            "Specify --page-id or --page-title, or use 'upload create' for new pages.");
    }

    private async Task ProcessChildForUpdate(string spaceKey, string childDir, string parentPageId, SyncReport report)
    {
        var folderName = LocalStorageHelper.GetPageTitle(childDir);
        var markerPageId = LocalStorageHelper.ReadPageIdFromMarker(childDir);
        string? resolvedPageId = null;
        string? moveToParentId = null;
        bool shouldCreate = false;

        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
            {
                resolvedPageId = page.Id;
                if (page.ParentId != parentPageId)
                {
                    _logger.LogInformation(
                        "Page '{Title}' (ID: {PageId}) will be moved from parent {OldParent} to {NewParent}",
                        page.Title, page.Id, page.ParentId, parentPageId);
                    moveToParentId = parentPageId;
                }
            }
        }

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
                    _logger.LogInformation(
                        "Page '{Title}' (ID: {PageId}) found in space but under a different parent, will be moved to parent {NewParent}",
                        folderName, foundGlobally, parentPageId);
                    resolvedPageId = foundGlobally;
                    moveToParentId = parentPageId;
                }
                else
                {
                    shouldCreate = true;
                }
            }
        }

        if (shouldCreate)
        {
            var (createResult, effectiveTitle) = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
            if (createResult == null) return;
            resolvedPageId = createResult.Id;
            await UpdatePageIdMarker(childDir, createResult.Id, createResult.VersionNumber, effectiveTitle);

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
                await ProcessChildForCreate(spaceKey, grandchildDir, resolvedPageId);
        }
        else
        {
            var (updateResult, effectiveTitle) = await UpdatePageContentAndAttachments(spaceKey, resolvedPageId!, childDir, moveToParentId);
            if (updateResult != null)
                await UpdatePageIdMarker(childDir, updateResult.Id, updateResult.VersionNumber, effectiveTitle);

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
                await ProcessChildForUpdate(spaceKey, grandchildDir, resolvedPageId!, report);
        }
    }

    // ── merge internals ───────────────────────────────────────────────

    private async Task MergeUploadPageAsync(
        string spaceKey, string pageId, string pageDir,
        ChangeSourceAnalyzer analyzer, SyncReport report)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var localContent = await LocalStorageHelper.ReadPageContent(pageDir);

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would merge-upload page {PageId} with title '{Title}'", pageId, title);
            LogDryRunAttachments(pageDir);
            return;
        }

        var serverPage = await _apiClient.GetPageByIdAsync(pageId);

        if (LocalStorageHelper.ReadOriginalTitle(pageDir) == null
            && string.Equals(LocalStorageHelper.SanitizeFileName(serverPage.Title), title, StringComparison.OrdinalIgnoreCase))
        {
            title = serverPage.Title;
        }

        bool contentChanged = !StorageFormatNormalizer.ContentEquals(localContent, serverPage.Body.Storage.Value);
        bool titleChanged = !string.Equals(title, serverPage.Title, StringComparison.Ordinal);

        if (!contentChanged && !titleChanged)
        {
            _logger.LogDebug("Page {PageId} '{Title}' is unchanged, skipping merge-upload", pageId, title);
            await UpdatePageIdMarker(pageDir, pageId, serverPage.Version?.Number, title);
            return;
        }

        var markerInfo = LocalStorageHelper.ReadPageMarkerInfo(pageDir);
        var syncTime = LocalStorageHelper.GetMarkerFileTimeUtc(pageDir);
        var indexPath = Path.Combine(pageDir, "index.html");
        DateTime? localFileTime = File.Exists(indexPath) ? File.GetLastWriteTimeUtc(indexPath) : null;

        var sourceInfo = analyzer.AnalyzeContentChange(
            serverPage.Version?.When?.ToUniversalTime(), localFileTime,
            markerInfo?.Version, serverPage.Version?.Number, syncTime);

        switch (sourceInfo.Origin)
        {
            case ChangeOrigin.Local:
                _logger.LogInformation("Page '{Title}' changed locally, uploading to server", title);
                var result = await _apiClient.UpdatePageAsync(pageId, title, localContent, null);
                if (result != null)
                {
                    await UpdatePageIdMarker(pageDir, result.Id, result.VersionNumber, title);
                    await UploadPageAttachments(pageId, pageDir);
                }
                break;

            case ChangeOrigin.Server:
                _logger.LogInformation("Page '{Title}' changed on server, skipping upload", title);
                report.AddSkipped(pageId, title, sourceInfo.Reason);
                break;

            case ChangeOrigin.Conflict:
                _logger.LogWarning("CONFLICT: Page '{Title}' changed both locally and on server", title);
                report.AddConflict(pageId, title, sourceInfo.Reason);
                break;

            default:
                _logger.LogWarning("Page '{Title}' change source unknown, skipping upload", title);
                report.AddSkipped(pageId, title, sourceInfo.Reason);
                break;
        }
    }

    private async Task ProcessChildForMerge(
        string spaceKey, string childDir, string parentPageId,
        ChangeSourceAnalyzer analyzer, SyncReport report)
    {
        var folderName = LocalStorageHelper.GetPageTitle(childDir);
        var markerPageId = LocalStorageHelper.ReadPageIdFromMarker(childDir);
        string? resolvedPageId = null;

        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
                resolvedPageId = page.Id;
        }

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
                    resolvedPageId = foundGlobally;
            }
        }

        if (resolvedPageId == null)
        {
            var (createResult, effectiveTitle) = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
            if (createResult == null) return;
            resolvedPageId = createResult.Id;
            await UpdatePageIdMarker(childDir, createResult.Id, createResult.VersionNumber, effectiveTitle);

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
                await ProcessChildForCreate(spaceKey, grandchildDir, resolvedPageId);
        }
        else
        {
            await MergeUploadPageAsync(spaceKey, resolvedPageId, childDir, analyzer, report);

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
                await ProcessChildForMerge(spaceKey, grandchildDir, resolvedPageId, analyzer, report);
        }
    }

    // ── create internals ──────────────────────────────────────────────

    private async Task ProcessChildForCreate(string spaceKey, string childDir, string? parentPageId)
    {
        var (createResult, effectiveTitle) = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
        if (createResult == null) return;
        await UpdatePageIdMarker(childDir, createResult.Id, createResult.VersionNumber, effectiveTitle);

        foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
            await ProcessChildForCreate(spaceKey, grandchildDir, createResult.Id);
    }

    private async Task<(PageUpdateResult? Result, string? Title)> CreatePageFromDirectory(string spaceKey, string pageDir, string? parentId)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var content = await LocalStorageHelper.ReadPageContent(pageDir);

        var existingId = await _apiClient.FindPageByTitleAsync(spaceKey, null, title);
        if (existingId != null)
        {
            _logger.LogError("Cannot create page '{Title}': a page with this title already exists (ID: {ExistingId})", title, existingId);
            return (null, null);
        }

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would create page '{Title}' under parent {ParentId}", title, parentId ?? "(space root)");
            LogDryRunAttachments(pageDir);
            return (new PageUpdateResult($"dry-run-{title}", 1), title);
        }

        var result = await _apiClient.CreatePageAsync(spaceKey, parentId, title, content);
        if (result == null)
        {
            _logger.LogError("Failed to create page '{Title}'", title);
            return (null, null);
        }

        _logger.LogInformation("Created page '{Title}' with ID {PageId}", title, result.Id);
        await UploadPageAttachments(result.Id, pageDir);
        return (result, title);
    }

    // ── shared: page content update ───────────────────────────────────

    private async Task<(PageUpdateResult? Result, string? Title)> UpdatePageContentAndAttachments(
        string spaceKey, string pageId, string pageDir, string? moveToParentId = null)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var localContent = await LocalStorageHelper.ReadPageContent(pageDir);

        if (_dryRun)
        {
            if (moveToParentId != null)
                _logger.LogInformation("DRY RUN: Would move page {PageId} to parent {NewParent} and update with title '{Title}'", pageId, moveToParentId, title);
            else
                _logger.LogInformation("DRY RUN: Would update page {PageId} with title '{Title}'", pageId, title);

            var existingByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, title);
            if (existingByTitle != null && existingByTitle != pageId)
                _logger.LogWarning("DRY RUN: Renaming page {PageId} to '{Title}' would conflict with existing page {ConflictId}",
                    pageId, title, existingByTitle);

            LogDryRunAttachments(pageDir);
            return (null, null);
        }

        var serverPage = await _apiClient.GetPageByIdAsync(pageId);

        if (LocalStorageHelper.ReadOriginalTitle(pageDir) == null
            && string.Equals(LocalStorageHelper.SanitizeFileName(serverPage.Title), title, StringComparison.OrdinalIgnoreCase))
        {
            title = serverPage.Title;
        }

        var serverVersion = serverPage.Version?.Number;

        bool titleChanged = !string.Equals(title, serverPage.Title, StringComparison.Ordinal);
        bool contentChanged = !StorageFormatNormalizer.ContentEquals(localContent, serverPage.Body.Storage.Value);
        bool parentChanged = moveToParentId != null;

        if (!titleChanged && !contentChanged && !parentChanged)
        {
            _logger.LogInformation(
                "Page {PageId} '{Title}' is unchanged (title, content, parent match server), skipping update",
                pageId, title);
            return (new PageUpdateResult(pageId, serverVersion ?? 0), title);
        }

        _logger.LogDebug("Page {PageId} changes detected: title={TitleChanged}, content={ContentChanged}, parent={ParentChanged}",
            pageId, titleChanged, contentChanged, parentChanged);

        var result = await _apiClient.UpdatePageAsync(pageId, title, localContent, moveToParentId);
        if (result == null)
        {
            _logger.LogError("Failed to update page {PageId} with title '{Title}'", pageId, title);
            return (null, null);
        }

        if (moveToParentId != null)
            _logger.LogInformation("Moved and updated page {PageId} with title '{Title}' to parent {NewParent}", pageId, title, moveToParentId);
        else
            _logger.LogInformation("Updated page {PageId} with title '{Title}'", pageId, title);
        await UploadPageAttachments(pageId, pageDir);
        return (result, title);
    }

    // ── shared: attachments ───────────────────────────────────────────

    private async Task UploadPageAttachments(string pageId, string pageDir)
    {
        var files = LocalStorageHelper.GetAttachmentFiles(pageDir).ToList();
        if (files.Count == 0) return;

        var existingAttachments = await _apiClient.GetAttachmentsAsync(pageId);

        foreach (var file in files)
        {
            var fileName = Path.GetFileName(file);
            var existing = existingAttachments.FirstOrDefault(
                a => a.Title.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                bool changed = await IsAttachmentChangedAsync(file, existing);
                if (!changed)
                {
                    _logger.LogDebug("Attachment '{FileName}' on page {PageId} is unchanged, skipping", fileName, pageId);
                    continue;
                }

                var updated = await _apiClient.UpdateAttachmentDataAsync(pageId, existing.Id, file, fileName);
                if (updated)
                    _logger.LogInformation("Updated attachment '{FileName}' (new version) on page {PageId}", fileName, pageId);
            }
            else
            {
                var uploaded = await _apiClient.UploadAttachmentAsync(pageId, file, fileName);
                if (uploaded)
                    _logger.LogInformation("Uploaded new attachment '{FileName}' to page {PageId}", fileName, pageId);
            }
        }
    }

    private async Task<bool> IsAttachmentChangedAsync(string localFilePath, AttachmentData serverAttachment)
    {
        var localFileInfo = new FileInfo(localFilePath);
        if (!localFileInfo.Exists)
            return false;

        if (serverAttachment.Extensions?.FileSize is long remoteSize && localFileInfo.Length != remoteSize)
        {
            _logger.LogDebug(
                "Attachment '{FileName}' size differs: local={LocalSize}, server={ServerSize}",
                serverAttachment.Title, localFileInfo.Length, remoteSize);
            return true;
        }

        var remoteContent = await _apiClient.DownloadAttachmentAsync(serverAttachment.Links.DownloadUrl);
        var localHash = await ComputeFileHashAsync(localFilePath);
        var remoteHash = ComputeHash(remoteContent);

        bool differs = !localHash.SequenceEqual(remoteHash);
        if (differs)
            _logger.LogDebug("Attachment '{FileName}' content hash differs", serverAttachment.Title);

        return differs;
    }

    private static async Task<byte[]> ComputeFileHashAsync(string filePath)
    {
        await using var stream = File.OpenRead(filePath);
        return await SHA256.HashDataAsync(stream);
    }

    private static byte[] ComputeHash(byte[] data) => SHA256.HashData(data);

    // ── shared: utilities ─────────────────────────────────────────────

    private void LogDryRunAttachments(string pageDir)
    {
        foreach (var file in LocalStorageHelper.GetAttachmentFiles(pageDir))
            _logger.LogInformation("DRY RUN: Would upload attachment '{FileName}'", Path.GetFileName(file));
    }

    private async Task UpdatePageIdMarker(string pageDir, string pageId, int? version, string? originalTitle = null)
    {
        if (_dryRun) return;

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
        _logger.LogInformation("Saved page ID marker '.id{PageId}_{Version}' in '{PageDir}'", pageId, version, pageDir);
    }
}
