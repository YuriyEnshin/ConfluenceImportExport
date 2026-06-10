using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public sealed class UploadUpdateCommandHandler : ICommandHandler
{
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<UploadUpdateOptions> _opts;
    private readonly IConfluenceApiClient _apiClient;
    private readonly IContentNormalizer _normalizer;
    private readonly IContentHasher _hasher;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConsoleWriter _writer;

    public UploadUpdateCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<UploadUpdateOptions> opts,
        IConfluenceApiClient apiClient,
        IContentNormalizer normalizer,
        IContentHasher hasher,
        ILoggerFactory loggerFactory,
        IConsoleWriter writer)
    {
        _global = global;
        _opts = opts;
        _apiClient = apiClient;
        _normalizer = normalizer;
        _hasher = hasher;
        _loggerFactory = loggerFactory;
        _writer = writer;
    }

    public async Task<int> ExecuteAsync(CancellationToken ct)
    {
        var g = _global.Value;
        var o = _opts.Value;

        var spaceKey = g.SpaceKey
            ?? throw new ArgumentException("Missing required parameter: --space-key");
        var sourceDir = PathNormalizer.Normalize(o.SourceDir)
            ?? throw new ArgumentException("Missing required parameter: --source-dir");

        var pageId = o.PageId;
        var pageTitle = o.PageTitle;
        if (!string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(pageTitle))
            throw new ArgumentException("--page-id and --page-title are mutually exclusive.");

        var recursive = o.Recursive ?? g.Recursive ?? false;
        var multiTree = o.MultiTree ?? false;
        var dryRun = g.DryRun ?? false;
        var showReport = g.Report ?? false;
        var maxParallelism = g.MaxParallelism ?? GlobalOptions.DefaultMaxParallelism;

        if (dryRun)
            _writer.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

        var desc = (recursive ? " (recursive)" : "") + (multiTree ? " (multi-tree)" : "");
        _writer.WriteLine($"Upload update: pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

        var service = new UploadService(
            _apiClient,
            _normalizer,
            _loggerFactory.CreateLogger<UploadService>(),
            dryRun,
            maxParallelism,
            hasher: _hasher);

        var report = await service.UploadUpdateAsync(spaceKey, sourceDir, pageId, pageTitle, recursive, multiTree: multiTree, ct: ct);

        _writer.WriteLine("Upload update completed.");
        if (showReport)
            report.PrintReport(_writer);
        return 0;
    }
}
