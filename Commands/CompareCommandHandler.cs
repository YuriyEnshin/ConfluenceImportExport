using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class CompareCommandHandler
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppConfig _config;

    public CompareCommandHandler(ILoggerFactory loggerFactory, AppConfig? config = null)
    {
        _loggerFactory = loggerFactory;
        _config = config ?? new AppConfig();
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
        baseUrlOption.Required = false;
        usernameOption.Required = false;
        tokenOption.Required = false;
        spaceKeyOption.Required = false;
        outputDirOption.Required = false;

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
            var defaults = _config.Defaults;
            var compareDefaults = defaults.Compare;

            var baseUrl = CommandValueResolver.ResolveRequiredString(parseResult, baseUrlOption, defaults.BaseUrl, "--base-url");
            var username = CommandValueResolver.ResolveRequiredString(parseResult, usernameOption, defaults.Username, "--username");
            var token = CommandValueResolver.ResolveRequiredString(parseResult, tokenOption, defaults.Token, "--token");
            var spaceKey = CommandValueResolver.ResolveRequiredString(parseResult, spaceKeyOption, defaults.SpaceKey, "--space-key");
            var pageId = CommandValueResolver.ResolveOptionalString(parseResult, pageIdOption, compareDefaults.PageId);
            var pageTitle = CommandValueResolver.ResolveOptionalString(parseResult, pageTitleOption, compareDefaults.PageTitle);
            var outputDir = CommandValueResolver.ResolveRequiredString(parseResult, outputDirOption, compareDefaults.OutputDir, "--output-dir");
            var recursive = CommandValueResolver.ResolveBool(parseResult, recursiveOption, compareDefaults.Recursive ?? defaults.Recursive);
            var matchByTitle = CommandValueResolver.ResolveBool(parseResult, matchByTitleOption, compareDefaults.MatchByTitle);
            var authType = CommandValueResolver.ResolveEnum(
                parseResult,
                authTypeOption,
                defaults.AuthType,
                "onprem",
                "--auth-type",
                "onprem",
                "cloud");

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

            var apiClient = new HttpClientConfluenceApiClient(
                baseUrl, username, token,
                _loggerFactory.CreateLogger<HttpClientConfluenceApiClient>(),
                authType);
            var service = new CompareService(
                apiClient,
                _loggerFactory.CreateLogger<CompareService>());

            var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
            Console.WriteLine($"Comparing page {pageIdentifier} in space '{spaceKey}' with local folder '{outputDir}'{(recursive ? " (recursive)" : "")}...");

            var report = await CommandInvocationHelper.RunAsync(() => service.CompareAsync(spaceKey, pageId, pageTitle, outputDir, recursive, matchByTitle));
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
