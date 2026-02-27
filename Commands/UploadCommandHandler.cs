using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class UploadCommandHandler
{
    private readonly ILogger<Exporter> _logger;

    public UploadCommandHandler(ILogger<Exporter> logger)
    {
        _logger = logger;
    }

    public Command CreateCommand()
    {
        var uploadCommand = new Command("upload", "Upload local pages to Confluence");
        uploadCommand.Add(CreateUpdateSubcommand());
        uploadCommand.Add(CreateCreateSubcommand());
        return uploadCommand;
    }

    private Command CreateUpdateSubcommand()
    {
        var baseUrlOption = CommandOptionsBuilder.CreateRequiredStringOption("--base-url", "Base URL of Confluence instance");
        var usernameOption = CommandOptionsBuilder.CreateRequiredStringOption("--username", "Username or email for authentication");
        var tokenOption = CommandOptionsBuilder.CreateRequiredStringOption("--token", "API token or password");
        var spaceKeyOption = CommandOptionsBuilder.CreateRequiredStringOption("--space-key", "Confluence space key");
        var authTypeOption = CommandOptionsBuilder.CreateEnumOption("--auth-type", "Authentication type: 'onprem' (default) or 'cloud'", "onprem", "cloud");
        var dryRunOption = CommandOptionsBuilder.CreateBoolOption("--dry-run", "Check for issues without modifying Confluence");
        var sourceDirOption = CommandOptionsBuilder.CreateRequiredStringOption("--source-dir", "Local page folder to upload");
        var recursiveOption = CommandOptionsBuilder.CreateBoolOption("--recursive", "Recursively update child pages");
        var pageIdOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-id", "Confluence page ID to update (mutually exclusive with --page-title)");
        var pageTitleOption = CommandOptionsBuilder.CreateOptionalStringOption("--page-title", "Confluence page title to update (mutually exclusive with --page-id)");
        var onErrorOption = CommandOptionsBuilder.CreateEnumOption("--on-error", "Behavior on conflict during recursive upload: 'abort' (default) or 'skip'", "abort", "skip");

        var command = new Command("update", "Update existing Confluence pages from local files")
        {
            baseUrlOption, usernameOption, tokenOption, spaceKeyOption, authTypeOption,
            dryRunOption, sourceDirOption, recursiveOption, pageIdOption, pageTitleOption, onErrorOption
        };

        command.SetAction(async (parseResult) =>
        {
            var baseUrl = parseResult.GetValue(baseUrlOption) ?? "";
            var username = parseResult.GetValue(usernameOption) ?? "";
            var token = parseResult.GetValue(tokenOption) ?? "";
            var spaceKey = parseResult.GetValue(spaceKeyOption) ?? "";
            var authType = parseResult.GetValue(authTypeOption) ?? "onprem";
            var dryRun = parseResult.GetValue(dryRunOption);
            var sourceDir = parseResult.GetValue(sourceDirOption) ?? "";
            var recursive = parseResult.GetValue(recursiveOption);
            var pageId = parseResult.GetValue(pageIdOption);
            var pageTitle = parseResult.GetValue(pageTitleOption);
            var onError = parseResult.GetValue(onErrorOption) ?? "abort";

            if (!string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(pageTitle))
            {
                Console.WriteLine("Error: --page-id and --page-title are mutually exclusive.");
                return;
            }

            var exporter = new Exporter(baseUrl, username, token, _logger, authType, dryRun);

            if (dryRun)
                Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

            var desc = recursive ? " (recursive)" : "";
            Console.WriteLine($"Updating pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

            await exporter.UploadUpdateAsync(spaceKey, sourceDir, pageId, pageTitle, recursive, onError);
            Console.WriteLine("Upload update completed.");
        });

        return command;
    }

    private Command CreateCreateSubcommand()
    {
        var baseUrlOption = CommandOptionsBuilder.CreateRequiredStringOption("--base-url", "Base URL of Confluence instance");
        var usernameOption = CommandOptionsBuilder.CreateRequiredStringOption("--username", "Username or email for authentication");
        var tokenOption = CommandOptionsBuilder.CreateRequiredStringOption("--token", "API token or password");
        var spaceKeyOption = CommandOptionsBuilder.CreateRequiredStringOption("--space-key", "Confluence space key");
        var authTypeOption = CommandOptionsBuilder.CreateEnumOption("--auth-type", "Authentication type: 'onprem' (default) or 'cloud'", "onprem", "cloud");
        var dryRunOption = CommandOptionsBuilder.CreateBoolOption("--dry-run", "Check for issues without modifying Confluence");
        var sourceDirOption = CommandOptionsBuilder.CreateRequiredStringOption("--source-dir", "Local page folder to upload");
        var recursiveOption = CommandOptionsBuilder.CreateBoolOption("--recursive", "Recursively create child pages");
        var parentIdOption = CommandOptionsBuilder.CreateOptionalStringOption("--parent-id", "Parent Confluence page ID (mutually exclusive with --parent-title)");
        var parentTitleOption = CommandOptionsBuilder.CreateOptionalStringOption("--parent-title", "Parent Confluence page title (mutually exclusive with --parent-id)");

        var command = new Command("create", "Create new Confluence pages from local files")
        {
            baseUrlOption, usernameOption, tokenOption, spaceKeyOption, authTypeOption,
            dryRunOption, sourceDirOption, recursiveOption, parentIdOption, parentTitleOption
        };

        command.SetAction(async (parseResult) =>
        {
            var baseUrl = parseResult.GetValue(baseUrlOption) ?? "";
            var username = parseResult.GetValue(usernameOption) ?? "";
            var token = parseResult.GetValue(tokenOption) ?? "";
            var spaceKey = parseResult.GetValue(spaceKeyOption) ?? "";
            var authType = parseResult.GetValue(authTypeOption) ?? "onprem";
            var dryRun = parseResult.GetValue(dryRunOption);
            var sourceDir = parseResult.GetValue(sourceDirOption) ?? "";
            var recursive = parseResult.GetValue(recursiveOption);
            var parentId = parseResult.GetValue(parentIdOption);
            var parentTitle = parseResult.GetValue(parentTitleOption);

            if (!string.IsNullOrEmpty(parentId) && !string.IsNullOrEmpty(parentTitle))
            {
                Console.WriteLine("Error: --parent-id and --parent-title are mutually exclusive.");
                return;
            }

            var exporter = new Exporter(baseUrl, username, token, _logger, authType, dryRun);

            if (dryRun)
                Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

            var parentDesc = !string.IsNullOrEmpty(parentId) ? $"under parent ID '{parentId}'"
                           : !string.IsNullOrEmpty(parentTitle) ? $"under parent '{parentTitle}'"
                           : "at space root";
            var desc = recursive ? " (recursive)" : "";
            Console.WriteLine($"Creating pages in space '{spaceKey}' {parentDesc} from '{sourceDir}'{desc}...");

            await exporter.UploadCreateAsync(spaceKey, sourceDir, parentId, parentTitle, recursive);
            Console.WriteLine("Upload create completed.");
        });

        return command;
    }
}
