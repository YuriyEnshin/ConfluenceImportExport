namespace ConfluencePageExporter.Options;

public sealed class DownloadOptions
{
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public string? OutputDir { get; set; }
    public bool? Recursive { get; set; }
    public string? OverwriteStrategy { get; set; }
}
