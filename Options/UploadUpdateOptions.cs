namespace ConfluencePageExporter.Options;

public sealed class UploadUpdateOptions
{
    public string? SourceDir { get; set; }
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public bool? Recursive { get; set; }
}
