using Microsoft.Extensions.Logging;

namespace ConfluencePageExporter.Services;

public class UploadService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<UploadService> _logger;
    private readonly bool _dryRun;

    public UploadService(IConfluenceApiClient apiClient, ILogger<UploadService> logger, bool dryRun = false)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dryRun = dryRun;
    }

    public async Task UploadUpdateAsync(string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle, bool recursive, string onError = "abort", bool movePages = false)
    {
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        var rootPageId = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle);

        await UpdatePageContentAndAttachments(spaceKey, rootPageId, sourceDir);

        if (recursive)
        {
            foreach (var childDir in LocalStorageHelper.GetPageSubdirectories(sourceDir))
            {
                await ProcessChildForUpdate(spaceKey, childDir, rootPageId, onError, movePages);
            }
        }
    }

    private async Task<string> ResolveRootPageForUpdate(string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle)
    {
        if (!string.IsNullOrEmpty(explicitPageId))
        {
            var page = await _apiClient.TryGetPageByIdAsync(explicitPageId);
            if (page == null)
                throw new InvalidOperationException($"Page with ID '{explicitPageId}' not found in Confluence");
            return page.Id;
        }

        if (!string.IsNullOrEmpty(explicitPageTitle))
        {
            var foundId = await _apiClient.FindPageByTitleAsync(spaceKey, null, explicitPageTitle);
            if (foundId == null)
                throw new InvalidOperationException($"Page with title '{explicitPageTitle}' not found in space '{spaceKey}'");
            return foundId;
        }

        var markerPageId = LocalStorageHelper.ReadPageIdFromMarker(sourceDir);
        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
                return page.Id;
            _logger.LogWarning("Page with ID '{PageId}' from .id marker not found, falling back to title search", markerPageId);
        }

        var folderName = LocalStorageHelper.GetPageTitleFromDirectory(sourceDir);
        var foundByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName);
        if (foundByTitle != null)
            return foundByTitle;

        throw new InvalidOperationException(
            $"Could not find a matching Confluence page for '{folderName}'. " +
            "Specify --page-id or --page-title, or use 'upload create' for new pages.");
    }

    private async Task ProcessChildForUpdate(string spaceKey, string childDir, string parentPageId, string onError, bool movePages = false)
    {
        var folderName = LocalStorageHelper.GetPageTitleFromDirectory(childDir);
        var markerPageId = LocalStorageHelper.ReadPageIdFromMarker(childDir);
        string? resolvedPageId = null;
        string? moveToParentId = null;
        bool shouldCreate = false;

        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId);
            if (page != null)
            {
                if (page.ParentId == parentPageId)
                {
                    resolvedPageId = page.Id;
                }
                else if (movePages)
                {
                    _logger.LogInformation(
                        "Page '{Title}' (ID: {PageId}) will be moved from parent {OldParent} to {NewParent}",
                        page.Title, page.Id, page.ParentId, parentPageId);
                    resolvedPageId = page.Id;
                    moveToParentId = parentPageId;
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
                    if (movePages)
                    {
                        _logger.LogInformation(
                            "Page '{Title}' (ID: {PageId}) found in space but under a different parent, will be moved to parent {NewParent}",
                            folderName, foundGlobally, parentPageId);
                        resolvedPageId = foundGlobally;
                        moveToParentId = parentPageId;
                    }
                    else
                    {
                        var msg = $"Page '{folderName}' exists in space '{spaceKey}' but under a different parent";
                        _logger.LogError("{Message}", msg);
                        if (onError == "abort")
                            throw new InvalidOperationException(msg);
                        return;
                    }
                }
                else
                {
                    shouldCreate = true;
                }
            }
        }

        if (shouldCreate)
        {
            resolvedPageId = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
            if (resolvedPageId == null) return;

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
            {
                await ProcessChildForCreate(spaceKey, grandchildDir, resolvedPageId);
            }
        }
        else
        {
            await UpdatePageContentAndAttachments(spaceKey, resolvedPageId!, childDir, moveToParentId);

            foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
            {
                await ProcessChildForUpdate(spaceKey, grandchildDir, resolvedPageId!, onError, movePages);
            }
        }
    }

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

        var createdPageId = await CreatePageFromDirectory(spaceKey, sourceDir, resolvedParentId);
        if (createdPageId == null) return;

        if (recursive)
        {
            foreach (var childDir in LocalStorageHelper.GetPageSubdirectories(sourceDir))
            {
                await ProcessChildForCreate(spaceKey, childDir, createdPageId);
            }
        }
    }

    private async Task ProcessChildForCreate(string spaceKey, string childDir, string? parentPageId)
    {
        var createdPageId = await CreatePageFromDirectory(spaceKey, childDir, parentPageId);
        if (createdPageId == null) return;

        foreach (var grandchildDir in LocalStorageHelper.GetPageSubdirectories(childDir))
        {
            await ProcessChildForCreate(spaceKey, grandchildDir, createdPageId);
        }
    }

    private async Task<string?> CreatePageFromDirectory(string spaceKey, string pageDir, string? parentId)
    {
        var title = LocalStorageHelper.GetPageTitleFromDirectory(pageDir);
        var content = await LocalStorageHelper.ReadPageContent(pageDir);

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

    private async Task UpdatePageContentAndAttachments(string spaceKey, string pageId, string pageDir, string? moveToParentId = null)
    {
        var title = LocalStorageHelper.GetPageTitleFromDirectory(pageDir);
        var content = await LocalStorageHelper.ReadPageContent(pageDir);

        if (_dryRun)
        {
            if (moveToParentId != null)
                _logger.LogInformation("DRY RUN: Would move page {PageId} to parent {NewParent} and update with title '{Title}'", pageId, moveToParentId, title);
            else
                _logger.LogInformation("DRY RUN: Would update page {PageId} with title '{Title}'", pageId, title);

            var existingByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, title);
            if (existingByTitle != null && existingByTitle != pageId)
            {
                _logger.LogWarning("DRY RUN: Renaming page {PageId} to '{Title}' would conflict with existing page {ConflictId}",
                    pageId, title, existingByTitle);
            }

            LogDryRunAttachments(pageDir);
            return;
        }

        var updatedId = await _apiClient.UpdatePageAsync(pageId, title, content, moveToParentId);
        if (updatedId == null)
        {
            _logger.LogError("Failed to update page {PageId} with title '{Title}'", pageId, title);
            return;
        }

        if (moveToParentId != null)
            _logger.LogInformation("Moved and updated page {PageId} with title '{Title}' to parent {NewParent}", pageId, title, moveToParentId);
        else
            _logger.LogInformation("Updated page {PageId} with title '{Title}'", pageId, title);
        await UploadPageAttachments(pageId, pageDir);
    }

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
                await _apiClient.DeleteAttachmentAsync(pageId, existing.Id);

            await _apiClient.UploadAttachmentAsync(pageId, file, fileName);
            _logger.LogInformation("Uploaded attachment '{FileName}' to page {PageId}", fileName, pageId);
        }
    }

    private void LogDryRunAttachments(string pageDir)
    {
        foreach (var file in LocalStorageHelper.GetAttachmentFiles(pageDir))
        {
            _logger.LogInformation("DRY RUN: Would upload attachment '{FileName}'", Path.GetFileName(file));
        }
    }
}
