namespace ConfluencePageExporter.Options;

public sealed class UploadCreateOptions
{
    public string? SourceDir { get; set; }
    public string? ParentId { get; set; }
    public string? ParentTitle { get; set; }
    public bool? Recursive { get; set; }
}
