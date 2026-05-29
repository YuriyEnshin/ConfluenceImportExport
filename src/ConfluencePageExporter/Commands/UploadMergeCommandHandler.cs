using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public sealed class UploadMergeCommandHandler : ICommandHandler
{
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<UploadMergeOptions> _opts;
    private readonly IConfluenceApiClient _apiClient;
    private readonly IContentNormalizer _normalizer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConsoleWriter _writer;

    public UploadMergeCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<UploadMergeOptions> opts,
        IConfluenceApiClient apiClient,
        IContentNormalizer normalizer,
        ILoggerFactory loggerFactory,
        IConsoleWriter writer)
    {
        _global = global;
        _opts = opts;
        _apiClient = apiClient;
        _normalizer = normalizer;
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
        var dryRun = g.DryRun ?? false;
        var showReport = g.Report ?? false;
        var maxParallelism = g.MaxParallelism ?? 8;

        if (dryRun)
            _writer.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

        var desc = recursive ? " (recursive)" : "";
        _writer.WriteLine($"Upload merge: pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

        var analyzer = new ChangeSourceAnalyzer(
            _apiClient,
            _loggerFactory.CreateLogger<ChangeSourceAnalyzer>());

        var service = new UploadService(
            _apiClient,
            _normalizer,
            _loggerFactory.CreateLogger<UploadService>(),
            dryRun,
            maxParallelism);

        var report = await service.UploadMergeAsync(spaceKey, sourceDir, pageId, pageTitle, recursive, analyzer, ct);

        _writer.WriteLine("Upload merge completed.");
        if (showReport)
            report.PrintReport(_writer);
        return 0;
    }
}
