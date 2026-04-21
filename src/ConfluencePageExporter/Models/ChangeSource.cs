namespace ConfluencePageExporter.Models;

public enum ChangeOrigin
{
    Unknown,
    Local,
    Server,
    Conflict
}

public enum ChangeConfidence
{
    Low,
    Medium,
    High
}

public sealed record ChangeSourceInfo(
    ChangeOrigin Origin,
    ChangeConfidence Confidence,
    string Reason);
