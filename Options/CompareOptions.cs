namespace ConfluencePageExporter.Options;

public sealed class CompareOptions
{
    public string? PageId { get; set; }
    public string? PageTitle { get; set; }
    public string? OutputDir { get; set; }
    public bool? Recursive { get; set; }
    public bool? MatchByTitle { get; set; }
    public bool? DetectSource { get; set; }
}
