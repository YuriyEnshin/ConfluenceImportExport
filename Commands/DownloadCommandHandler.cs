using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class DownloadCommandHandler
{
    private readonly ILoggerFactory _loggerFactory;

    public DownloadCommandHandler(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
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
            var baseUrl = parseResult.GetValue(baseUrlOption) ?? "";
            var username = parseResult.GetValue(usernameOption) ?? "";
            var token = parseResult.GetValue(tokenOption) ?? "";
            var spaceKey = parseResult.GetValue(spaceKeyOption) ?? "";
            var pageId = parseResult.GetValue(pageIdOption);
            var pageTitle = parseResult.GetValue(pageTitleOption);
            var outputDir = parseResult.GetValue(outputDirOption) ?? "";
            var recursive = parseResult.GetValue(recursiveOption);
            var authType = parseResult.GetValue(authTypeOption) ?? "onprem";
            var dryRun = parseResult.GetValue(dryRunOption);
            var overwriteStrategy = parseResult.GetValue(overwriteStrategyOption) ?? "fail";

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

            await service.DownloadAsync(spaceKey, pageId, pageTitle, outputDir, recursive, overwriteStrategy);
            Console.WriteLine($"Download completed. Files saved to: {outputDir}");
        });

        return downloadCommand;
    }
}
