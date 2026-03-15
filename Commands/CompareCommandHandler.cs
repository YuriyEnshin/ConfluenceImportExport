using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public sealed class CompareCommandHandler : ICommandHandler
{
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<CompareOptions> _opts;
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILoggerFactory _loggerFactory;

    public CompareCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<CompareOptions> opts,
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
        var matchByTitle = o.MatchByTitle ?? false;
        var detectSource = o.DetectSource ?? false;

        var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
        Console.WriteLine($"Comparing page {pageIdentifier} in space '{spaceKey}' with local folder '{outputDir}'{(recursive ? " (recursive)" : "")}{(detectSource ? " (detect-source)" : "")}...");

        var analyzer = new ChangeSourceAnalyzer(
            _apiClient,
            _loggerFactory.CreateLogger<ChangeSourceAnalyzer>());

        var service = new CompareService(
            _apiClient,
            analyzer,
            _loggerFactory.CreateLogger<CompareService>());

        var report = await service.CompareAsync(spaceKey, pageId, pageTitle, outputDir, recursive, matchByTitle, detectSource);
        PrintReport(report);
        return 0;
    }

    private static void PrintReport(CompareReport report)
    {
        Console.WriteLine();
        Console.WriteLine("Compare report");
        Console.WriteLine("==============");
        Console.WriteLine($"Added in Confluence: {report.AddedInConfluence.Count}");
        foreach (var page in report.AddedInConfluence)
            Console.WriteLine($"  + [{page.PageId}] {page.Title} ({page.Path})");

        Console.WriteLine($"Deleted in Confluence: {report.DeletedInConfluence.Count}");
        foreach (var page in report.DeletedInConfluence)
            Console.WriteLine($"  - [{page.PageId}] {page.Title} ({page.Path})");

        Console.WriteLine($"Renamed/moved: {report.RenamedOrMovedInConfluence.Count}");
        foreach (var page in report.RenamedOrMovedInConfluence)
        {
            Console.WriteLine($"  ~ [{page.PageId}] {page.Title} | local: {page.LocalPath} -> confluence: {page.ConfluencePath}");
            if (page.RenameSource != null)
                Console.WriteLine($"    Переименование: {FormatSource(page.RenameSource)}");
            if (page.MoveSource != null)
                Console.WriteLine($"    Перемещение: {FormatSource(page.MoveSource)}");
        }

        Console.WriteLine($"Content changed: {report.ContentChanged.Count}");
        foreach (var page in report.ContentChanged)
        {
            Console.WriteLine($"  * [{page.PageId}] {page.Title} ({page.Path})");
            if (page.ChangeSource != null)
                Console.WriteLine($"    Источник: {FormatSource(page.ChangeSource)}");
        }

        if (report.Notes.Count > 0)
        {
            Console.WriteLine("Notes:");
            foreach (var note in report.Notes)
                Console.WriteLine($"  ! {note}");
        }
    }

    private static string FormatSource(ChangeSourceInfo source)
    {
        var origin = source.Origin switch
        {
            ChangeOrigin.Local => "ЛОКАЛЬНО",
            ChangeOrigin.Server => "СЕРВЕР",
            _ => "НЕИЗВЕСТНО"
        };
        var confidence = source.Confidence switch
        {
            ChangeConfidence.High => "высокая",
            ChangeConfidence.Medium => "средняя",
            _ => "низкая"
        };
        return $"{origin} ({confidence}) — {source.Reason}";
    }
}
