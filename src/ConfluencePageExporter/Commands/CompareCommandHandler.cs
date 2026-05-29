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
    private readonly IContentNormalizer _normalizer;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IConsoleWriter _writer;

    public CompareCommandHandler(
        IOptions<GlobalOptions> global,
        IOptions<CompareOptions> opts,
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
        var maxParallelism = g.MaxParallelism ?? 8;

        var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
        _writer.WriteLine($"Comparing page {pageIdentifier} in space '{spaceKey}' with local folder '{outputDir}'{(recursive ? " (recursive)" : "")}{(detectSource ? " (detect-source)" : "")}...");

        var analyzer = new ChangeSourceAnalyzer(
            _apiClient,
            _loggerFactory.CreateLogger<ChangeSourceAnalyzer>());

        var service = new CompareService(
            _apiClient,
            analyzer,
            _normalizer,
            _loggerFactory.CreateLogger<CompareService>(),
            maxParallelism);

        var report = await service.CompareAsync(spaceKey, pageId, pageTitle, outputDir, recursive, matchByTitle, detectSource, ct);
        PrintReport(report, _writer);
        return 0;
    }

    private static void PrintReport(CompareReport report, IConsoleWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("Compare report");
        writer.WriteLine("==============");

        if (report.Conflicts.Count > 0)
        {
            writer.WriteLine($"Conflicts (changed on both sides): {report.Conflicts.Count}");
            foreach (var page in report.Conflicts)
            {
                writer.WriteLine($"  !! [{page.PageId}] {page.Title} ({page.Path})");
                if (page.ChangeSource != null)
                    writer.WriteLine($"     {page.ChangeSource.Reason}");
            }
        }

        writer.WriteLine($"Added in Confluence: {report.AddedInConfluence.Count}");
        foreach (var page in report.AddedInConfluence)
            writer.WriteLine($"  + [{page.PageId}] {page.Title} ({page.Path})");

        writer.WriteLine($"Deleted in Confluence: {report.DeletedInConfluence.Count}");
        foreach (var page in report.DeletedInConfluence)
            writer.WriteLine($"  - [{page.PageId}] {page.Title} ({page.Path})");

        writer.WriteLine($"Renamed/moved: {report.RenamedOrMovedInConfluence.Count}");
        foreach (var page in report.RenamedOrMovedInConfluence)
        {
            writer.WriteLine($"  ~ [{page.PageId}] {page.Title} | local: {page.LocalPath} -> confluence: {page.ConfluencePath}");
            if (page.RenameSource != null)
                writer.WriteLine($"    Переименование: {FormatSource(page.RenameSource)}");
            if (page.MoveSource != null)
                writer.WriteLine($"    Перемещение: {FormatSource(page.MoveSource)}");
        }

        writer.WriteLine($"Content changed: {report.ContentChanged.Count}");
        foreach (var page in report.ContentChanged)
        {
            writer.WriteLine($"  * [{page.PageId}] {page.Title} ({page.Path})");
            if (page.ChangeSource != null)
                writer.WriteLine($"    Источник: {FormatSource(page.ChangeSource)}");
        }

        if (report.Notes.Count > 0)
        {
            writer.WriteLine("Notes:");
            foreach (var note in report.Notes)
                writer.WriteLine($"  ! {note}");
        }
    }

    private static string FormatSource(ChangeSourceInfo source)
    {
        var origin = source.Origin switch
        {
            ChangeOrigin.Local => "ЛОКАЛЬНО",
            ChangeOrigin.Server => "СЕРВЕР",
            ChangeOrigin.Conflict => "КОНФЛИКТ",
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
