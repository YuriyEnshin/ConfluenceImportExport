using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;

namespace ConfluencePageExporter.Services;

public class CompareService
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ChangeSourceAnalyzer _changeSourceAnalyzer;
    private readonly IContentNormalizer _normalizer;
    private readonly IContentHasher _hasher;
    private readonly ILogger<CompareService> _logger;
    private readonly int _maxParallelism;

    public CompareService(
        IConfluenceApiClient apiClient,
        ChangeSourceAnalyzer changeSourceAnalyzer,
        IContentNormalizer normalizer,
        ILogger<CompareService> logger,
        int maxParallelism = GlobalOptions.DefaultMaxParallelism,
        IContentHasher? hasher = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _changeSourceAnalyzer = changeSourceAnalyzer ?? throw new ArgumentNullException(nameof(changeSourceAnalyzer));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        // Default the hasher from the same normalizer so existing constructors keep working.
        _hasher = hasher ?? new ContentHasher(_normalizer);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxParallelism = maxParallelism < 1 ? 1 : maxParallelism;
    }

    public async Task<CompareReport> CompareAsync(
        string spaceKey,
        string? pageId,
        string? pageTitle,
        string outputDir,
        bool recursive,
        bool matchByTitleWhenNoId = false,
        bool detectSource = false,
        CancellationToken ct = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return await CompareInternalAsync(spaceKey, pageId, pageTitle, outputDir, recursive, matchByTitleWhenNoId, detectSource, ct);
        }
        finally
        {
            _logger.LogDebug(
                "[PROFILE] Compare completed in {ElapsedMs}ms",
                (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    private async Task<CompareReport> CompareInternalAsync(
        string spaceKey,
        string? pageId,
        string? pageTitle,
        string outputDir,
        bool recursive,
        bool matchByTitleWhenNoId,
        bool detectSource,
        CancellationToken ct)
    {
        var report = new CompareReport { DetectSourceEnabled = detectSource };
        var resolvedRootPageId = await _apiClient.ResolvePageIdAsync(spaceKey, pageId, pageTitle, ct);
        if (resolvedRootPageId == null)
            throw new InvalidOperationException("Could not resolve root page for comparison.");

        var rootPage = await _apiClient.GetPageByIdAsync(resolvedRootPageId, ct);
        var rootRelativePath = LocalStorageHelper.NormalizeRelativePath(LocalStorageHelper.SanitizeFileName(rootPage.Title));

        var remotePages = new ConcurrentDictionary<string, RemotePageSnapshot>(StringComparer.OrdinalIgnoreCase);
        await CollectRemotePagesAsync(rootPage, rootRelativePath, recursive, remotePages, ct);

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
                var renamedOrMoved = new CompareRenamedOrMovedPageInfo
                {
                    PageId = remote.PageId,
                    Title = remote.Title,
                    LocalPath = localById.RelativePath,
                    ConfluencePath = remote.RelativePath
                };

                var localName = LocalStorageHelper.GetLastPathSegment(localById.RelativePath);
                var remoteName = LocalStorageHelper.GetLastPathSegment(remote.RelativePath);
                bool isRenamed = !string.Equals(localName, remoteName, StringComparison.OrdinalIgnoreCase);

                var localParent = LocalStorageHelper.GetParentPath(localById.RelativePath);
                var remoteParent = LocalStorageHelper.GetParentPath(remote.RelativePath);
                bool isMoved = !string.Equals(localParent, remoteParent, StringComparison.OrdinalIgnoreCase);

                if (isRenamed)
                {
                    renamedOrMoved.RenameSource = await _changeSourceAnalyzer.AnalyzeRenameAsync(
                        remote.PageId, remote.Title, localName,
                        remote.LastModifiedUtc, localById.DirectoryLastModifiedUtc,
                        detectSource, ct);
                }

                if (isMoved)
                {
                    renamedOrMoved.MoveSource = await _changeSourceAnalyzer.AnalyzeMoveAsync(
                        remote.PageId, remoteParent, localParent,
                        remote.LastModifiedUtc, localById.DirectoryLastModifiedUtc,
                        detectSource, ct);
                }

                report.RenamedOrMovedInConfluence.Add(renamedOrMoved);
            }

            var localDir = localById?.DirectoryPath ?? localByTitlePath!.DirectoryPath;
            var localContent = await LocalStorageHelper.ReadLocalPageContentOrNull(localDir, ct);

            if (!_normalizer.ContentEquals(localContent, remote.Content))
            {
                var syncState = LocalSyncState.Read(localDir, localContent, _hasher);

                var changeSource = _changeSourceAnalyzer.AnalyzeContentChange(
                    remote.LastModifiedUtc, syncState.LocalFileTimeUtc,
                    syncState.MarkerVersion, remote.VersionNumber, syncState.SyncTimeUtc, syncState.LocalContentChanged);

                if (changeSource.Origin == ChangeOrigin.Conflict)
                {
                    report.Conflicts.Add(new CompareContentChangedPageInfo
                    {
                        PageId = remote.PageId,
                        Title = remote.Title,
                        Path = remote.RelativePath,
                        ChangeSource = changeSource
                    });
                }
                else
                {
                    report.ContentChanged.Add(new CompareContentChangedPageInfo
                    {
                        PageId = remote.PageId,
                        Title = remote.Title,
                        Path = remote.RelativePath,
                        ChangeSource = changeSource
                    });
                }
            }

            await CompareAttachmentsAsync(remote, localDir, report, ct);
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

        await DetectRootPageParentMismatchAsync(report, rootPage, localSnapshot, rootRelativePath, detectSource, ct);

        return report;
    }

    /// <summary>
    /// Attachment comparison for one matched page, download-free: presence by
    /// (sanitised) name, and for attachments present on both sides the change
    /// source via <see cref="AttachmentChangeAnalyzer"/> (server version vs the
    /// marker baseline, local raw-bytes hash vs the baseline) → changed-locally /
    /// changed-on-server / conflict. Without a baseline it falls back to a byte-size
    /// comparison (a same-size edit then stays undetected). The server list is
    /// fetched only when there is something to compare.
    /// </summary>
    private async Task CompareAttachmentsAsync(
        RemotePageSnapshot remote, string localDir, CompareReport report, CancellationToken ct)
    {
        var localFiles = LocalStorageHelper.GetAttachmentFiles(localDir).ToList();
        if (localFiles.Count == 0 && !remote.HasAttachments)
            return;

        var serverAttachments = await _apiClient.GetAttachmentsAsync(remote.PageId, ct);
        var priorBaselines = PageMarker.Load(localDir)?.Attachments;

        var localPathsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in localFiles)
            localPathsByName[Path.GetFileName(file)] = file;

        var diffs = new List<CompareAttachmentDiff>();
        var serverNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attachment in serverAttachments)
        {
            var localName = LocalStorageHelper.SanitizeFileName(attachment.Title);
            serverNames.Add(localName);

            if (!localPathsByName.TryGetValue(localName, out var localPath))
            {
                diffs.Add(new CompareAttachmentDiff(attachment.Title, AttachmentDiffKind.OnlyRemote));
                continue;
            }

            var prior = priorBaselines != null && priorBaselines.TryGetValue(localName, out var p) ? p : null;
            var localChanged = await LocalStorageHelper.HasAttachmentChangedLocallyAsync(localPath, prior, ct);
            AttachmentDiffKind? kind = AttachmentChangeAnalyzer.Analyze(prior, attachment.Version?.Number, localChanged) switch
            {
                AttachmentChangeOrigin.Unchanged => null,
                AttachmentChangeOrigin.Local => AttachmentDiffKind.ChangedLocal,
                AttachmentChangeOrigin.Server => AttachmentDiffKind.ChangedServer,
                AttachmentChangeOrigin.Conflict => AttachmentDiffKind.ChangedBoth,
                // Unknown (no baseline) → cheap size comparison.
                _ => attachment.Extensions?.FileSize is long serverSize && new FileInfo(localPath).Length != serverSize
                    ? AttachmentDiffKind.SizeDiffers
                    : null,
            };

            if (kind is AttachmentDiffKind diffKind)
                diffs.Add(new CompareAttachmentDiff(attachment.Title, diffKind));
        }

        foreach (var name in localPathsByName.Keys)
        {
            if (!serverNames.Contains(name))
                diffs.Add(new CompareAttachmentDiff(name, AttachmentDiffKind.OnlyLocal));
        }

        if (diffs.Count == 0)
            return;

        report.AttachmentsChanged.Add(new CompareAttachmentsChangedPageInfo
        {
            PageId = remote.PageId,
            Title = remote.Title,
            Path = remote.RelativePath,
            Differences = diffs
        });
    }

    private async Task CollectRemotePagesAsync(
        PageData page,
        string relativePath,
        bool recursive,
        ConcurrentDictionary<string, RemotePageSnapshot> remotePages,
        CancellationToken ct)
    {
        remotePages[page.Id] = new RemotePageSnapshot(
            page.Id,
            page.Title,
            relativePath,
            page.Body.Storage.Value,
            page.Version?.When?.ToUniversalTime(),
            page.Version?.Number,
            // null = unknown: Cloud v2 payloads carry no childTypes, so the
            // attachment check must run rather than be silently skipped
            // (Server/DC always populates it via expand=childTypes.all).
            page.ChildTypes?.HasAttachments ?? true);

        if (!recursive)
            return;

        if (!(page.ChildTypes?.HasPages ?? true))
            return;

        var children = await _apiClient.GetChildrenPagesAsync(page.Id, ct);
        await Parallel.ForEachAsync(
            children,
            new ParallelOptions { MaxDegreeOfParallelism = _maxParallelism, CancellationToken = ct },
            async (child, _) =>
            {
                var childRelativePath = LocalStorageHelper.NormalizeRelativePath(
                    Path.Combine(relativePath, LocalStorageHelper.SanitizeFileName(child.Title)));
                await CollectRemotePagesAsync(child, childRelativePath, recursive, remotePages, ct);
            });
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

            var pageId = PageMarker.ParseFileName(markerName).PageId;
            var pageDir = Path.GetDirectoryName(markerFile);
            if (string.IsNullOrEmpty(pageDir))
                continue;

            var normalizedDir = Path.GetFullPath(pageDir);
            var relativePath = LocalStorageHelper.NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = PageMarker.Load(normalizedDir)?.Title ?? Path.GetFileName(normalizedDir);

            if (pagesById.ContainsKey(pageId))
            {
                report.Notes.Add($"Duplicate local .id marker found for page ID {pageId}. Only the first one is used.");
                continue;
            }

            var dirDate = Directory.GetLastWriteTimeUtc(normalizedDir);

            pagesById[pageId] = new LocalPageSnapshot(pageId, title, normalizedDir, relativePath, dirDate);
        }

        foreach (var localDir in LocalStorageHelper.EnumeratePageDirectories(localRootDir))
        {
            var normalizedDir = Path.GetFullPath(localDir);
            var relativePath = LocalStorageHelper.NormalizeRelativePath(Path.GetRelativePath(outputDir, normalizedDir));
            var title = Path.GetFileName(normalizedDir);
            var localPageId = PageMarker.Load(normalizedDir)?.PageId;

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

    private async Task DetectRootPageParentMismatchAsync(
        CompareReport report,
        PageData rootPage,
        LocalComparisonSnapshot localSnapshot,
        string rootRelativePath,
        bool detectSource,
        CancellationToken ct)
    {
        if (!localSnapshot.PagesById.TryGetValue(rootPage.Id, out var localRootSnapshot))
            return;

        var localParentDir = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(localRootSnapshot.DirectoryPath));
        if (string.IsNullOrEmpty(localParentDir))
            return;

        var localParentPageId = PageMarker.Load(localParentDir)?.PageId;
        if (localParentPageId == null)
            return;

        if (string.Equals(rootPage.ParentId, localParentPageId, StringComparison.OrdinalIgnoreCase))
            return;

        var existing = report.RenamedOrMovedInConfluence
            .FirstOrDefault(r => string.Equals(r.PageId, rootPage.Id, StringComparison.OrdinalIgnoreCase));

        var serverParentTitle = rootPage.Ancestors.Count > 0 ? rootPage.Ancestors[^1].Title : "";
        var localParentTitle = LocalStorageHelper.GetPageTitle(localParentDir);

        var moveSource = await _changeSourceAnalyzer.AnalyzeMoveAsync(
            rootPage.Id, serverParentTitle, localParentTitle,
            rootPage.Version?.When?.ToUniversalTime(),
            localRootSnapshot.DirectoryLastModifiedUtc,
            detectSource, ct);

        if (existing != null)
        {
            existing.MoveSource ??= moveSource;
            return;
        }

        report.RenamedOrMovedInConfluence.Add(new CompareRenamedOrMovedPageInfo
        {
            PageId = rootPage.Id,
            Title = rootPage.Title,
            LocalPath = localRootSnapshot.RelativePath,
            ConfluencePath = rootRelativePath,
            MoveSource = moveSource
        });
    }

}
