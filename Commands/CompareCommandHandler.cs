using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class CompareCommandHandler
{
    private readonly ILogger<Exporter> _logger;

    public CompareCommandHandler(ILogger<Exporter> logger)
    {
        _logger = logger;
    }

    public Command CreateCommand()
    {
        var baseUrlOption = CommandOptionsBuilder.CreateRequiredStringOption("--base-url", "Base URL of Confluence instance (e.g., https://wiki.example.com)");
        var usernameOption = CommandOptionsBuilder.CreateRequiredStringOption("--username", "Username or email for authentication");
        var tokenOption = CommandOptionsBuilder.CreateRequiredStringOption("--token", "API token or password for authentication");
        var spaceKeyOption = CommandOptionsBuilder.CreateRequiredStringOption("--space-key", "Confluence space key (e.g., DOCS)");
        var authTypeOption = CommandOptionsBuilder.CreateEnumOption("--auth-type", "Authentication type: 'onprem' (default) or 'cloud'", "onprem", "cloud");
        var pageIdOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-id", "ID of the page to compare (mutually exclusive with --page-title)");
        var pageTitleOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-title", "Title of the page to compare (mutually exclusive with --page-id)");
        var outputDirOption = CommandOptionsBuilder.CreateRequiredStringOption("--output-dir", "Output directory with local exported pages");
        var recursiveOption = CommandOptionsBuilder.CreateBoolOption("--recursive", "Recursively compare all child pages");
        var matchByTitleOption = CommandOptionsBuilder.CreateBoolOption("--match-by-title", "If no .id marker is found locally, try matching by titles/folder names");

        var compareCommand = new Command("compare", "Compare Confluence pages with local exported copy")
        {
            baseUrlOption,
            usernameOption,
            tokenOption,
            spaceKeyOption,
            pageIdOption,
            pageTitleOption,
            outputDirOption,
            recursiveOption,
            matchByTitleOption,
            authTypeOption
        };

        compareCommand.SetAction(async (parseResult) =>
        {
            var baseUrl = parseResult.GetValue(baseUrlOption) ?? "";
            var username = parseResult.GetValue(usernameOption) ?? "";
            var token = parseResult.GetValue(tokenOption) ?? "";
            var spaceKey = parseResult.GetValue(spaceKeyOption) ?? "";
            var pageId = parseResult.GetValue(pageIdOption);
            var pageTitle = parseResult.GetValue(pageTitleOption);
            var outputDir = parseResult.GetValue(outputDirOption) ?? "";
            var recursive = parseResult.GetValue(recursiveOption);
            var matchByTitle = parseResult.GetValue(matchByTitleOption);
            var authType = parseResult.GetValue(authTypeOption) ?? "onprem";

            if (string.IsNullOrEmpty(pageId) && string.IsNullOrEmpty(pageTitle))
            {
                Console.WriteLine("Error: Either --page-id or --page-title must be specified.");
                return;
            }
            if (!string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(pageTitle))
            {
                Console.WriteLine("Error: --page-id and --page-title are mutually exclusive. Specify only one.");
                return;
            }

            var exporter = new Exporter(baseUrl, username, token, _logger, authType);
            var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
            Console.WriteLine($"Comparing page {pageIdentifier} in space '{spaceKey}' with local folder '{outputDir}'{(recursive ? " (recursive)" : "")}...");

            var report = await exporter.CompareAsync(spaceKey, pageId, pageTitle, outputDir, recursive, matchByTitle);
            PrintReport(report);
        });

        return compareCommand;
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

        Console.WriteLine($"Renamed/moved in Confluence: {report.RenamedOrMovedInConfluence.Count}");
        foreach (var page in report.RenamedOrMovedInConfluence)
            Console.WriteLine($"  ~ [{page.PageId}] {page.Title} | local: {page.LocalPath} -> confluence: {page.ConfluencePath}");

        Console.WriteLine($"Content changed: {report.ContentChanged.Count}");
        foreach (var page in report.ContentChanged)
            Console.WriteLine($"  * [{page.PageId}] {page.Title} ({page.Path})");

        if (report.Notes.Count > 0)
        {
            Console.WriteLine("Notes:");
            foreach (var note in report.Notes)
                Console.WriteLine($"  ! {note}");
        }
    }
}
