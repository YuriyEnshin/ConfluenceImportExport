using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public sealed class DownloadMergeCommandHandler : ICommandHandler
{
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<DownloadMergeOptions> _opts;
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILoggerFactory _loggerFactory;

    public DownloadMergeCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<DownloadMergeOptions> opts,
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
        var outputDir = PathNormalizer.Normalize(o.OutputDir)
            ?? throw new ArgumentException("Missing required parameter: --output-dir");

        var pageId = o.PageId;
        var pageTitle = o.PageTitle;

        if (string.IsNullOrEmpty(pageId) && string.IsNullOrEmpty(pageTitle))
            throw new ArgumentException("Either --page-id or --page-title must be specified.");
        if (!string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(pageTitle))
            throw new ArgumentException("--page-id and --page-title are mutually exclusive. Specify only one.");

        var recursive = o.Recursive ?? g.Recursive ?? false;
        var dryRun = g.DryRun ?? false;
        var showReport = g.Report ?? false;

        var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
        Console.WriteLine($"Download merge: page {pageIdentifier} from space '{spaceKey}'{(recursive ? " (recursive)" : "")}...");
        if (dryRun)
            Console.WriteLine("DRY RUN MODE: No files will be written to disk.");

        var analyzer = new ChangeSourceAnalyzer(
            _apiClient,
            _loggerFactory.CreateLogger<ChangeSourceAnalyzer>());

        var service = new DownloadService(
            _apiClient,
            _loggerFactory.CreateLogger<DownloadService>(),
            dryRun);

        var report = await service.DownloadMergeAsync(spaceKey, pageId, pageTitle, outputDir, recursive, analyzer);

        Console.WriteLine($"Download merge completed. Files saved to: {outputDir}");
        if (showReport)
            report.PrintReport();
        return 0;
    }
}
