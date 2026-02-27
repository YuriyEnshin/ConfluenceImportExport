using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class UploadServiceTests
{
    [Fact]
    public async Task UploadUpdateAsync_ShouldThrow_WhenNoMatchingRootPageFound()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Root")).ReturnsAsync((string?)null);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldPreferExplicitPageId_ForRootResolution()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync("100");

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", "IgnoredTitle", recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null), Times.Once);
        api.Verify(x => x.FindPageByTitleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
        api.VerifyAll();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUseIdMarker_WhenExplicitParametersMissing()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>", pageId: "200");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("200")).ReturnsAsync(ApiClientMockFactory.CreatePage("200", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("200", "Root", "<p>content</p>", null)).ReturnsAsync("200");

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.TryGetPageByIdAsync("200"), Times.Once);
        api.VerifyAll();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUseFolderTitle_WhenIdMarkerNotFound()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootByTitle", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootByTitle")).ReturnsAsync("300");
        api.Setup(x => x.UpdatePageAsync("300", "RootByTitle", "<p>content</p>", null)).ReturnsAsync("300");

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.VerifyAll();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipChild_WhenParentMismatch_AndOnErrorSkip()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync("111");
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "another-parent"));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "skip");

        api.Verify(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null), Times.Once);
        api.Verify(x => x.UpdatePageAsync("222", It.IsAny<string>(), It.IsAny<string>(), null), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrowOnChildParentMismatch_WhenOnErrorAbort()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync("111");
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "another-parent"));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldCreateChild_WhenNotFoundUnderParent()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "ChildNew", "<p>child</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync("111");

        api.Setup(x => x.FindPageByTitleAsync("SPACE", "111", "ChildNew")).ReturnsAsync((string?)null);
        api.SetupSequence(x => x.FindPageByTitleAsync("SPACE", null, "ChildNew"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>")).ReturnsAsync("500");

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort");

        api.Verify(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>"), Times.Once);
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldResolveParentByTitle_AndCreateRootPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootToCreate", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "ParentTitle")).ReturnsAsync("P100");
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootToCreate")).ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "P100", "RootToCreate", "<p>content</p>")).ReturnsAsync("C100");

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadCreateAsync("SPACE", sourceDir, null, "ParentTitle", recursive: false);

        api.VerifyAll();
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldNotCreatePagesInDryRun()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootToCreate", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootToCreate")).ReturnsAsync((string?)null);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>(), dryRun: true);

        await service.UploadCreateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.CreatePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldReplaceAttachmentWithSameName()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("file.txt", "new data")]);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync("100");
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync([ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt")]);
        api.Setup(x => x.DeleteAttachmentAsync("100", "ATT-1")).ReturnsAsync(true);
        api.Setup(x => x.UploadAttachmentAsync("100", It.Is<string>(p => p.EndsWith("file.txt")), "file.txt")).ReturnsAsync(true);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.DeleteAttachmentAsync("100", "ATT-1"), Times.Once);
        api.Verify(x => x.UploadAttachmentAsync("100", It.IsAny<string>(), "file.txt"), Times.Once);
    }
}
