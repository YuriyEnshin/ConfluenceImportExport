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
    private readonly ILoggerFactory _loggerFactory;

    public UploadUpdateCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<UploadUpdateOptions> opts,
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

        if (dryRun)
            Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

        var desc = recursive ? " (recursive)" : "";
        Console.WriteLine($"Upload update: pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

        var service = new UploadService(
            _apiClient,
            _loggerFactory.CreateLogger<UploadService>(),
            dryRun);

        var report = await service.UploadUpdateAsync(spaceKey, sourceDir, pageId, pageTitle, recursive);

        Console.WriteLine("Upload update completed.");
        if (showReport)
            report.PrintReport();
        return 0;
    }
}
