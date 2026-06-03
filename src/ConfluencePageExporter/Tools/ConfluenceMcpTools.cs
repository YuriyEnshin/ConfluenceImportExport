using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Tools;

/// <summary>
/// MCP tools exposing the six top-level synchronisation operations.
/// Each method is a thin facade: it validates arguments, applies the
/// path sandbox, optionally honours --read-only, calls the underlying
/// service, and returns a uniform JSON envelope (see <see cref="McpToolResult"/>).
/// All exceptions are caught and converted to error envelopes — agents
/// always receive one shape regardless of failure mode.
/// </summary>
[McpServerToolType]
public sealed class ConfluenceMcpTools
{
    // ── download_update ──────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_download_update")]
    [Description("Force-download Confluence pages to local files, overwriting any local changes. Returns SyncReport in 'report' when 'report'=true.")]
    public static async Task<object> DownloadUpdate(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Parent directory that will contain the page subfolder (named after the page title); relative paths resolve against --root-dir, absolute paths must lie inside it")] string outputDir = ".",
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
        [Description("Dry run; do not write any files")] bool dryRun = false,
        [Description("Include the full SyncReport in the result")] bool report = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            ArgValidation.RequireExactlyOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedOut = sandbox.Resolve(outputDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            writer.WriteLine($"Download update: page {Describe(pageId, pageTitle)} from space '{resolvedSpace}'{(recursive ? " (recursive)" : "")}...");
            if (dryRun) writer.WriteLine("DRY RUN MODE: No files will be written to disk.");

            var service = new DownloadService(api, normalizer, loggerFactory.CreateLogger<DownloadService>(), dryRun, maxParallelism);
            var syncReport = await service.DownloadUpdateAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive, cancellationToken);

            writer.WriteLine($"Download update completed. Files saved to: {resolvedOut}");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Download update", syncReport, resolvedOut),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── download_merge ───────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_download_merge")]
    [Description("Download only server-side changes, preserving local edits (smart merge). Conflicts are reported, not overwritten.")]
    public static async Task<object> DownloadMerge(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Parent directory containing (or that will contain) the page subfolder; relative paths resolve against --root-dir")] string outputDir = ".",
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
        [Description("Dry run; do not write any files")] bool dryRun = false,
        [Description("Include the full SyncReport in the result")] bool report = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            ArgValidation.RequireExactlyOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedOut = sandbox.Resolve(outputDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            writer.WriteLine($"Download merge: page {Describe(pageId, pageTitle)} from space '{resolvedSpace}'{(recursive ? " (recursive)" : "")}...");
            if (dryRun) writer.WriteLine("DRY RUN MODE: No files will be written to disk.");

            var analyzer = new ChangeSourceAnalyzer(api, loggerFactory.CreateLogger<ChangeSourceAnalyzer>());
            var service = new DownloadService(api, normalizer, loggerFactory.CreateLogger<DownloadService>(), dryRun, maxParallelism);
            var syncReport = await service.DownloadMergeAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive, analyzer, cancellationToken);

            writer.WriteLine($"Download merge completed. Files saved to: {resolvedOut}");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Download merge", syncReport, resolvedOut),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── upload_update ────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_update")]
    [Description("Force-upload a local page folder to Confluence, overwriting any server-side changes. Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadUpdate(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID to update (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title to update (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Space key. Usually unnecessary for an already-synced tree — the page's actual server space wins. If given and it contradicts the page's real space, the call errors. Defaults to Global:SpaceKey.")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
        [Description("Process every page tree directly under sourceDir independently (each may live in a different space). Use when sourceDir is a container of tree folders, not a single page folder. Incompatible with pageId/pageTitle.")] bool multiTree = false,
        [Description("Dry run; no changes are sent to Confluence")] bool dryRun = false,
        [Description("Include the full SyncReport in the result")] bool report = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            if (mcpOpts.ReadOnly)
                return McpToolResult.Error("READ_ONLY_VIOLATION", "Server is running with --read-only; upload tools are disabled.", writer.Lines);

            ArgValidation.RequireAtMostOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedSrc = sandbox.Resolve(sourceDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            if (dryRun) writer.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");
            writer.WriteLine($"Upload update: pages in space '{resolvedSpace}' from '{resolvedSrc}'{(recursive ? " (recursive)" : "")}{(multiTree ? " (multi-tree)" : "")}...");

            var service = new UploadService(api, normalizer, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            var syncReport = await service.UploadUpdateAsync(resolvedSpace, resolvedSrc, pageId, pageTitle, recursive, explicitSpaceKey: spaceKey, multiTree: multiTree, ct: cancellationToken);

            writer.WriteLine("Upload update completed.");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Upload update", syncReport, resolvedSrc),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── upload_create ────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_create")]
    [Description("Create new Confluence pages from a local folder. Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadCreate(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
        CancellationToken cancellationToken = default,
        [Description("Parent Confluence page ID (mutually exclusive with parentTitle). Omit both to create at space root.")] string? parentId = null,
        [Description("Parent Confluence page title (mutually exclusive with parentId)")] string? parentTitle = null,
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
        [Description("Dry run; no changes are sent to Confluence")] bool dryRun = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            if (mcpOpts.ReadOnly)
                return McpToolResult.Error("READ_ONLY_VIOLATION", "Server is running with --read-only; upload tools are disabled.", writer.Lines);

            ArgValidation.RequireAtMostOne(("parentId", parentId), ("parentTitle", parentTitle));
            var resolvedSrc = sandbox.Resolve(sourceDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            if (dryRun) writer.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");
            var parentDesc = !string.IsNullOrEmpty(parentId) ? $"under parent ID '{parentId}'"
                           : !string.IsNullOrEmpty(parentTitle) ? $"under parent '{parentTitle}'"
                           : "at space root";
            writer.WriteLine($"Creating pages in space '{resolvedSpace}' {parentDesc} from '{resolvedSrc}'{(recursive ? " (recursive)" : "")}...");

            var service = new UploadService(api, normalizer, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            await service.UploadCreateAsync(resolvedSpace, resolvedSrc, parentId, parentTitle, recursive, explicitSpaceKey: spaceKey, ct: cancellationToken);

            writer.WriteLine("Upload create completed.");
            return McpToolResult.Success(
                summary: $"Upload create completed from '{resolvedSrc}' to space '{resolvedSpace}' {parentDesc}.",
                report: null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── upload_merge ─────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_merge")]
    [Description("Upload only local changes, preserving server-side edits (smart merge). Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadMerge(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Space key. Usually unnecessary for an already-synced tree — the page's actual server space wins. If given and it contradicts the page's real space, the call errors. Defaults to Global:SpaceKey.")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
        [Description("Process every page tree directly under sourceDir independently (each may live in a different space). Use when sourceDir is a container of tree folders, not a single page folder. Incompatible with pageId/pageTitle.")] bool multiTree = false,
        [Description("Dry run; no changes are sent to Confluence")] bool dryRun = false,
        [Description("Include the full SyncReport in the result")] bool report = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            if (mcpOpts.ReadOnly)
                return McpToolResult.Error("READ_ONLY_VIOLATION", "Server is running with --read-only; upload tools are disabled.", writer.Lines);

            ArgValidation.RequireAtMostOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedSrc = sandbox.Resolve(sourceDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            if (dryRun) writer.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");
            writer.WriteLine($"Upload merge: pages in space '{resolvedSpace}' from '{resolvedSrc}'{(recursive ? " (recursive)" : "")}{(multiTree ? " (multi-tree)" : "")}...");

            var analyzer = new ChangeSourceAnalyzer(api, loggerFactory.CreateLogger<ChangeSourceAnalyzer>());
            var service = new UploadService(api, normalizer, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            var syncReport = await service.UploadMergeAsync(resolvedSpace, resolvedSrc, pageId, pageTitle, recursive, analyzer, explicitSpaceKey: spaceKey, multiTree: multiTree, ct: cancellationToken);

            writer.WriteLine("Upload merge completed.");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Upload merge", syncReport, resolvedSrc),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── compare ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_compare")]
    [Description("Compare Confluence pages with the local exported copy. Always returns the full CompareReport.")]
    public static async Task<object> Compare(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Parent directory containing the exported page subfolder; relative paths resolve against --root-dir")] string outputDir = ".",
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively traverse child pages")] bool recursive = false,
        [Description("Match by title when the .id marker is missing")] bool matchByTitle = false,
        [Description("Analyse version history to determine change source (server vs local)")] bool detectSource = false)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            ArgValidation.RequireExactlyOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedOut = sandbox.Resolve(outputDir);
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var maxParallelism = globalOpts.Value.MaxParallelism ?? 8;

            writer.WriteLine($"Comparing page {Describe(pageId, pageTitle)} in space '{resolvedSpace}' with local folder '{resolvedOut}'{(recursive ? " (recursive)" : "")}{(detectSource ? " (detect-source)" : "")}...");

            var analyzer = new ChangeSourceAnalyzer(api, loggerFactory.CreateLogger<ChangeSourceAnalyzer>());
            var service = new CompareService(api, analyzer, normalizer, loggerFactory.CreateLogger<CompareService>(), maxParallelism);
            var compareReport = await service.CompareAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive, matchByTitle, detectSource, cancellationToken);

            return McpToolResult.Success(
                summary: BuildCompareSummary(compareReport),
                report: compareReport,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── ping ─────────────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_ping")]
    [Description("Diagnostic: verify the MCP server's connectivity and credentials to Confluence with a single lightweight call. Returns base URL, authenticated user, response latency, configured space key, and sandbox settings. Safe in --read-only mode.")]
    public static async Task<object> Ping(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        CancellationToken cancellationToken = default)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            var globals = globalOpts.Value;
            writer.WriteLine($"Pinging Confluence at '{globals.BaseUrl}'...");

            var started = Stopwatch.GetTimestamp();
            var user = await api.GetCurrentUserAsync(cancellationToken);
            var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;

            writer.WriteLine($"OK in {elapsedMs}ms — authenticated as '{user.Username ?? user.AccountId ?? "?"}' ({user.DisplayName ?? "no display name"}).");

            var report = new
            {
                baseUrl = globals.BaseUrl,
                spaceKey = globals.SpaceKey,
                authenticatedAs = new
                {
                    username = user.Username,
                    displayName = user.DisplayName,
                    userKey = user.UserKey,
                    accountId = user.AccountId,
                },
                elapsedMs,
                sandbox = new
                {
                    rootDir = mcpOpts.RootDir,
                    readOnly = mcpOpts.ReadOnly,
                },
            };
            return McpToolResult.Success(
                summary: $"Confluence reachable at '{globals.BaseUrl}' in {elapsedMs}ms as '{user.Username ?? user.AccountId ?? "?"}'.",
                report: report,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── get_page_content ────────────────────────────────────────────────

    /// <summary>
    /// Default cap on returned content size. Tuned to keep a single tool
    /// invocation well under any reasonable agent context budget while still
    /// covering the vast majority of real-world Confluence pages (most are
    /// under 50 KB of storage XML).
    /// </summary>
    private const int DefaultMaxContentBytes = 262144; // 256 KB

    [McpServerTool(Name = "confluence_get_page_content")]
    [Description("Fetch the storage-format (XHTML) content of a Confluence page, optionally at a specific historical version and optionally normalised for diffing. Designed for the agent-assisted merge workflow: when 'compare' or 'download_merge' reports a conflict, call this to get the server-side content the agent can diff against the local index.html. For 3-way merges, call once with the local marker's version and once without (current). Safe in --read-only mode.")]
    public static async Task<object> GetPageContent(
        IConfluenceApiClient api,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IOptions<GlobalOptions> globalOpts,
        CancellationToken cancellationToken = default,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey. Required when pageTitle is used.")] string? spaceKey = null,
        [Description("Historical version number. Omit for the latest version. Useful for 3-way merges: pass the version recorded in the local .idPAGEID_VER marker to get the merge base.")] int? version = null,
        [Description("If true, run the returned content through the same storage-format normalisation used by 'compare' (canonical entities, sorted attributes, trimmed inter-tag whitespace) so textual diffs reflect semantic differences only.")] bool normalize = false,
        [Description("Hard cap on the size of the returned 'content' field in bytes. Default 262144 (256 KB). When exceeded, the content is truncated and 'truncated':true / 'fullSize' are set so the agent knows to fetch via 'download_update' instead.")] int? maxBytes = null)
    {
        var writer = new BufferingConsoleWriter();
        try
        {
            ArgValidation.RequireExactlyOne(("pageId", pageId), ("pageTitle", pageTitle));
            var resolvedSpace = ResolveSpaceKey(spaceKey, globalOpts.Value);
            var cap = maxBytes ?? DefaultMaxContentBytes;
            if (cap < 1024)
                throw new ArgumentException("maxBytes must be at least 1024 (1 KB).");

            // Resolve title → ID first; both code paths below need a real ID.
            var resolvedId = pageId;
            if (string.IsNullOrEmpty(resolvedId))
            {
                writer.WriteLine($"Resolving title '{pageTitle}' in space '{resolvedSpace}'...");
                resolvedId = await api.FindPageByTitleAsync(resolvedSpace, parentId: null, title: pageTitle!, ct: cancellationToken)
                    ?? throw new InvalidOperationException($"Could not resolve page by title '{pageTitle}' in space '{resolvedSpace}'.");
            }

            PageData page;
            if (version is int v)
            {
                writer.WriteLine($"Fetching page ID '{resolvedId}' at historical version {v}...");
                page = await api.GetPageContentAtVersionAsync(resolvedId, v, cancellationToken);
            }
            else
            {
                writer.WriteLine($"Fetching page ID '{resolvedId}' (current version)...");
                page = await api.GetPageByIdAsync(resolvedId, cancellationToken);
            }

            var rawContent = page.Body?.Storage?.Value ?? string.Empty;
            var finalContent = normalize
                ? normalizer.NormalizeForComparison(rawContent)
                : rawContent;
            var utf8 = System.Text.Encoding.UTF8;
            var bytes = utf8.GetBytes(finalContent);
            var fullSize = bytes.Length;
            var truncated = fullSize > cap;
            if (truncated)
            {
                // Cut at `cap` bytes, then walk back to the last complete
                // UTF-8 sequence so we never hand the agent invalid UTF-8.
                // Continuation bytes match 10xxxxxx (0xC0 mask, 0x80 value).
                var safeEnd = cap;
                while (safeEnd > 0 && (bytes[safeEnd] & 0xC0) == 0x80) safeEnd--;
                finalContent = utf8.GetString(bytes, 0, safeEnd);
            }

            writer.WriteLine($"OK: {fullSize} bytes (returned {(truncated ? "truncated" : "in full")}). Version {page.Version?.Number}.");

            var report = new
            {
                pageId = page.Id,
                title = page.Title,
                // The page's actual server space (resolvedSpace is only the
                // request hint and can differ in a multi-space setup).
                spaceKey = page.SpaceKey ?? resolvedSpace,
                version = new
                {
                    number = page.Version?.Number,
                    when = page.Version?.When,
                },
                content = finalContent,
                contentLength = System.Text.Encoding.UTF8.GetByteCount(finalContent),
                fullSize,
                truncated,
                normalized = normalize,
            };
            return McpToolResult.Success(
                summary: $"Fetched '{page.Title}' v{page.Version?.Number}: {fullSize} bytes{(truncated ? " (truncated)" : "")}{(normalize ? ", normalized" : "")}.",
                report: report,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            return McpToolResult.FromException(ex, loggerFactory, writer.Lines);
        }
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private static string Describe(string? pageId, string? pageTitle) =>
        !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";

    private static string ResolveSpaceKey(string? toolSpaceKey, GlobalOptions globalOpts)
    {
        var key = toolSpaceKey ?? globalOpts.SpaceKey;
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Space key is required: pass 'spaceKey' to the tool or configure Global:SpaceKey on the server.");
        return key;
    }

    private static string BuildSyncSummary(string operation, SyncReport report, string targetPath)
    {
        var parts = new List<string> { $"{operation} completed in {targetPath}" };
        if (report.ConflictPages.Count > 0) parts.Add($"{report.ConflictPages.Count} conflict(s)");
        if (report.OrphanPages.Count > 0) parts.Add($"{report.OrphanPages.Count} orphan(s)");
        if (report.SkippedPages.Count > 0) parts.Add($"{report.SkippedPages.Count} skipped");
        var summary = string.Join("; ", parts) + ".";
        if (report.ConflictPages.Count > 0)
            summary += " To resolve a conflict, call confluence_get_page_content with the conflicting pageId, diff it against the local index.html, and upload the merged result with confluence_upload_update.";
        return summary;
    }

    private static string BuildCompareSummary(CompareReport report)
    {
        var summary = $"Compare completed. Added: {report.AddedInConfluence.Count}; Deleted: {report.DeletedInConfluence.Count}; "
            + $"Renamed/moved: {report.RenamedOrMovedInConfluence.Count}; Content changed: {report.ContentChanged.Count}; "
            + $"Conflicts: {report.Conflicts.Count}; Notes: {report.Notes.Count}.";
        if (report.Conflicts.Count > 0)
            summary += " To resolve a conflict, call confluence_get_page_content with the conflicting pageId, diff it against the local index.html, and upload the merged result with confluence_upload_update.";
        if (report.ContentChanged.Count > 0)
            summary += " For any page in 'ContentChanged', call confluence_get_page_content with its pageId to inspect the latest server content and assist with merging.";
        return summary;
    }
}
