using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Options;

namespace ConfluencePageExporter.Commands;

public sealed class ConfigShowCommandHandler : ICommandHandler
{
    private const string EnvPrefix = "CONFLUENCE_EXPORTER__";

    private readonly IConfiguration _configuration;
    private readonly IOptions<GlobalOptions> _global;
    private readonly IOptions<DownloadOptions> _download;
    private readonly IOptions<UploadUpdateOptions> _uploadUpdate;
    private readonly IOptions<UploadCreateOptions> _uploadCreate;
    private readonly IOptions<CompareOptions> _compare;
    private readonly IReadOnlyDictionary<string, string?> _cliOverrides;

    public ConfigShowCommandHandler(
        IConfiguration configuration,
        IOptions<GlobalOptions> global,
        IOptions<DownloadOptions> download,
        IOptions<UploadUpdateOptions> uploadUpdate,
        IOptions<UploadCreateOptions> uploadCreate,
        IOptions<CompareOptions> compare,
        IReadOnlyDictionary<string, string?> cliOverrides)
    {
        _configuration = configuration;
        _global = global;
        _download = download;
        _uploadUpdate = uploadUpdate;
        _uploadCreate = uploadCreate;
        _compare = compare;
        _cliOverrides = cliOverrides;
    }

    public Task<int> ExecuteAsync(CancellationToken ct)
    {
        Console.WriteLine("Effective configuration");
        Console.WriteLine("=======================");
        Console.WriteLine();

        PrintGlobal();
        PrintDownload();
        PrintUploadUpdate();
        PrintUploadCreate();
        PrintCompare();

        return Task.FromResult(0);
    }

    private void PrintGlobal()
    {
        var g = _global.Value;
        Console.WriteLine("Global:");
        PrintValue("  BaseUrl", g.BaseUrl, "Global:BaseUrl");
        PrintValue("  Username", g.Username, "Global:Username");
        PrintValue("  Token", Mask(g.Token), "Global:Token");
        PrintValue("  SpaceKey", g.SpaceKey, "Global:SpaceKey");
        PrintValue("  AuthType", g.AuthType ?? "onprem", "Global:AuthType");
        PrintValue("  Verbose", (g.Verbose ?? false).ToString(), "Global:Verbose");
        PrintValue("  DryRun", (g.DryRun ?? false).ToString(), "Global:DryRun");
        PrintValue("  Recursive", (g.Recursive ?? false).ToString(), "Global:Recursive");
        PrintValue("  Report", (g.Report ?? false).ToString(), "Global:Report");
        Console.WriteLine();
    }

    private void PrintDownload()
    {
        var d = _download.Value;
        Console.WriteLine("Download:");
        PrintValue("  PageId", d.PageId, "Download:PageId");
        PrintValue("  PageTitle", d.PageTitle, "Download:PageTitle");
        PrintValue("  OutputDir", d.OutputDir, "Download:OutputDir");
        PrintValue("  Recursive", d.Recursive?.ToString(), "Download:Recursive");
        Console.WriteLine();
    }

    private void PrintUploadUpdate()
    {
        var u = _uploadUpdate.Value;
        Console.WriteLine("Upload > Update:");
        PrintValue("  SourceDir", u.SourceDir, "Upload:Update:SourceDir");
        PrintValue("  PageId", u.PageId, "Upload:Update:PageId");
        PrintValue("  PageTitle", u.PageTitle, "Upload:Update:PageTitle");
        PrintValue("  Recursive", u.Recursive?.ToString(), "Upload:Update:Recursive");
        Console.WriteLine();
    }

    private void PrintUploadCreate()
    {
        var c = _uploadCreate.Value;
        Console.WriteLine("Upload > Create:");
        PrintValue("  SourceDir", c.SourceDir, "Upload:Create:SourceDir");
        PrintValue("  ParentId", c.ParentId, "Upload:Create:ParentId");
        PrintValue("  ParentTitle", c.ParentTitle, "Upload:Create:ParentTitle");
        PrintValue("  Recursive", c.Recursive?.ToString(), "Upload:Create:Recursive");
        Console.WriteLine();
    }

    private void PrintCompare()
    {
        var c = _compare.Value;
        Console.WriteLine("Compare:");
        PrintValue("  PageId", c.PageId, "Compare:PageId");
        PrintValue("  PageTitle", c.PageTitle, "Compare:PageTitle");
        PrintValue("  OutputDir", c.OutputDir, "Compare:OutputDir");
        PrintValue("  Recursive", c.Recursive?.ToString(), "Compare:Recursive");
        PrintValue("  MatchByTitle", (c.MatchByTitle ?? false).ToString(), "Compare:MatchByTitle");
        Console.WriteLine();
    }

    private void PrintValue(string label, string? value, string configKey)
    {
        var display = string.IsNullOrEmpty(value) ? "(not set)" : value;
        var source = DetectSource(configKey, value);
        Console.WriteLine($"{label,-28} = {display,-36} {source}");
    }

    private string DetectSource(string configKey, string? effectiveValue)
    {
        if (_cliOverrides.ContainsKey(configKey))
            return "[CLI]";

        var envKey = EnvPrefix + configKey.Replace(":", "__", StringComparison.Ordinal).ToUpperInvariant();
        if (Environment.GetEnvironmentVariable(envKey) != null)
            return "[ENV]";

        if (!string.IsNullOrEmpty(effectiveValue) && _configuration[configKey] != null)
            return "[FILE]";

        return "[DEFAULT]";
    }

    private static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.Length <= 4)
            return "***";
        return string.Concat(value.AsSpan(0, 2), "***", value.AsSpan(value.Length - 2));
    }
}
