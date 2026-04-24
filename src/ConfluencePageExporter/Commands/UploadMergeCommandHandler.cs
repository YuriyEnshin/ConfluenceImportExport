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
    private readonly ILoggerFactory _loggerFactory;

    public UploadMergeCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<UploadMergeOptions> opts,
        IConfluenceApiClient apiClient,
        ILoggerFactory loggerFactory)
    {
        _global = global;
        _opts = opts;
        _apiClient = apiClient;
        _loggerFactory = loggerFactory;
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
            Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

        var desc = recursive ? " (recursive)" : "";
        Console.WriteLine($"Upload merge: pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

        var analyzer = new ChangeSourceAnalyzer(
            _apiClient,
            _loggerFactory.CreateLogger<ChangeSourceAnalyzer>());

        var service = new UploadService(
            _apiClient,
            _loggerFactory.CreateLogger<UploadService>(),
            dryRun,
            maxParallelism);

        var report = await service.UploadMergeAsync(spaceKey, sourceDir, pageId, pageTitle, recursive, analyzer);

        Console.WriteLine("Upload merge completed.");
        if (showReport)
            report.PrintReport();
        return 0;
    }
}
