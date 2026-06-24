namespace ConfluencePageExporter.Models;

public class CompareReport
{
    public List<ComparePageInfo> AddedInConfluence { get; } = new();
    public List<ComparePageInfo> DeletedInConfluence { get; } = new();
    public List<CompareRenamedOrMovedPageInfo> RenamedOrMovedInConfluence { get; } = new();
    public List<CompareContentChangedPageInfo> ContentChanged { get; } = new();
    public List<CompareContentChangedPageInfo> Conflicts { get; } = new();
    public List<CompareAttachmentsChangedPageInfo> AttachmentsChanged { get; } = new();
    public List<string> Notes { get; } = new();
    public bool DetectSourceEnabled { get; set; }
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
    public ChangeSourceInfo? RenameSource { get; set; }
    public ChangeSourceInfo? MoveSource { get; set; }
}

public class CompareContentChangedPageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public ChangeSourceInfo? ChangeSource { get; set; }
}

public class CompareAttachmentsChangedPageInfo
{
    public string PageId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public List<CompareAttachmentDiff> Differences { get; set; } = new();
}

/// <summary>
/// One attachment-level difference found by <c>compare</c>. Detection is
/// "cheap": by file name presence and byte size only — no content download — so
/// a same-size in-place edit is not flagged.
/// </summary>
public sealed record CompareAttachmentDiff(string FileName, AttachmentDiffKind Kind);

public enum AttachmentDiffKind
{
    /// <summary>Present locally, absent on the server (would be uploaded).</summary>
    OnlyLocal,
    /// <summary>Present on the server, absent locally.</summary>
    OnlyRemote,
    /// <summary>Present on both sides but the byte size differs.</summary>
    SizeDiffers
}
