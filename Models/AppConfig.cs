namespace ConfluencePageExporter.Models;

public sealed class AppConfig
{
    public DefaultConfig Defaults { get; set; } = new();
}

public sealed class DefaultConfig
{
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? Token { get; set; }
    public string? SpaceKey { get; set; }
    public string? AuthType { get; set; }
    public bool? Recursive { get; set; }
    public bool? DryRun { get; set; }
    public DownloadDefaults Download { get; set; } = new();
    public UploadDefaults Upload { get; set; } = new();
    public CompareDefaults Compare { get; set; } = new();
}

public sealed class DownloadDefaults
{
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public string? OutputDir { get; set; }
    public bool? Recursive { get; set; }
    public string? OverwriteStrategy { get; set; }
}

public sealed class UploadDefaults
{
    public UploadUpdateDefaults Update { get; set; } = new();
    public UploadCreateDefaults Create { get; set; } = new();
}

public sealed class CompareDefaults
{
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public string? OutputDir { get; set; }
    public bool? Recursive { get; set; }
    public bool? MatchByTitle { get; set; }
}

public sealed class UploadUpdateDefaults
{
    public string? SourceDir { get; set; }
    public bool? Recursive { get; set; }
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public string? OnError { get; set; }
    public bool? MovePages { get; set; }
}

public sealed class UploadCreateDefaults
{
    public string? SourceDir { get; set; }
    public bool? Recursive { get; set; }
    public string? ParentId { get; set; }
    public string? ParentTitle { get; set; }
}
