using System.CommandLine;
using System.CommandLine.Parsing;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Converts explicitly-specified CLI options into IConfiguration key-value pairs.
/// Walks the command result parent chain so recursive options are found
/// regardless of where the user places them in the command line.
/// </summary>
public static class CliOverrideBuilder
{
    public static IReadOnlyDictionary<string, string?> Build(ParseResult parseResult, string commandPath)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        AddIfExplicit(parseResult, "verbose", "Global:Verbose", result, isFlag: true);
        AddIfExplicit(parseResult, "base-url", "Global:BaseUrl", result);
        AddIfExplicit(parseResult, "username", "Global:Username", result);
        AddIfExplicit(parseResult, "token", "Global:Token", result);
        AddIfExplicit(parseResult, "space-key", "Global:SpaceKey", result);
        AddIfExplicit(parseResult, "auth-type", "Global:AuthType", result);
        AddIfExplicit(parseResult, "dry-run", "Global:DryRun", result, isFlag: true);

        switch (commandPath)
        {
            case "download":
                MapDownload(parseResult, result);
                break;
            case "upload update":
                MapUploadUpdate(parseResult, result);
                break;
            case "upload create":
                MapUploadCreate(parseResult, result);
                break;
            case "compare":
                MapCompare(parseResult, result);
                break;
            case "config show":
                MapConfigShow(parseResult, result);
                break;
        }

        return result;
    }

    private static void MapDownload(ParseResult pr, Dictionary<string, string?> d)
    {
        AddIfExplicit(pr, "page-id", "Download:PageId", d);
        AddIfExplicit(pr, "page-title", "Download:PageTitle", d);
        AddIfExplicitPath(pr, "output-dir", "Download:OutputDir", d);
        AddIfExplicit(pr, "recursive", "Download:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "overwrite-strategy", "Download:OverwriteStrategy", d);
    }

    private static void MapUploadUpdate(ParseResult pr, Dictionary<string, string?> d)
    {
        AddIfExplicitPath(pr, "source-dir", "Upload:Update:SourceDir", d);
        AddIfExplicit(pr, "page-id", "Upload:Update:PageId", d);
        AddIfExplicit(pr, "page-title", "Upload:Update:PageTitle", d);
        AddIfExplicit(pr, "recursive", "Upload:Update:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "on-error", "Upload:Update:OnError", d);
        AddIfExplicit(pr, "move-pages", "Upload:Update:MovePages", d, isFlag: true);
    }

    private static void MapUploadCreate(ParseResult pr, Dictionary<string, string?> d)
    {
        AddIfExplicitPath(pr, "source-dir", "Upload:Create:SourceDir", d);
        AddIfExplicit(pr, "parent-id", "Upload:Create:ParentId", d);
        AddIfExplicit(pr, "parent-title", "Upload:Create:ParentTitle", d);
        AddIfExplicit(pr, "recursive", "Upload:Create:Recursive", d, isFlag: true);
    }

    private static void MapCompare(ParseResult pr, Dictionary<string, string?> d)
    {
        AddIfExplicit(pr, "page-id", "Compare:PageId", d);
        AddIfExplicit(pr, "page-title", "Compare:PageTitle", d);
        AddIfExplicitPath(pr, "output-dir", "Compare:OutputDir", d);
        AddIfExplicit(pr, "recursive", "Compare:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "match-by-title", "Compare:MatchByTitle", d, isFlag: true);
    }

    private static void MapConfigShow(ParseResult pr, Dictionary<string, string?> d)
    {
        AddIfExplicit(pr, "page-id", "Download:PageId", d);
        AddIfExplicit(pr, "page-title", "Download:PageTitle", d);
        AddIfExplicitPath(pr, "output-dir", "Download:OutputDir", d);
        AddIfExplicit(pr, "recursive", "Download:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "overwrite-strategy", "Download:OverwriteStrategy", d);

        AddIfExplicitPath(pr, "source-dir", "Upload:Update:SourceDir", d);
        AddIfExplicit(pr, "page-id", "Upload:Update:PageId", d);
        AddIfExplicit(pr, "page-title", "Upload:Update:PageTitle", d);
        AddIfExplicit(pr, "recursive", "Upload:Update:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "on-error", "Upload:Update:OnError", d);
        AddIfExplicit(pr, "move-pages", "Upload:Update:MovePages", d, isFlag: true);

        AddIfExplicitPath(pr, "source-dir", "Upload:Create:SourceDir", d);
        AddIfExplicit(pr, "parent-id", "Upload:Create:ParentId", d);
        AddIfExplicit(pr, "parent-title", "Upload:Create:ParentTitle", d);
        AddIfExplicit(pr, "recursive", "Upload:Create:Recursive", d, isFlag: true);

        AddIfExplicit(pr, "page-id", "Compare:PageId", d);
        AddIfExplicit(pr, "page-title", "Compare:PageTitle", d);
        AddIfExplicitPath(pr, "output-dir", "Compare:OutputDir", d);
        AddIfExplicit(pr, "recursive", "Compare:Recursive", d, isFlag: true);
        AddIfExplicit(pr, "match-by-title", "Compare:MatchByTitle", d, isFlag: true);
    }

    private static void AddIfExplicit(
        ParseResult parseResult,
        string optionName,
        string configKey,
        Dictionary<string, string?> dict,
        bool isFlag = false)
    {
        var optionResult = FindOptionResult(parseResult, optionName);
        if (optionResult == null || optionResult.Implicit)
            return;

        if (isFlag)
        {
            dict[configKey] = "True";
        }
        else
        {
            var token = optionResult.Tokens.FirstOrDefault();
            if (token != null)
                dict[configKey] = token.Value;
        }
    }

    private static void AddIfExplicitPath(
        ParseResult parseResult,
        string optionName,
        string configKey,
        Dictionary<string, string?> dict)
    {
        var optionResult = FindOptionResult(parseResult, optionName);
        if (optionResult == null || optionResult.Implicit)
            return;

        var token = optionResult.Tokens.FirstOrDefault();
        if (token != null)
            dict[configKey] = PathNormalizer.Normalize(token.Value);
    }

    private static OptionResult? FindOptionResult(ParseResult parseResult, string optionName)
    {
        var fullName = "--" + optionName;

        var cmd = parseResult.CommandResult;
        while (cmd != null)
        {
            var match = cmd.Children
                .OfType<OptionResult>()
                .FirstOrDefault(r => r.Option.Name == fullName);
            if (match != null)
                return match;
            cmd = cmd.Parent as CommandResult;
        }

        return null;
    }
}
