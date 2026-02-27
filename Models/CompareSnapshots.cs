namespace ConfluencePageExporter.Models;

public sealed record RemotePageSnapshot(
    string PageId,
    string Title,
    string RelativePath,
    string Content);

public sealed record LocalComparisonSnapshot(
    Dictionary<string, LocalPageSnapshot> PagesById,
    Dictionary<string, LocalPathSnapshot> PagesByPath);

public sealed record LocalPageSnapshot(
    string PageId,
    string Title,
    string DirectoryPath,
    string RelativePath);

public sealed record LocalPathSnapshot(
    string RelativePath,
    string Title,
    string DirectoryPath,
    string? PageIdFromMarker);
