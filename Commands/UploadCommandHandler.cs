using System.CommandLine;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Commands;

public class UploadCommandHandler
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly AppConfig _config;

    public UploadCommandHandler(ILoggerFactory loggerFactory, AppConfig? config = null)
    {
        _loggerFactory = loggerFactory;
        _config = config ?? new AppConfig();
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
        var movePagesOption = CommandOptionsBuilder.CreateBoolOption("--move-pages", "Move pages in Confluence when their local position in the tree differs from the remote one");
        baseUrlOption.Required = false;
        usernameOption.Required = false;
        tokenOption.Required = false;
        spaceKeyOption.Required = false;
        sourceDirOption.Required = false;

        var command = new Command("update", "Update existing Confluence pages from local files")
        {
            baseUrlOption, usernameOption, tokenOption, spaceKeyOption, authTypeOption,
            dryRunOption, sourceDirOption, recursiveOption, pageIdOption, pageTitleOption, onErrorOption, movePagesOption
        };

        command.SetAction(async (parseResult) =>
        {
            var defaults = _config.Defaults;
            var updateDefaults = defaults.Upload.Update;

            var baseUrl = CommandValueResolver.ResolveRequiredString(parseResult, baseUrlOption, defaults.BaseUrl, "--base-url");
            var username = CommandValueResolver.ResolveRequiredString(parseResult, usernameOption, defaults.Username, "--username");
            var token = CommandValueResolver.ResolveRequiredString(parseResult, tokenOption, defaults.Token, "--token");
            var spaceKey = CommandValueResolver.ResolveRequiredString(parseResult, spaceKeyOption, defaults.SpaceKey, "--space-key");
            var authType = CommandValueResolver.ResolveEnum(
                parseResult,
                authTypeOption,
                defaults.AuthType,
                "onprem",
                "--auth-type",
                "onprem",
                "cloud");
            var dryRun = CommandValueResolver.ResolveBool(parseResult, dryRunOption, defaults.DryRun);
            var sourceDir = CommandValueResolver.ResolveRequiredString(parseResult, sourceDirOption, updateDefaults.SourceDir, "--source-dir");
            var recursive = CommandValueResolver.ResolveBool(parseResult, recursiveOption, updateDefaults.Recursive ?? defaults.Recursive);
            var pageId = CommandValueResolver.ResolveOptionalString(parseResult, pageIdOption, updateDefaults.PageId);
            var pageTitle = CommandValueResolver.ResolveOptionalString(parseResult, pageTitleOption, updateDefaults.PageTitle);
            var onError = CommandValueResolver.ResolveEnum(
                parseResult,
                onErrorOption,
                updateDefaults.OnError,
                "abort",
                "--on-error",
                "abort",
                "skip");
            var movePages = CommandValueResolver.ResolveBool(parseResult, movePagesOption, updateDefaults.MovePages);

            if (!string.IsNullOrEmpty(pageId) && !string.IsNullOrEmpty(pageTitle))
            {
                Console.WriteLine("Error: --page-id and --page-title are mutually exclusive.");
                return;
            }

            var apiClient = new HttpClientConfluenceApiClient(
                baseUrl, username, token,
                _loggerFactory.CreateLogger<HttpClientConfluenceApiClient>(),
                authType);
            var service = new UploadService(
                apiClient,
                _loggerFactory.CreateLogger<UploadService>(),
                dryRun);

            if (dryRun)
                Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

            var desc = recursive ? " (recursive)" : "";
            Console.WriteLine($"Updating pages in space '{spaceKey}' from '{sourceDir}'{desc}...");

            await CommandInvocationHelper.RunAsync(() => service.UploadUpdateAsync(spaceKey, sourceDir, pageId, pageTitle, recursive, onError, movePages));
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
        baseUrlOption.Required = false;
        usernameOption.Required = false;
        tokenOption.Required = false;
        spaceKeyOption.Required = false;
        sourceDirOption.Required = false;

        var command = new Command("create", "Create new Confluence pages from local files")
        {
            baseUrlOption, usernameOption, tokenOption, spaceKeyOption, authTypeOption,
            dryRunOption, sourceDirOption, recursiveOption, parentIdOption, parentTitleOption
        };

        command.SetAction(async (parseResult) =>
        {
            var defaults = _config.Defaults;
            var createDefaults = defaults.Upload.Create;

            var baseUrl = CommandValueResolver.ResolveRequiredString(parseResult, baseUrlOption, defaults.BaseUrl, "--base-url");
            var username = CommandValueResolver.ResolveRequiredString(parseResult, usernameOption, defaults.Username, "--username");
            var token = CommandValueResolver.ResolveRequiredString(parseResult, tokenOption, defaults.Token, "--token");
            var spaceKey = CommandValueResolver.ResolveRequiredString(parseResult, spaceKeyOption, defaults.SpaceKey, "--space-key");
            var authType = CommandValueResolver.ResolveEnum(
                parseResult,
                authTypeOption,
                defaults.AuthType,
                "onprem",
                "--auth-type",
                "onprem",
                "cloud");
            var dryRun = CommandValueResolver.ResolveBool(parseResult, dryRunOption, defaults.DryRun);
            var sourceDir = CommandValueResolver.ResolveRequiredString(parseResult, sourceDirOption, createDefaults.SourceDir, "--source-dir");
            var recursive = CommandValueResolver.ResolveBool(parseResult, recursiveOption, createDefaults.Recursive ?? defaults.Recursive);
            var parentId = CommandValueResolver.ResolveOptionalString(parseResult, parentIdOption, createDefaults.ParentId);
            var parentTitle = CommandValueResolver.ResolveOptionalString(parseResult, parentTitleOption, createDefaults.ParentTitle);

            if (!string.IsNullOrEmpty(parentId) && !string.IsNullOrEmpty(parentTitle))
            {
                Console.WriteLine("Error: --parent-id and --parent-title are mutually exclusive.");
                return;
            }

            var apiClient = new HttpClientConfluenceApiClient(
                baseUrl, username, token,
                _loggerFactory.CreateLogger<HttpClientConfluenceApiClient>(),
                authType);
            var service = new UploadService(
                apiClient,
                _loggerFactory.CreateLogger<UploadService>(),
                dryRun);

            if (dryRun)
                Console.WriteLine("DRY RUN MODE: No changes will be made to Confluence.");

            var parentDesc = !string.IsNullOrEmpty(parentId) ? $"under parent ID '{parentId}'"
                           : !string.IsNullOrEmpty(parentTitle) ? $"under parent '{parentTitle}'"
                           : "at space root";
            var desc = recursive ? " (recursive)" : "";
            Console.WriteLine($"Creating pages in space '{spaceKey}' {parentDesc} from '{sourceDir}'{desc}...");

            await CommandInvocationHelper.RunAsync(() => service.UploadCreateAsync(spaceKey, sourceDir, parentId, parentTitle, recursive));
            Console.WriteLine("Upload create completed.");
        });

        return command;
    }
}
