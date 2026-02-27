namespace ConfluencePageExporter.Models;

public class CompareReport
{
    public List<ComparePageInfo> AddedInConfluence { get; } = new();
    public List<ComparePageInfo> DeletedInConfluence { get; } = new();
    public List<CompareRenamedOrMovedPageInfo> RenamedOrMovedInConfluence { get; } = new();
    public List<CompareContentChangedPageInfo> ContentChanged { get; } = new();
    public List<string> Notes { get; } = new();
}

public class ComparePageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public class CompareRenamedOrMovedPageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public string ConfluencePath { get; set; } = string.Empty;
}

public class CompareContentChangedPageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}
