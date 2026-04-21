using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public sealed class UploadCreateCommandHandler : ICommandHandler
{
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<UploadCreateOptions> _opts;
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILoggerFactory _loggerFactory;

    public UploadCreateCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<UploadCreateOptions> opts,
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

        var parentId = o.ParentId;
        var parentTitle = o.ParentTitle;
        if (!string.IsNullOrEmpty(parentId) && !string.IsNullOrEmpty(parentTitle))
            throw new ArgumentException("--parent-id and --parent-title are mutually exclusive.");

        var recursive = o.Recursive ?? g.Recursive ?? false;
        var dryRun = g.DryRun ?? false;

        if (dryRun)
            Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

        var parentDesc = !string.IsNullOrEmpty(parentId) ? $"under parent ID '{parentId}'"
                       : !string.IsNullOrEmpty(parentTitle) ? $"under parent '{parentTitle}'"
                       : "at space root";
        var desc = recursive ? " (recursive)" : "";
        Console.WriteLine($"Creating pages in space '{spaceKey}' {parentDesc} from '{sourceDir}'{desc}...");

        var service = new UploadService(
            _apiClient,
            _loggerFactory.CreateLogger<UploadService>(),
            dryRun);

        await service.UploadCreateAsync(spaceKey, sourceDir, parentId, parentTitle, recursive);
        Console.WriteLine("Upload create completed.");
        return 0;
    }
}
