using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;

namespace ConfluencePageExporter.Services;

public class DownloadService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly IContentNormalizer _normalizer;
    private readonly IContentHasher _hasher;
    private readonly ILogger<DownloadService> _logger;
    private readonly bool _dryRun;
    private readonly int _maxParallelism;
    private readonly Lock _fsLock = new();

    public DownloadService(
        IConfluenceApiClient apiClient,
        IContentNormalizer normalizer,
        ILogger<DownloadService> logger,
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

    public async Task<SyncReport> DownloadUpdateAsync(
        string spaceKey, string? pageId, string? pageTitle,
        string outputDir, bool recursive, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();
        var resolvedPageId = await ResolvePageId(spaceKey, pageId, pageTitle, ct);

        var pageDirectoryIndex = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
        var page = await _apiClient.GetPageByIdAsync(resolvedPageId, ct);
        // A Confluence tree lives in a single space; resolve it once from the
        // root's server value and flow it down so every marker is stamped with
        // it (children inherit — no per-child space fetch needed).
        var treeSpace = page.SpaceKey ?? spaceKey;
        await DownloadPageUpdateAsync(page, outputDir, recursive, pageDirectoryIndex, report, treeSpace, ct);
        _logger.LogDebug(
            "[PROFILE] DownloadUpdate completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    public async Task<SyncReport> DownloadMergeAsync(
        string spaceKey, string? pageId, string? pageTitle,
        string outputDir, bool recursive, ChangeSourceAnalyzer analyzer, CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        var report = new SyncReport();
        var resolvedPageId = await ResolvePageId(spaceKey, pageId, pageTitle, ct);

        var pageDirectoryIndex = LocalStorageHelper.BuildPageDirectoryIndex(outputDir, _logger);
        var page = await _apiClient.GetPageByIdAsync(resolvedPageId, ct);
        var treeSpace = page.SpaceKey ?? spaceKey;
        await DownloadPageMergeAsync(page, outputDir, recursive, pageDirectoryIndex, analyzer, report, treeSpace, ct);
        _logger.LogDebug(
            "[PROFILE] DownloadMerge completed in {ElapsedMs}ms",
            (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return report;
    }

    private async Task<string> ResolvePageId(string spaceKey, string? pageId, string? pageTitle, CancellationToken ct)
    {
        var resolved = await _apiClient.ResolvePageIdAsync(spaceKey, pageId, pageTitle, ct);
        return resolved ?? throw new InvalidOperationException(
            $"Could not resolve page. ID: '{pageId}', Title: '{pageTitle}'");
    }

    private async Task DownloadPageUpdateAsync(
        PageData page, string parentDir, bool recursive,
        Dictionary<string, string> pageDirectoryIndex, SyncReport report, string treeSpaceKey, CancellationToken ct)
    {
        var pageDir = ResolvePageDirectoryForDownload(page, parentDir, pageDirectoryIndex);

        if (!_dryRun)
            Directory.CreateDirectory(pageDir);

        await SavePageContentForUpdate(page, pageDir, ct);

        // Sync attachments before stamping the marker so their baseline (server
        // name/version/hash) is recorded in the same marker write.
        IReadOnlyDictionary<string, AttachmentBaseline>? attachmentBaselines = null;
        if (page.ChildTypes?.HasAttachments ?? true)
        {
            var attachments = await _apiClient.GetAttachmentsAsync(page.Id, ct);
            attachmentBaselines = await SaveAttachments(attachments, pageDir, page.Id, page.Title, mergeMode: false, report, ct);
        }

        await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title, treeSpaceKey, page.Body.Storage.Value, attachmentBaselines, ct);

        if (recursive && (page.ChildTypes?.HasPages ?? true))
        {
            var children = await _apiClient.GetChildrenPagesAsync(page.Id, ct);
            await Parallel.ForEachAsync(
                children,
                new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
                async (child, _) => await DownloadPageUpdateAsync(child, pageDir, recursive, pageDirectoryIndex, report, treeSpaceKey, ct));
        }
    }

    private async Task DownloadPageMergeAsync(
        PageData page, string parentDir, bool recursive,
        Dictionary<string, string> pageDirectoryIndex,
        ChangeSourceAnalyzer analyzer, SyncReport report, string treeSpaceKey, CancellationToken ct)
    {
        var pageDir = ResolvePageDirectoryForDownload(page, parentDir, pageDirectoryIndex);

        if (!_dryRun)
            Directory.CreateDirectory(pageDir);

        var serverContent = page.Body.Storage.Value;
        var localContent = await LocalStorageHelper.ReadLocalPageContentOrNull(pageDir, ct);

        // Attachments are mirrored regardless of the page-content decision
        // (download direction; per-attachment source detection is a later phase).
        // Sync them first so their baseline can be stamped into the marker writes
        // below. Branches that don't write the marker (local-newer / conflict)
        // leave the baseline for the next successful sync.
        IReadOnlyDictionary<string, AttachmentBaseline>? attachmentBaselines = null;
        if (page.ChildTypes?.HasAttachments ?? true)
        {
            var attachments = await _apiClient.GetAttachmentsAsync(page.Id, ct);
            attachmentBaselines = await SaveAttachments(attachments, pageDir, page.Id, page.Title, mergeMode: true, report, ct);
        }

        if (localContent == null)
        {
            await WritePageContent(page.Title, pageDir, serverContent, ct);
            await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title, treeSpaceKey, page.Body.Storage.Value, attachmentBaselines, ct);
        }
        else if (_normalizer.ContentEquals(localContent, serverContent))
        {
            _logger.LogDebug("Page '{Title}' content is unchanged, skipping", page.Title);
            await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title, treeSpaceKey, page.Body.Storage.Value, attachmentBaselines, ct);
        }
        else
        {
            var syncState = LocalSyncState.Read(pageDir, localContent, _hasher);

            var sourceInfo = analyzer.AnalyzeContentChange(
                page.Version?.When?.ToUniversalTime(), syncState.LocalFileTimeUtc,
                syncState.MarkerVersion, page.Version?.Number, syncState.SyncTimeUtc, syncState.LocalContentChanged);

            switch (sourceInfo.Origin)
            {
                case ChangeOrigin.Server:
                    _logger.LogInformation("Page '{Title}' changed on server, downloading", page.Title);
                    await WritePageContent(page.Title, pageDir, serverContent, ct);
                    await SavePageIdMarker(page.Id, page.Version?.Number, pageDir, page.Title, treeSpaceKey, page.Body.Storage.Value, attachmentBaselines, ct);
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

        if (recursive && (page.ChildTypes?.HasPages ?? true))
        {
            var children = await _apiClient.GetChildrenPagesAsync(page.Id, ct);
            await Parallel.ForEachAsync(
                children,
                new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
                async (child, _) => await DownloadPageMergeAsync(child, pageDir, recursive, pageDirectoryIndex, analyzer, report, treeSpaceKey, ct));
        }
    }

    private async Task SavePageContentForUpdate(PageData page, string pageDir, CancellationToken ct)
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
            var existingContent = await File.ReadAllTextAsync(filePath, ct);
            if (_normalizer.ContentEquals(existingContent, content))
            {
                _logger.LogDebug("Page '{Title}' content is unchanged, skipping rewrite", page.Title);
                return;
            }
        }

        await File.WriteAllTextAsync(filePath, content, ct);
        _logger.LogInformation("Saved page '{Title}' -> {File}", page.Title, filePath);
    }

    private async Task WritePageContent(string title, string pageDir, string content, CancellationToken ct)
    {
        var filePath = Path.Combine(pageDir, "index.html");

        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would save page '{Title}' -> {File}", title, filePath);
            return;
        }

        await File.WriteAllTextAsync(filePath, content, ct);
        _logger.LogInformation("Saved page '{Title}' -> {File}", title, filePath);
    }

    private string ResolvePageDirectoryForDownload(
        PageData page, string parentDir,
        Dictionary<string, string> pageDirectoryIndex)
    {
        // Критическая секция: под параллельным обходом дерева несколько сиблингов
        // могут одновременно запрашивать expectedDir, делать Directory.Move/Delete
        // и мутировать общий индекс. Сетевой I/O здесь не происходит, операция
        // быстрая — сериализация безопасна и не влияет на производительность.
        lock (_fsLock)
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
                    var markerAtExpected = PageMarker.Load(expectedDir)?.PageId;
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
    }

    private async Task SavePageIdMarker(string pageId, int? version, string pageDir, string? originalTitle = null, string? spaceKey = null, string? syncedContent = null, IReadOnlyDictionary<string, AttachmentBaseline>? attachments = null, CancellationToken ct = default)
    {
        if (_dryRun)
        {
            _logger.LogInformation("DRY RUN: Would create ID marker: .id{PageId}_{Version}", pageId, version);
            return;
        }

        // syncedContent is the body that is now the synced baseline (server
        // content, or the local content when it already matched); attachments is
        // the post-sync per-attachment baseline (or null to preserve existing).
        // The shared skip/upgrade policy lives in PageMarker.UpdateAsync.
        await PageMarker.UpdateAsync(pageDir, pageId, version, originalTitle, spaceKey, syncedContent, _hasher, attachments, ct);
    }

    /// <summary>
    /// Mirrors server attachments to the local directory and returns the post-sync
    /// baseline map for stamping into the marker. With a baseline,
    /// <see cref="AttachmentChangeAnalyzer"/> decides the source: in
    /// <paramref name="mergeMode"/> a locally-changed attachment (or a two-sided
    /// conflict) is left untouched and reported instead of being overwritten by
    /// the server copy; in force mode the server always wins (a local change is
    /// overwritten with a warning). Without a baseline it falls back to the cheap
    /// size/content check.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, AttachmentBaseline>> SaveAttachments(
        List<AttachmentData> attachments, string pageDir, string pageId, string pageTitle, bool mergeMode, SyncReport report, CancellationToken ct)
    {
        var priorBaselines = PageMarker.Load(pageDir)?.Attachments;
        var baselines = new ConcurrentDictionary<string, AttachmentBaseline>(StringComparer.OrdinalIgnoreCase);

        await Parallel.ForEachAsync(
            attachments,
            new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
            async (att, _) =>
            {
                var localName = LocalStorageHelper.SanitizeFileName(att.Title);
                var filePath = Path.Combine(pageDir, localName);
                var prior = priorBaselines != null && priorBaselines.TryGetValue(localName, out var p) ? p : null;

                try
                {
                    if (_dryRun)
                    {
                        _logger.LogInformation("DRY RUN: Would download attachment '{Title}' -> {Path}", att.Title, filePath);
                        return;
                    }

                    // A missing local file is treated as "server-side" (just pull it).
                    var verdict = File.Exists(filePath)
                        ? AttachmentChangeAnalyzer.Analyze(prior, att.Version?.Number, await LocalStorageHelper.HasAttachmentChangedLocallyAsync(filePath, prior, ct))
                        : AttachmentChangeOrigin.Server;

                    // merge: protect a locally-changed attachment from the server copy.
                    if (mergeMode && verdict is AttachmentChangeOrigin.Local or AttachmentChangeOrigin.Conflict)
                    {
                        if (verdict == AttachmentChangeOrigin.Local)
                        {
                            _logger.LogInformation("Attachment '{Title}' is newer locally; skipping download", att.Title);
                            report.AddSkipped(pageId, $"{pageTitle} → {att.Title}",
                                "вложение новее локально — скачивание пропущено; выполните 'upload merge'");
                        }
                        else
                        {
                            _logger.LogWarning("CONFLICT: attachment '{Title}' changed on both sides; skipping download", att.Title);
                            report.AddConflict(pageId, $"{pageTitle} → {att.Title}",
                                "вложение изменено и локально, и на сервере — скачивание пропущено; разрешите вручную ('upload merge' или повторная правка)");
                        }
                        if (prior != null)
                            baselines[localName] = prior; // keep prior baseline so the change is re-detected
                        return;
                    }

                    if (!mergeMode && verdict is AttachmentChangeOrigin.Local or AttachmentChangeOrigin.Conflict)
                        _logger.LogWarning("force: overwriting a local change of attachment '{Title}'", att.Title);

                    if (verdict == AttachmentChangeOrigin.Unchanged)
                    {
                        _logger.LogDebug("Attachment '{Title}' is unchanged, skipping download", att.Title);
                    }
                    else if (verdict == AttachmentChangeOrigin.Unknown && IsLocalFileSizeMatch(filePath, att))
                    {
                        // No baseline and same size — keep the cheap legacy skip.
                        _logger.LogDebug("Attachment '{Title}' is up to date (size: {Size}), skipping download", att.Title, att.Extensions!.FileSize);
                    }
                    else
                    {
                        // Server is authoritative here (server-changed, force-overwrite,
                        // missing locally, or unknown-with-differing-size): download and
                        // write unless the bytes already match (avoids a needless rewrite).
                        var fileContent = await _apiClient.DownloadAttachmentAsync(att.Links.DownloadUrl, ct);
                        if (await IsLocalContentMatchAsync(filePath, fileContent, ct))
                        {
                            _logger.LogDebug(
                                "Attachment '{Title}' content already matches the server, skipping rewrite", att.Title);
                        }
                        else
                        {
                            await File.WriteAllBytesAsync(filePath, fileContent, ct);
                            _logger.LogInformation("Downloaded attachment '{Title}' -> {Path}", att.Title, filePath);
                        }
                    }

                    var baseline = await LocalStorageHelper.BuildAttachmentBaselineAsync(
                        filePath, att.Title, att.Version?.Number, priorBaselines, ct);
                    if (baseline != null)
                        baselines[localName] = baseline;
                }
                catch (OperationCanceledException)
                {
                    // Cancellation is not a per-attachment failure — let it
                    // abort the whole download instead of being logged and
                    // swallowed as an error below.
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to download attachment: {Title}", att.Title);
                }
            });

        return baselines;
    }

    private static bool IsLocalFileSizeMatch(string filePath, AttachmentData serverAttachment)
    {
        if (!File.Exists(filePath))
            return false;

        if (serverAttachment.Extensions?.FileSize is not long remoteSize)
            return false;

        return new FileInfo(filePath).Length == remoteSize;
    }

    private async Task<bool> IsLocalContentMatchAsync(string filePath, byte[] downloadedContent, CancellationToken ct)
    {
        if (!File.Exists(filePath))
            return false;

        var localFileInfo = new FileInfo(filePath);
        if (localFileInfo.Length != downloadedContent.Length)
            return false;

        var started = Stopwatch.GetTimestamp();
        var downloadedHash = SHA256.HashData(downloadedContent);
        await using var stream = File.OpenRead(filePath);
        var localHash = await SHA256.HashDataAsync(stream, ct);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        _logger.LogDebug(
            "[PROFILE] SHA256 compared ({Bytes} bytes) in {ElapsedMs}ms: {Path}",
            downloadedContent.Length, elapsedMs, filePath);

        return localHash.AsSpan().SequenceEqual(downloadedHash);
    }
}
