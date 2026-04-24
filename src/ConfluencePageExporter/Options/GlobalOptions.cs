namespace ConfluencePageExporter.Options;

public sealed class GlobalOptions
{
    public string? BaseUrl { get; set; }
    public string? Username { get; set; }
    public string? Token { get; set; }
    public string? SpaceKey { get; set; }
    public string? AuthType { get; set; }
    public bool? Verbose { get; set; }
    public bool? DryRun { get; set; }
    public bool? Recursive { get; set; }
    public bool? Report { get; set; }

    /// <summary>
    /// Maximum parallelism for tree-walking operations (download merge/update,
    /// upload merge/update/create). Default is 8; set to 1 to disable parallelism.
    /// </summary>
    public int? MaxParallelism { get; set; }
}
