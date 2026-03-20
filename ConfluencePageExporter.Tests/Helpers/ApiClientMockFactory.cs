using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using Moq;

namespace ConfluencePageExporter.Tests.Helpers;

public static class ApiClientMockFactory
{
    public static Mock<IConfluenceApiClient> CreateLoose() => new(MockBehavior.Loose);

    public static Mock<IConfluenceApiClient> CreateStrict() => new(MockBehavior.Strict);

    public static PageData CreatePage(
        string id,
        string title,
        string content,
        string? parentId = null,
        string? parentTitle = null,
        int versionNumber = 1)
    {
        return new PageData
        {
            Id = id,
            Title = title,
            Body = new Body
            {
                Storage = new StorageContent
                {
                    Value = content,
                    Representation = "storage"
                }
            },
            Version = new VersionInfo { Number = versionNumber },
            Ancestors = parentId == null
                ? []
                : [new PageAncestor { Id = parentId, Title = parentTitle ?? $"parent-{parentId}" }]
        };
    }

    public static PageUpdateResult CreateUpdateResult(string id, int versionNumber)
    {
        return new PageUpdateResult(id, versionNumber);
    }

    public static AttachmentData CreateAttachment(
        string id,
        string title,
        string downloadUrl = "/download/mock",
        long? fileSize = null)
    {
        return new AttachmentData
        {
            Id = id,
            Title = title,
            Extensions = fileSize.HasValue ? new AttachmentExtensions { FileSize = fileSize.Value } : null,
            Links = new AttachmentLinks
            {
                DownloadUrl = downloadUrl
            }
        };
    }
}
