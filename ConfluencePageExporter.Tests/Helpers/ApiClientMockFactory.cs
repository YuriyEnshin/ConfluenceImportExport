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
        string? parentTitle = null)
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
            Ancestors = parentId == null
                ? []
                : [new PageAncestor { Id = parentId, Title = parentTitle ?? $"parent-{parentId}" }]
        };
    }

    public static AttachmentData CreateAttachment(
        string id,
        string title,
        string downloadUrl = "/download/mock")
    {
        return new AttachmentData
        {
            Id = id,
            Title = title,
            Links = new AttachmentLinks
            {
                DownloadUrl = downloadUrl
            }
        };
    }
}
