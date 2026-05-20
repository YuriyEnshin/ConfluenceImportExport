using System.ComponentModel;
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
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle,
        [Description("Output directory; relative paths resolve against the server's --root-dir, absolute paths must lie inside it")] string outputDir,
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

            var service = new DownloadService(api, loggerFactory.CreateLogger<DownloadService>(), dryRun, maxParallelism);
            var syncReport = await service.DownloadUpdateAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive);

            writer.WriteLine($"Download update completed. Files saved to: {resolvedOut}");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Download update", syncReport, resolvedOut),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
        }
    }

    // ── download_merge ───────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_download_merge")]
    [Description("Download only server-side changes, preserving local edits (smart merge). Conflicts are reported, not overwritten.")]
    public static async Task<object> DownloadMerge(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle,
        [Description("Output directory; relative paths resolve against the server's --root-dir")] string outputDir,
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
            var service = new DownloadService(api, loggerFactory.CreateLogger<DownloadService>(), dryRun, maxParallelism);
            var syncReport = await service.DownloadMergeAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive, analyzer);

            writer.WriteLine($"Download merge completed. Files saved to: {resolvedOut}");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Download merge", syncReport, resolvedOut),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
        }
    }

    // ── upload_update ────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_update")]
    [Description("Force-upload a local page folder to Confluence, overwriting any server-side changes. Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadUpdate(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
        [Description("Confluence page ID to update (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title to update (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
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
            writer.WriteLine($"Upload update: pages in space '{resolvedSpace}' from '{resolvedSrc}'{(recursive ? " (recursive)" : "")}...");

            var service = new UploadService(api, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            var syncReport = await service.UploadUpdateAsync(resolvedSpace, resolvedSrc, pageId, pageTitle, recursive);

            writer.WriteLine("Upload update completed.");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Upload update", syncReport, resolvedSrc),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
        }
    }

    // ── upload_create ────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_create")]
    [Description("Create new Confluence pages from a local folder. Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadCreate(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
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

            var service = new UploadService(api, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            await service.UploadCreateAsync(resolvedSpace, resolvedSrc, parentId, parentTitle, recursive);

            writer.WriteLine("Upload create completed.");
            return McpToolResult.Success(
                summary: $"Upload create completed from '{resolvedSrc}' to space '{resolvedSpace}' {parentDesc}.",
                report: null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
        }
    }

    // ── upload_merge ─────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_upload_merge")]
    [Description("Upload only local changes, preserving server-side edits (smart merge). Blocked when the server runs with --read-only.")]
    public static async Task<object> UploadMerge(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        ConfluenceMcpOptions mcpOpts,
        [Description("Local page folder to upload; relative paths resolve against the server's --root-dir")] string sourceDir,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId = null,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle = null,
        [Description("Space key. Optional — defaults to the server's configured Global:SpaceKey")] string? spaceKey = null,
        [Description("Recursively process child pages")] bool recursive = false,
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
            writer.WriteLine($"Upload merge: pages in space '{resolvedSpace}' from '{resolvedSrc}'{(recursive ? " (recursive)" : "")}...");

            var analyzer = new ChangeSourceAnalyzer(api, loggerFactory.CreateLogger<ChangeSourceAnalyzer>());
            var service = new UploadService(api, loggerFactory.CreateLogger<UploadService>(), dryRun, maxParallelism);
            var syncReport = await service.UploadMergeAsync(resolvedSpace, resolvedSrc, pageId, pageTitle, recursive, analyzer);

            writer.WriteLine("Upload merge completed.");
            return McpToolResult.Success(
                summary: BuildSyncSummary("Upload merge", syncReport, resolvedSrc),
                report: report ? syncReport : null,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
        }
    }

    // ── compare ──────────────────────────────────────────────────────────

    [McpServerTool(Name = "confluence_compare")]
    [Description("Compare Confluence pages with the local exported copy. Always returns the full CompareReport.")]
    public static async Task<object> Compare(
        IConfluenceApiClient api,
        ILoggerFactory loggerFactory,
        IPathSandbox sandbox,
        IOptions<GlobalOptions> globalOpts,
        [Description("Confluence page ID (mutually exclusive with pageTitle)")] string? pageId,
        [Description("Confluence page title (mutually exclusive with pageId)")] string? pageTitle,
        [Description("Local output directory with the exported pages; relative paths resolve against the server's --root-dir")] string outputDir,
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
            var service = new CompareService(api, analyzer, loggerFactory.CreateLogger<CompareService>(), maxParallelism);
            var compareReport = await service.CompareAsync(resolvedSpace, pageId, pageTitle, resolvedOut, recursive, matchByTitle, detectSource);

            return McpToolResult.Success(
                summary: BuildCompareSummary(compareReport),
                report: compareReport,
                logs: writer.Lines);
        }
        catch (Exception ex)
        {
            var (code, message) = McpToolResult.Classify(ex);
            return McpToolResult.Error(code, message, writer.Lines);
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
        return string.Join("; ", parts) + ".";
    }

    private static string BuildCompareSummary(CompareReport report)
    {
        return $"Compare completed. Added: {report.AddedInConfluence.Count}; Deleted: {report.DeletedInConfluence.Count}; "
            + $"Renamed/moved: {report.RenamedOrMovedInConfluence.Count}; Content changed: {report.ContentChanged.Count}; "
            + $"Notes: {report.Notes.Count}.";
    }
}
