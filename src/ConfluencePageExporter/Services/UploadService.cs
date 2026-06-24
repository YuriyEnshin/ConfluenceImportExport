using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;

namespace ConfluencePageExporter.Services;

public class UploadService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly IContentNormalizer _normalizer;
    private readonly IContentHasher _hasher;
    private readonly ILogger<UploadService> _logger;
    private readonly bool _dryRun;
    private readonly int _maxParallelism;

    public UploadService(
        IConfluenceApiClient apiClient,
        IContentNormalizer normalizer,
        ILogger<UploadService> logger,
        bool dryRun = false,
        int maxParallelism = GlobalOptions.DefaultMaxParallelism,
        IContentHasher? hasher = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        // Default the hasher from the same normalizer so existing constructors
        // (prod handlers and tests) keep working without a new dependency.
        _hasher = hasher ?? new ContentHasher(_normalizer);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dryRun = dryRun;
        _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
    }

    // ── upload update (force: local → server) ─────────────────────────

    public async Task<SyncReport> UploadUpdateAsync(
        string spaceKey, string sourceDir, string? explicitPageId,
        string? explicitPageTitle, bool recursive,
        string? explicitSpaceKey = null, bool multiTree = false, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();

        if (multiTree && !LocalStorageHelper.IsPageDirectory(sourceDir))
        {
            await RunMultiTreeAsync(sourceDir, explicitPageId, explicitPageTitle, report,
                rootDir => UploadUpdateSingleTreeAsync(spaceKey, explicitSpaceKey, rootDir, null, null, recursive, report, ct), ct);
        }
        else
        {
            EnsurePageDirectory(sourceDir);
            await UploadUpdateSingleTreeAsync(spaceKey, explicitSpaceKey, sourceDir, explicitPageId, explicitPageTitle, recursive, report, ct);
        }

        _logger.LogDebug(
            "[PROFILE] UploadUpdate completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    private async Task UploadUpdateSingleTreeAsync(
        string spaceKey, string? explicitSpaceKey, string sourceDir,
        string? explicitPageId, string? explicitPageTitle, bool recursive, SyncReport report, CancellationToken ct)
    {
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        var (rootPageId, _) = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle, ct);

        // Resolve the tree's space once from the root's server value (the
        // authority) and flow it down: new children are created in it, existing
        // pages are checked against it (cross-space guard), and markers are
        // stamped with it.
        var rootServer = await _apiClient.GetPageByIdAsync(rootPageId, ct);
        var treeSpace = ResolveTreeSpace(rootServer.SpaceKey, explicitSpaceKey, spaceKey,
            $"страница '{rootServer.Title}' (ID {rootPageId})");

        var moveToParentId = await DetectRootPageMoveAsync(rootServer, sourceDir, treeSpace, report, ct);
        var (result, effectiveTitle) = await UpdatePageContentAndAttachments(treeSpace, rootPageId, sourceDir, report, moveToParentId, ct);
        if (result != null)
            await UpdatePageIdMarker(sourceDir, result.Id, result.VersionNumber, effectiveTitle, treeSpace, ct);

        if (recursive)
        {
            await ProcessChildrenAsync(sourceDir, ct,
                childDir => ProcessChildForUpdate(treeSpace, childDir, rootPageId, report, ct));
        }
    }

    // ── upload merge (smart: local → server, only local-newer) ────────

    public async Task<SyncReport> UploadMergeAsync(
        string spaceKey, string sourceDir, string? explicitPageId,
        string? explicitPageTitle, bool recursive, ChangeSourceAnalyzer analyzer,
        string? explicitSpaceKey = null, bool multiTree = false, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();

        if (multiTree && !LocalStorageHelper.IsPageDirectory(sourceDir))
        {
            await RunMultiTreeAsync(sourceDir, explicitPageId, explicitPageTitle, report,
                rootDir => UploadMergeSingleTreeAsync(spaceKey, explicitSpaceKey, rootDir, null, null, recursive, analyzer, report, ct), ct);
        }
        else
        {
            EnsurePageDirectory(sourceDir);
            await UploadMergeSingleTreeAsync(spaceKey, explicitSpaceKey, sourceDir, explicitPageId, explicitPageTitle, recursive, analyzer, report, ct);
        }

        _logger.LogDebug(
            "[PROFILE] UploadMerge completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    private async Task UploadMergeSingleTreeAsync(
        string spaceKey, string? explicitSpaceKey, string sourceDir,
        string? explicitPageId, string? explicitPageTitle, bool recursive,
        ChangeSourceAnalyzer analyzer, SyncReport report, CancellationToken ct)
    {
        LocalStorageHelper.ValidateSourceDirectory(sourceDir);

        var (rootPageId, _) = await ResolveRootPageForUpdate(spaceKey, sourceDir, explicitPageId, explicitPageTitle, ct);

        var rootServer = await _apiClient.GetPageByIdAsync(rootPageId, ct);
        var treeSpace = ResolveTreeSpace(rootServer.SpaceKey, explicitSpaceKey, spaceKey,
            $"страница '{rootServer.Title}' (ID {rootPageId})");

        // Симметрично upload update: если локальная родительская папка имеет
        // .id-маркер и его ID отличается от серверного родителя страницы,
        // считаем, что пользователь перенёс папку локально, и применяем
        // структурное перемещение на сервере как часть merge-операции.
        var moveToParentId = await DetectRootPageMoveAsync(rootServer, sourceDir, treeSpace, report, ct);
        await MergeUploadPageAsync(treeSpace, rootPageId, sourceDir, moveToParentId, analyzer, report, ct);

        if (recursive)
        {
            await ProcessChildrenAsync(sourceDir, ct,
                childDir => ProcessChildForMerge(treeSpace, childDir, rootPageId, analyzer, report, ct));
        }
    }

    // ── upload create (unchanged) ─────────────────────────────────────

    public async Task UploadCreateAsync(string spaceKey, string sourceDir, string? parentId, string? parentTitle, bool recursive, string? explicitSpaceKey = null, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            LocalStorageHelper.ValidateSourceDirectory(sourceDir);

            string? resolvedParentId = null;
            var treeSpace = spaceKey;
            if (!string.IsNullOrEmpty(parentId) || !string.IsNullOrEmpty(parentTitle))
            {
                resolvedParentId = await _apiClient.ResolvePageIdAsync(spaceKey, parentId, parentTitle, ct);
                if (resolvedParentId == null)
                    throw new InvalidOperationException(
                        $"Parent page not found. ID: '{parentId}', Title: '{parentTitle}'");

                // New pages must live in the parent's space (Confluence
                // invariant). Take the parent's actual server space as the tree
                // space so the whole created subtree lands in the right place
                // regardless of the configured default; an explicitly requested
                // space that contradicts the parent is a user error.
                var parentPage = await _apiClient.TryGetPageByIdAsync(resolvedParentId, ct);
                if (parentPage?.SpaceKey != null)
                    treeSpace = ResolveTreeSpace(parentPage.SpaceKey, explicitSpaceKey, spaceKey,
                        $"родитель (ID {resolvedParentId})");
            }

            var (createResult, effectiveTitle) = await CreatePageFromDirectory(treeSpace, sourceDir, resolvedParentId, ct);
            if (createResult == null) return;
            await UpdatePageIdMarker(sourceDir, createResult.Id, createResult.VersionNumber, effectiveTitle, treeSpace, ct);

            if (recursive)
            {
                await ProcessChildrenAsync(sourceDir, ct,
                    childDir => ProcessChildForCreate(treeSpace, childDir, createResult.Id, ct));
            }
        }
        finally
        {
            _logger.LogDebug(
                "[PROFILE] UploadCreate completed in {ElapsedMs}ms",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    // ── multi-tree / tree-space resolution ────────────────────────────

    private static void EnsurePageDirectory(string sourceDir)
    {
        // Preserve the original DirectoryNotFoundException (and its message) for
        // a missing directory; only an existing-but-index-less folder gets the
        // multi-tree hint.
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");

        if (!LocalStorageHelper.IsPageDirectory(sourceDir))
            throw new InvalidOperationException(
                $"'{sourceDir}' не содержит index.html (это не папка страницы). Укажите корень дерева, "
                + "либо передайте multiTree=true для обработки всех деревьев (подпапок с index.html) в этом каталоге.");
    }

    /// <summary>
    /// Multi-tree dispatch: process every page tree directly under
    /// <paramref name="containerDir"/> independently — each resolves its own
    /// space, so trees from different spaces can be synced in one call. One
    /// tree's failure is recorded and skipped without aborting the rest; an
    /// auth failure is global and propagates. <c>pageId</c>/<c>pageTitle</c>
    /// identify a single page and are therefore incompatible with multi-tree.
    /// </summary>
    private async Task RunMultiTreeAsync(
        string containerDir, string? explicitPageId, string? explicitPageTitle,
        SyncReport report, Func<string, Task> processRoot, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(explicitPageId) || !string.IsNullOrEmpty(explicitPageTitle))
            throw new InvalidOperationException(
                "multiTree нельзя сочетать с pageId/pageTitle: они указывают одну страницу, а multiTree "
                + "обрабатывает несколько деревьев. Уберите pageId/pageTitle.");

        var roots = LocalStorageHelper.EnumerateTreeRoots(containerDir).ToList();
        if (roots.Count == 0)
            throw new InvalidOperationException(
                $"multiTree: в каталоге '{containerDir}' не найдено деревьев (непосредственных подпапок с index.html).");

        _logger.LogInformation("multiTree: обрабатываю {Count} дерев(а) из '{Dir}'", roots.Count, containerDir);
        await Parallel.ForEachAsync(
            roots,
            new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
            async (rootDir, _) =>
            {
                try
                {
                    await processRoot(rootDir);
                }
                catch (ConfluenceApiException ex) when (ex.IsAuthFailure)
                {
                    throw; // auth is global — abort the whole batch rather than per-tree skip
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "multiTree: дерево '{Dir}' пропущено из-за ошибки", rootDir);
                    report.AddSkipped("-", LocalStorageHelper.GetPageTitleFromDirectory(rootDir),
                        $"Дерево пропущено из-за ошибки: {ex.Message}");
                }
            });
    }

    /// <summary>
    /// Resolves the space a tree belongs to. The server value (when known) is
    /// the authority; an explicitly requested space that contradicts it throws
    /// (a deliberate user choice that can't be honoured — we fail loud rather
    /// than silently ignore it). A configured default never conflicts: it's
    /// only the fallback when the server space is unknown.
    /// </summary>
    private static string ResolveTreeSpace(string? serverSpace, string? explicitSpaceKey, string fallbackSpace, string conflictSubject)
    {
        if (string.IsNullOrEmpty(serverSpace))
            return fallbackSpace;

        if (!string.IsNullOrEmpty(explicitSpaceKey)
            && !string.Equals(explicitSpaceKey, serverSpace, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Запрошено пространство '{explicitSpaceKey}', но {conflictSubject} находится в пространстве '{serverSpace}'. "
                + "Уберите параметр spaceKey либо укажите верный — пространство определяется деревом на сервере.");
        }

        return serverSpace;
    }

    // ── update internals ──────────────────────────────────────────────

    /// <summary>
    /// True when a page's actual (server) space differs from the tree being
    /// operated on. A Confluence tree is single-space, so this flags a page that
    /// doesn't belong here — a manually-moved subfolder, a hand-edited marker,
    /// or a stale mapping. Such pages are refused (not moved/updated) and
    /// reported. Null/empty page space means "unknown" and never blocks.
    /// </summary>
    private static bool IsCrossSpace(string? pageSpace, string treeSpace) =>
        !string.IsNullOrEmpty(pageSpace) && !string.Equals(pageSpace, treeSpace, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> DetectRootPageMoveAsync(
        PageData rootPage, string sourceDir, string treeSpace, SyncReport report, CancellationToken ct)
    {
        var parentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(sourceDir));
        if (string.IsNullOrEmpty(parentDir))
            return null;

        var localParentPageId = PageMarker.Load(parentDir)?.PageId;
        if (localParentPageId == null)
            return null;

        if (string.Equals(rootPage.ParentId, localParentPageId, StringComparison.OrdinalIgnoreCase))
            return null;

        // Cross-space guard: refuse to move the root under a parent in another
        // space. Confluence won't relocate a page across spaces via an
        // ancestors change anyway; refuse early with a clear report entry
        // instead of surfacing a confusing server error.
        var parentPage = await _apiClient.TryGetPageByIdAsync(localParentPageId, ct);
        if (parentPage != null && IsCrossSpace(parentPage.SpaceKey, treeSpace))
        {
            _logger.LogWarning(
                "Cross-space move refused: root '{Title}' is in '{TreeSpace}', target parent {ParentId} is in '{ParentSpace}'",
                rootPage.Title, treeSpace, localParentPageId, parentPage.SpaceKey);
            report.AddSkipped(rootPage.Id, rootPage.Title,
                $"Перемещение между пространствами отклонено: страница в '{treeSpace}', целевой родитель в '{parentPage.SpaceKey}'. "
                + "Confluence не переносит страницы между пространствами сменой родителя — перемещение пропущено.");
            return null;
        }

        _logger.LogInformation(
            "Root page '{Title}' (ID: {PageId}) will be moved from parent {OldParent} to {NewParent}",
            rootPage.Title, rootPage.Id, rootPage.ParentId, localParentPageId);
        return localParentPageId;
    }

    private async Task<(string PageId, bool ResolvedByTitle)> ResolveRootPageForUpdate(
        string spaceKey, string sourceDir, string? explicitPageId, string? explicitPageTitle, CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(explicitPageId))
        {
            var page = await _apiClient.TryGetPageByIdAsync(explicitPageId, ct);
            if (page == null)
                throw new InvalidOperationException($"Page with ID '{explicitPageId}' not found in Confluence");
            return (page.Id, false);
        }

        if (!string.IsNullOrEmpty(explicitPageTitle))
        {
            var foundId = await _apiClient.FindPageByTitleAsync(spaceKey, null, explicitPageTitle, ct);
            if (foundId == null)
                throw new InvalidOperationException($"Page with title '{explicitPageTitle}' not found in space '{spaceKey}'");
            return (foundId, true);
        }

        var markerPageId = PageMarker.Load(sourceDir)?.PageId;
        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId, ct);
            if (page != null)
                return (page.Id, false);
            _logger.LogWarning("Page with ID '{PageId}' from .id marker not found, falling back to title search", markerPageId);
        }

        var folderName = LocalStorageHelper.GetPageTitle(sourceDir);
        var foundByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName, ct);
        if (foundByTitle != null)
            return (foundByTitle, true);

        throw new InvalidOperationException(
            $"Could not find a matching Confluence page for '{folderName}'. " +
            "Specify --page-id or --page-title, or use 'upload create' for new pages.");
    }

    // ── child resolution (shared by the update/merge walkers) ─────────

    /// <summary>
    /// Result of resolving a local child directory to a server page:
    /// <c>PageId</c> null ⇒ no matching page exists (create it);
    /// <c>MoveToParentId</c> non-null ⇒ the page exists under another parent
    /// and must be moved here; <c>Skip</c> ⇒ cross-space mismatch — the
    /// directory and its subtree are already reported and must not be touched.
    /// </summary>
    private sealed record ChildPageResolution(string? PageId, string? MoveToParentId, bool Skip);

    /// <summary>
    /// Shared resolution step of the update/merge walkers: map a child folder
    /// to its Confluence page via the .id marker (with cross-space guard and
    /// move detection), falling back to a title search under the parent, then
    /// space-wide (a page found elsewhere in the space is moved under the parent).
    /// </summary>
    private async Task<ChildPageResolution> ResolveChildPageAsync(
        string spaceKey, string childDir, string parentPageId, SyncReport report, CancellationToken ct)
    {
        var folderName = LocalStorageHelper.GetPageTitle(childDir);
        var markerPageId = PageMarker.Load(childDir)?.PageId;

        if (markerPageId != null)
        {
            var page = await _apiClient.TryGetPageByIdAsync(markerPageId, ct);
            if (page != null)
            {
                if (IsCrossSpace(page.SpaceKey, spaceKey))
                {
                    _logger.LogWarning(
                        "Cross-space: page '{Title}' (ID: {PageId}) is in '{PageSpace}', tree is '{TreeSpace}'; skipping it and its subtree",
                        page.Title, page.Id, page.SpaceKey, spaceKey);
                    report.AddSkipped(page.Id, page.Title,
                        $"Межпространственное расхождение: страница в '{page.SpaceKey}', а дерево привязано к '{spaceKey}'. "
                        + "Обновление/перемещение и обработка поддерева пропущены. Верните папку в правильное дерево или синхронизируйте заново.");
                    return new ChildPageResolution(null, null, Skip: true);
                }

                string? moveToParentId = null;
                if (!string.Equals(page.ParentId, parentPageId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Page '{Title}' (ID: {PageId}) will be moved from parent {OldParent} to {NewParent}",
                        page.Title, page.Id, page.ParentId, parentPageId);
                    moveToParentId = parentPageId;
                }
                return new ChildPageResolution(page.Id, moveToParentId, Skip: false);
            }
        }

        var foundUnderParent = await _apiClient.FindPageByTitleAsync(spaceKey, parentPageId, folderName, ct);
        if (foundUnderParent != null)
            return new ChildPageResolution(foundUnderParent, null, Skip: false);

        var foundGlobally = await _apiClient.FindPageByTitleAsync(spaceKey, null, folderName, ct);
        if (foundGlobally != null)
        {
            _logger.LogInformation(
                "Page '{Title}' (ID: {PageId}) found in space but under a different parent, will be moved to parent {NewParent}",
                folderName, foundGlobally, parentPageId);
            return new ChildPageResolution(foundGlobally, parentPageId, Skip: false);
        }

        return new ChildPageResolution(null, null, Skip: false);
    }

    private async Task ProcessChildForUpdate(string spaceKey, string childDir, string parentPageId, SyncReport report, CancellationToken ct)
    {
        var resolution = await ResolveChildPageAsync(spaceKey, childDir, parentPageId, report, ct);
        if (resolution.Skip)
            return;

        if (resolution.PageId == null)
        {
            await ProcessChildForCreate(spaceKey, childDir, parentPageId, ct);
            return;
        }

        var (updateResult, effectiveTitle) = await UpdatePageContentAndAttachments(spaceKey, resolution.PageId, childDir, report, resolution.MoveToParentId, ct);
        if (updateResult != null)
            await UpdatePageIdMarker(childDir, updateResult.Id, updateResult.VersionNumber, effectiveTitle, spaceKey, ct);

        await ProcessChildrenAsync(childDir, ct,
            grandchildDir => ProcessChildForUpdate(spaceKey, grandchildDir, resolution.PageId, report, ct));
    }

    // ── merge internals ───────────────────────────────────────────────

    private async Task MergeUploadPageAsync(
        string spaceKey, string pageId, string pageDir,
        string? moveToParentId,
        ChangeSourceAnalyzer analyzer, SyncReport report, CancellationToken ct)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var localContent = await LocalStorageHelper.ReadPageContent(pageDir, ct);

        if (_dryRun)
        {
            if (moveToParentId != null)
                _logger.LogInformation(
                    "DRY RUN: Would merge-upload page {PageId} '{Title}' and move to parent {NewParent}",
                    pageId, title, moveToParentId);
            else
                _logger.LogInformation("DRY RUN: Would merge-upload page {PageId} with title '{Title}'", pageId, title);
            LogDryRunAttachments(pageDir);
            return;
        }

        var serverPage = await _apiClient.GetPageByIdAsync(pageId, ct);

        // Single read of the local sync state: the marker (original title,
        // version, sync time) plus the hash-based "did local content change
        // since the last sync?" verdict — the latter is independent of
        // Confluence's server-side canonicalisation (assigned ac:macro-id,
        // dropped empty parameters, …). When it proves the local copy is
        // unchanged, treat content as unchanged even if ContentEquals still
        // sees a server-side diff, so we don't re-upload and spam server versions.
        var syncState = LocalSyncState.Read(pageDir, localContent, _hasher);

        if (syncState.Marker?.Title == null
            && string.Equals(LocalStorageHelper.SanitizeFileName(serverPage.Title), title, StringComparison.OrdinalIgnoreCase))
        {
            title = serverPage.Title;
        }

        bool contentChanged = !_normalizer.ContentEquals(localContent, serverPage.Body.Storage.Value)
            && syncState.LocalContentChanged != false;
        bool titleChanged = !string.Equals(title, serverPage.Title, StringComparison.Ordinal);
        bool parentChanged = moveToParentId != null;

        if (!contentChanged && !titleChanged && !parentChanged)
        {
            _logger.LogDebug("Page {PageId} '{Title}' is unchanged, skipping merge-upload", pageId, title);
            await UpdatePageIdMarker(pageDir, pageId, serverPage.Version?.Number, title, spaceKey, ct);
            return;
        }

        // Структурное перемещение без изменения контента/заголовка. Применяем
        // безусловно: пользователь явно перенёс папку, а локальные правки
        // контента отсутствуют. Чтобы не порождать «шумовую» правку контента
        // на сервере, отправляем серверные значения title/body — Confluence
        // создаст новую версию только с изменением ancestors.
        if (parentChanged && !contentChanged && !titleChanged)
        {
            _logger.LogInformation(
                "Page '{Title}' was moved locally (parent: {OldParent} -> {NewParent}); applying move on the server",
                title, serverPage.ParentId, moveToParentId);
            try
            {
                var moveResult = await _apiClient.UpdatePageAsync(pageId, serverPage.Title, serverPage.Body.Storage.Value, moveToParentId, serverPage.Version?.Number, ct);
                await UpdatePageIdMarker(pageDir, moveResult.Id, moveResult.VersionNumber, serverPage.Title, spaceKey, ct);
                await UploadPageAttachments(pageId, pageDir, ct);
            }
            catch (ConfluenceApiException ex)
            {
                if (!TryRecordWriteFailure(ex, pageId, title, report)) throw;
            }
            return;
        }

        var sourceInfo = analyzer.AnalyzeContentChange(
            serverPage.Version?.When?.ToUniversalTime(), syncState.LocalFileTimeUtc,
            syncState.MarkerVersion, serverPage.Version?.Number, syncState.SyncTimeUtc, syncState.LocalContentChanged);

        switch (sourceInfo.Origin)
        {
            case ChangeOrigin.Local:
                _logger.LogInformation(
                    parentChanged
                        ? "Page '{Title}' changed locally and was moved, uploading to server with new parent {NewParent}"
                        : "Page '{Title}' changed locally, uploading to server",
                    title, moveToParentId);
                try
                {
                    var result = await _apiClient.UpdatePageAsync(pageId, title, localContent, moveToParentId, serverPage.Version?.Number, ct);
                    await UpdatePageIdMarker(pageDir, result.Id, result.VersionNumber, title, spaceKey, ct);
                    await UploadPageAttachments(pageId, pageDir, ct);
                }
                catch (ConfluenceApiException ex)
                {
                    if (!TryRecordWriteFailure(ex, pageId, title, report)) throw;
                }
                break;

            case ChangeOrigin.Server:
                // Контент/заголовок на сервере новее — поверх него ничего не пишем.
                // Структурное перемещение, если оно есть, тоже откладываем: иначе
                // пришлось бы либо потерять серверные правки, либо перетащить их
                // в локальную копию (что выходит за рамки upload-операции).
                // Пользователю выводим явную подсказку.
                if (parentChanged)
                {
                    var reason = sourceInfo.Reason
                        + "; перемещение отложено до синхронизации контента — выполните 'download merge', при необходимости заново переместите папку и повторите 'upload merge'";
                    _logger.LogInformation(
                        "Page '{Title}' changed on server and was moved locally; skipping upload and deferring move",
                        title);
                    report.AddSkipped(pageId, title, reason);
                }
                else
                {
                    _logger.LogInformation("Page '{Title}' changed on server, skipping upload", title);
                    report.AddSkipped(pageId, title, sourceInfo.Reason);
                }
                break;

            case ChangeOrigin.Conflict:
                var conflictReason = parentChanged
                    ? sourceInfo.Reason + "; перемещение страницы также отложено до разрешения конфликта"
                    : sourceInfo.Reason;
                _logger.LogWarning("CONFLICT: Page '{Title}' changed both locally and on server", title);
                report.AddConflict(pageId, title, conflictReason);
                break;

            default:
                _logger.LogWarning("Page '{Title}' change source unknown, skipping upload", title);
                report.AddSkipped(pageId, title, sourceInfo.Reason);
                break;
        }
    }

    private async Task ProcessChildForMerge(
        string spaceKey, string childDir, string parentPageId,
        ChangeSourceAnalyzer analyzer, SyncReport report, CancellationToken ct)
    {
        var resolution = await ResolveChildPageAsync(spaceKey, childDir, parentPageId, report, ct);
        if (resolution.Skip)
            return;

        if (resolution.PageId == null)
        {
            await ProcessChildForCreate(spaceKey, childDir, parentPageId, ct);
            return;
        }

        await MergeUploadPageAsync(spaceKey, resolution.PageId, childDir, resolution.MoveToParentId, analyzer, report, ct);

        await ProcessChildrenAsync(childDir, ct,
            grandchildDir => ProcessChildForMerge(spaceKey, grandchildDir, resolution.PageId, analyzer, report, ct));
    }

    // ── create internals ──────────────────────────────────────────────

    private async Task ProcessChildForCreate(string spaceKey, string childDir, string? parentPageId, CancellationToken ct)
    {
        var (createResult, effectiveTitle) = await CreatePageFromDirectory(spaceKey, childDir, parentPageId, ct);
        if (createResult == null) return;
        await UpdatePageIdMarker(childDir, createResult.Id, createResult.VersionNumber, effectiveTitle, spaceKey, ct);

        await ProcessChildrenAsync(childDir, ct,
            grandchildDir => ProcessChildForCreate(spaceKey, grandchildDir, createResult.Id, ct));
    }

    private async Task<(PageUpdateResult? Result, string? Title)> CreatePageFromDirectory(string spaceKey, string pageDir, string? parentId, CancellationToken ct)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var content = await LocalStorageHelper.ReadPageContent(pageDir, ct);

        var existingId = await _apiClient.FindPageByTitleAsync(spaceKey, null, title, ct);
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

        PageUpdateResult result;
        try
        {
            result = await _apiClient.CreatePageAsync(spaceKey, parentId, title, content, ct);
        }
        catch (ConfluenceApiException ex) when (!ex.IsAuthFailure)
        {
            // Non-auth create failures stay non-fatal (logged, page skipped),
            // matching the pre-exception behaviour; auth failures propagate to
            // abort the run.
            _logger.LogError(ex, "Failed to create page '{Title}'", title);
            return (null, null);
        }

        _logger.LogInformation("Created page '{Title}' with ID {PageId}", title, result.Id);
        await UploadPageAttachments(result.Id, pageDir, ct);
        return (result, title);
    }

    // ── shared: page content update ───────────────────────────────────

    private async Task<(PageUpdateResult? Result, string? Title)> UpdatePageContentAndAttachments(
        string spaceKey, string pageId, string pageDir, SyncReport report, string? moveToParentId = null, CancellationToken ct = default)
    {
        var title = LocalStorageHelper.GetPageTitle(pageDir);
        var localContent = await LocalStorageHelper.ReadPageContent(pageDir, ct);

        if (_dryRun)
        {
            if (moveToParentId != null)
                _logger.LogInformation("DRY RUN: Would move page {PageId} to parent {NewParent} and update with title '{Title}'", pageId, moveToParentId, title);
            else
                _logger.LogInformation("DRY RUN: Would update page {PageId} with title '{Title}'", pageId, title);

            var existingByTitle = await _apiClient.FindPageByTitleAsync(spaceKey, null, title, ct);
            if (existingByTitle != null && existingByTitle != pageId)
                _logger.LogWarning("DRY RUN: Renaming page {PageId} to '{Title}' would conflict with existing page {ConflictId}",
                    pageId, title, existingByTitle);

            LogDryRunAttachments(pageDir);
            return (null, null);
        }

        var serverPage = await _apiClient.GetPageByIdAsync(pageId, ct);

        // Hash-based "did local content change since the last sync?" lets a force
        // update skip the push when the only diff is Confluence's server-side
        // canonicalisation, preventing pointless version churn.
        var syncState = LocalSyncState.Read(pageDir, localContent, _hasher);

        if (syncState.Marker?.Title == null
            && string.Equals(LocalStorageHelper.SanitizeFileName(serverPage.Title), title, StringComparison.OrdinalIgnoreCase))
        {
            title = serverPage.Title;
        }

        var serverVersion = serverPage.Version?.Number;

        bool titleChanged = !string.Equals(title, serverPage.Title, StringComparison.Ordinal);
        bool contentChanged = !_normalizer.ContentEquals(localContent, serverPage.Body.Storage.Value)
            && syncState.LocalContentChanged != false;
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

        PageUpdateResult result;
        try
        {
            result = await _apiClient.UpdatePageAsync(pageId, title, localContent, moveToParentId, serverVersion, ct);
        }
        catch (ConfluenceApiException ex)
        {
            if (!TryRecordWriteFailure(ex, pageId, title, report)) throw;
            return (null, null);
        }

        if (moveToParentId != null)
            _logger.LogInformation("Moved and updated page {PageId} with title '{Title}' to parent {NewParent}", pageId, title, moveToParentId);
        else
            _logger.LogInformation("Updated page {PageId} with title '{Title}'", pageId, title);
        await UploadPageAttachments(pageId, pageDir, ct);
        return (result, title);
    }

    // ── shared: attachments ───────────────────────────────────────────

    private async Task UploadPageAttachments(string pageId, string pageDir, CancellationToken ct)
    {
        var files = LocalStorageHelper.GetAttachmentFiles(pageDir).ToList();
        if (files.Count == 0) return;

        var existingAttachments = await _apiClient.GetAttachmentsAsync(pageId, ct);

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
            async (file, _) =>
            {
                var fileName = Path.GetFileName(file);
                var existing = existingAttachments.FirstOrDefault(
                    a => a.Title.Equals(fileName, StringComparison.OrdinalIgnoreCase));

                if (existing != null)
                {
                    bool changed = await IsAttachmentChangedAsync(file, existing, ct);
                    if (!changed)
                    {
                        _logger.LogDebug("Attachment '{FileName}' on page {PageId} is unchanged, skipping", fileName, pageId);
                        return;
                    }

                    // Preserve the attachment's stored media type so Confluence
                    // does not re-infer it from the (possibly absent) filename
                    // extension and drop the update — see UpdateAttachmentDataAsync.
                    var updated = await _apiClient.UpdateAttachmentDataAsync(pageId, existing.Id, file, fileName, existing.EffectiveMediaType, ct);
                    if (updated)
                        _logger.LogInformation("Updated attachment '{FileName}' (new version) on page {PageId}", fileName, pageId);
                }
                else
                {
                    var uploaded = await _apiClient.UploadAttachmentAsync(pageId, file, fileName, ct);
                    if (uploaded)
                        _logger.LogInformation("Uploaded new attachment '{FileName}' to page {PageId}", fileName, pageId);
                }
            });
    }

    private async Task<bool> IsAttachmentChangedAsync(string localFilePath, AttachmentData serverAttachment, CancellationToken ct)
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

        var remoteContent = await _apiClient.DownloadAttachmentAsync(serverAttachment.Links.DownloadUrl, ct);

        var started = Stopwatch.GetTimestamp();
        var localHash = await ComputeFileHashAsync(localFilePath, ct);
        var remoteHash = ComputeHash(remoteContent);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        _logger.LogDebug(
            "[PROFILE] SHA256 compared ({Bytes} bytes) in {ElapsedMs}ms: {FileName}",
            remoteContent.Length, elapsedMs, serverAttachment.Title);

        bool differs = !localHash.SequenceEqual(remoteHash);
        if (differs)
            _logger.LogDebug("Attachment '{FileName}' content hash differs", serverAttachment.Title);

        return differs;
    }

    private static async Task<byte[]> ComputeFileHashAsync(string filePath, CancellationToken ct)
    {
        await using var stream = File.OpenRead(filePath);
        return await SHA256.HashDataAsync(stream, ct);
    }

    private static byte[] ComputeHash(byte[] data) => SHA256.HashData(data);

    // ── shared: utilities ─────────────────────────────────────────────

    /// <summary>
    /// Runs <paramref name="processChild"/> over every child folder of a page
    /// directory, bounded by the configured max parallelism.
    /// </summary>
    private Task ProcessChildrenAsync(string pageDir, CancellationToken ct, Func<string, Task> processChild) =>
        Parallel.ForEachAsync(
            LocalStorageHelper.GetPageSubdirectories(pageDir),
            new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
            async (childDir, _) => await processChild(childDir));

    /// <summary>
    /// Translates a write-path <see cref="ConfluenceApiException"/> into a
    /// per-page report entry so one rejected page doesn't abort the whole
    /// (often recursive, parallel) upload. Returns <c>false</c> for auth
    /// failures (401/403), signalling the caller to rethrow — bad/insufficient
    /// credentials are global and the run should stop rather than keep trying.
    /// </summary>
    private bool TryRecordWriteFailure(ConfluenceApiException ex, string pageId, string title, SyncReport report)
    {
        if (ex.IsAuthFailure)
            return false;

        if (ex is ConfluenceConflictException)
        {
            _logger.LogWarning(
                "CONFLICT: page '{Title}' (ID: {PageId}) was rejected by the server with 409 during upload", title, pageId);
            report.AddConflict(pageId, title,
                $"Версионный конфликт при загрузке на сервер: {ex.Message}. "
                + "Выполните 'download merge' для синхронизации серверных правок, при необходимости повторите перемещение и затем 'upload merge'.");
        }
        else
        {
            _logger.LogError(ex, "Failed to upload page '{Title}' (ID: {PageId})", title, pageId);
            report.AddSkipped(pageId, title, $"Не удалось загрузить страницу на сервер: {ex.Message}");
        }

        return true;
    }

    private void LogDryRunAttachments(string pageDir)
    {
        foreach (var file in LocalStorageHelper.GetAttachmentFiles(pageDir))
            _logger.LogInformation("DRY RUN: Would upload attachment '{FileName}'", Path.GetFileName(file));
    }

    private async Task UpdatePageIdMarker(string pageDir, string pageId, int? version, string? originalTitle = null, string? spaceKey = null, CancellationToken ct = default)
    {
        if (_dryRun) return;

        // After a successful upload the local index.html IS the synced baseline
        // (upload never mutates local files) — hash it to stamp the marker.
        var syncedContent = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir, ct);
        var written = await PageMarker.UpdateAsync(pageDir, pageId, version, originalTitle, spaceKey, syncedContent, _hasher, ct);
        if (written)
            _logger.LogInformation("Saved page ID marker '.id{PageId}_{Version}' in '{PageDir}'", pageId, version, pageDir);
    }
}
