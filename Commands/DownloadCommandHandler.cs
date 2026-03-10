using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class DownloadCommandHandler
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppConfig _config;

    public DownloadCommandHandler(ILoggerFactory loggerFactory, AppConfig? config = null)
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
        var dryRunOption = CommandOptionsBuilder.CreateBoolOption("--dry-run", "Perform a dry run without writing files to disk");

        var pageIdOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-id", "ID of the page to download (mutually exclusive with --page-title)");
        var pageTitleOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-title", "Title of the page to download (mutually exclusive with --page-id)");
        var outputDirOption = CommandOptionsBuilder.CreateRequiredStringOption("--output-dir", "Output directory for downloaded pages");
        var recursiveOption = CommandOptionsBuilder.CreateBoolOption("--recursive", "Recursively download all child pages");
        var overwriteStrategyOption = CommandOptionsBuilder.CreateEnumOption("--overwrite-strategy", "How to handle existing files: 'skip', 'overwrite', or 'fail' (default)", "skip", "overwrite", "fail");
        baseUrlOption.Required = false;
        usernameOption.Required = false;
        tokenOption.Required = false;
        spaceKeyOption.Required = false;
        outputDirOption.Required = false;

        var downloadCommand = new Command("download", "Download Confluence pages to local files")
        {
            baseUrlOption,
            usernameOption,
            tokenOption,
            spaceKeyOption,
            pageIdOption,
            pageTitleOption,
            outputDirOption,
            recursiveOption,
            authTypeOption,
            dryRunOption,
            overwriteStrategyOption
        };

        downloadCommand.SetAction(async (parseResult) =>
        {
            var defaults = _config.Defaults;
            var baseUrl = CommandValueResolver.ResolveRequiredString(parseResult, baseUrlOption, defaults.BaseUrl, "--base-url");
            var username = CommandValueResolver.ResolveRequiredString(parseResult, usernameOption, defaults.Username, "--username");
            var token = CommandValueResolver.ResolveRequiredString(parseResult, tokenOption, defaults.Token, "--token");
            var spaceKey = CommandValueResolver.ResolveRequiredString(parseResult, spaceKeyOption, defaults.SpaceKey, "--space-key");
            var pageId = CommandValueResolver.ResolveOptionalString(parseResult, pageIdOption, defaults.Download.PageId);
            var pageTitle = CommandValueResolver.ResolveOptionalString(parseResult, pageTitleOption, defaults.Download.PageTitle);
            var outputDir = CommandValueResolver.ResolveRequiredString(parseResult, outputDirOption, defaults.Download.OutputDir, "--output-dir");
            var recursive = CommandValueResolver.ResolveBool(parseResult, recursiveOption, defaults.Download.Recursive ?? defaults.Recursive);
            var authType = CommandValueResolver.ResolveEnum(
                parseResult,
                authTypeOption,
                defaults.AuthType,
                "onprem",
                "--auth-type",
                "onprem",
                "cloud");
            var dryRun = CommandValueResolver.ResolveBool(parseResult, dryRunOption, defaults.DryRun);
            var overwriteStrategy = CommandValueResolver.ResolveEnum(
                parseResult,
                overwriteStrategyOption,
                defaults.Download.OverwriteStrategy,
                "fail",
                "--overwrite-strategy",
                "skip",
                "overwrite",
                "fail");

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
            var service = new DownloadService(
                apiClient,
                _loggerFactory.CreateLogger<DownloadService>(),
                dryRun);

            var pageIdentifier = !string.IsNullOrEmpty(pageId) ? $"ID '{pageId}'" : $"title '{pageTitle}'";
            Console.WriteLine($"Downloading page {pageIdentifier} from space '{spaceKey}'{(recursive ? " (recursive)" : "")}...");
            if (dryRun)
            {
                Console.WriteLine("DRY RUN MODE: No files will be written to disk.");
            }

            await CommandInvocationHelper.RunAsync(() => service.DownloadAsync(spaceKey, pageId, pageTitle, outputDir, recursive, overwriteStrategy));
            Console.WriteLine($"Download completed. Files saved to: {outputDir}");
        });

        return downloadCommand;
    }
}
