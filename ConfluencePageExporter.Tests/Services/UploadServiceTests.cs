using ConfluencePageExporter.Models;
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
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", "IgnoredTitle", recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null), Times.Once);
        api.Verify(x => x.FindPageByTitleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUseIdMarker_WhenExplicitParametersMissing()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>", pageId: "200");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("200")).ReturnsAsync(ApiClientMockFactory.CreatePage("200", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("200")).ReturnsAsync(ApiClientMockFactory.CreatePage("200", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("200", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("200", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.TryGetPageByIdAsync("200"), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUseFolderTitle_WhenIdMarkerNotFound()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootByTitle", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootByTitle")).ReturnsAsync("300");
        api.Setup(x => x.GetPageByIdAsync("300")).ReturnsAsync(ApiClientMockFactory.CreatePage("300", "OldTitle", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("300", "RootByTitle", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("300", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.VerifyAll();
        LocalStorageHelper.ReadPageIdFromMarker(sourceDir).Should().Be("300");
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipChild_WhenParentMismatch_AndOnErrorSkip()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
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
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
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
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));

        api.Setup(x => x.FindPageByTitleAsync("SPACE", "111", "ChildNew")).ReturnsAsync((string?)null);
        api.SetupSequence(x => x.FindPageByTitleAsync("SPACE", null, "ChildNew"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>")).ReturnsAsync(new PageUpdateResult("500", 1));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort");

        api.Verify(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>"), Times.Once);
        var childDir = Path.Combine(rootDir, "ChildNew");
        LocalStorageHelper.ReadPageIdFromMarker(childDir).Should().Be("500");
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldResolveParentByTitle_AndCreateRootPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootToCreate", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "ParentTitle")).ReturnsAsync("P100");
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootToCreate")).ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "P100", "RootToCreate", "<p>content</p>")).ReturnsAsync(new PageUpdateResult("C100", 1));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadCreateAsync("SPACE", sourceDir, null, "ParentTitle", recursive: false);

        api.VerifyAll();
        LocalStorageHelper.ReadPageIdFromMarker(sourceDir).Should().Be("C100");
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
        LocalStorageHelper.ReadPageIdFromMarker(sourceDir).Should().BeNull();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUpdateAttachmentVersion_WhenContentChanged()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("file.txt", "new data")]);

        var oldRemoteContent = System.Text.Encoding.UTF8.GetBytes("old data");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: oldRemoteContent.Length)]);
        api.Setup(x => x.DownloadAttachmentAsync(It.IsAny<string>())).ReturnsAsync(oldRemoteContent);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("file.txt")), "file.txt")).ReturnsAsync(true);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "file.txt"), Times.Once);
        api.Verify(x => x.DeleteAttachmentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        api.Verify(x => x.UploadAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipUnchangedAttachment()
    {
        var localContent = "same data";
        var localBytes = System.Text.Encoding.UTF8.GetBytes(localContent);

        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("file.txt", localContent)]);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: localBytes.Length)]);
        api.Setup(x => x.DownloadAttachmentAsync(It.IsAny<string>())).ReturnsAsync(localBytes);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        api.Verify(x => x.UploadAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        api.Verify(x => x.DeleteAttachmentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUploadNewAttachment_WhenNotExistOnServer()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("new-file.txt", "data")]);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync([]);
        api.Setup(x => x.UploadAttachmentAsync("100", It.Is<string>(p => p.EndsWith("new-file.txt")), "new-file.txt")).ReturnsAsync(true);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UploadAttachmentAsync("100", It.IsAny<string>(), "new-file.txt"), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldDetectChangeByFileSize_WithoutDownloading()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("file.txt", "much longer new content here")]);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: 5)]);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("file.txt")), "file.txt")).ReturnsAsync(true);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "file.txt"), Times.Once);
        api.Verify(x => x.DownloadAttachmentAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveChild_WhenIdMarkerParentMismatch_AndMovePagesEnabled()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.GetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111")).ReturnsAsync(new PageUpdateResult("222", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort", movePages: true);

        api.Verify(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111"), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveChild_WhenFoundGloballyByTitle_AndMovePagesEnabled()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "MovedChild", "<p>child</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.FindPageByTitleAsync("SPACE", "111", "MovedChild")).ReturnsAsync((string?)null);
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "MovedChild")).ReturnsAsync("333");
        api.Setup(x => x.GetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "MovedChild", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("333", "MovedChild", "<p>child</p>", "111")).ReturnsAsync(new PageUpdateResult("333", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort", movePages: true);

        api.Verify(x => x.UpdatePageAsync("333", "MovedChild", "<p>child</p>", "111"), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldNotMoveChild_WhenMovePagesDisabled_AndOnErrorSkip()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "skip", movePages: false);

        api.Verify(x => x.UpdatePageAsync("222", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveAndRecurseIntoGrandchildren()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        var childDir = LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");
        LocalPageTreeBuilder.CreatePage(childDir, "Grandchild", "<p>gc</p>", "333");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null)).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.GetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111")).ReturnsAsync(new PageUpdateResult("222", 2));
        api.Setup(x => x.TryGetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Grandchild", "<p>x</p>", parentId: "222"));
        api.Setup(x => x.GetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Grandchild", "<p>x</p>", parentId: "222"));
        api.Setup(x => x.UpdatePageAsync("333", "Grandchild", "<p>gc</p>", null)).ReturnsAsync(new PageUpdateResult("333", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort", movePages: true);

        api.Verify(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111"), Times.Once);
        api.Verify(x => x.UpdatePageAsync("333", "Grandchild", "<p>gc</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldDryRunMove_WithoutActualUpdate()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Root")).ReturnsAsync((string?)null);
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Child")).ReturnsAsync((string?)null);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>(), dryRun: true);

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true, onError: "abort", movePages: true);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveRootPage_WhenParentIdMarkerDiffers_AndMovePagesEnabled()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>old</p>", parentId: "P1", parentTitle: "OldParent");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", "P2")).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false, movePages: true);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", "P2"), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrowOnRootParentMismatch_WhenMovePagesDisabled_AndOnErrorAbort()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>old</p>", parentId: "P1", parentTitle: "OldParent");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false, onError: "abort", movePages: false);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipRootMove_WhenParentDirHasNoIdMarker()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = temp.CreateDirectory("SomeParent");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>old</p>", parentId: "P1");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false, movePages: true);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldNotMoveRootPage_WhenServerParentMatchesLocalParent()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Parent", "<p>parent</p>", "P1");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage", "<p>old</p>", parentId: "P1", parentTitle: "Parent");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("400", "Subpage", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false, movePages: true);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage", "<p>content</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldLogAndSkipRootMove_WhenMovePagesDisabled_AndOnErrorSkip()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>old</p>", parentId: "P1");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false, onError: "skip", movePages: false);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipUpdate_WhenPageIsUnchanged()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>same content</p>", pageId: "100", version: 5);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>same content</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUpdate_WhenContentChanged()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>new content</p>", pageId: "100", version: 5);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>old content</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 6));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>new content</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUpdate_WhenTitleChanged()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewTitle", "<p>content</p>", pageId: "100", version: 3);

        var serverPage = ApiClientMockFactory.CreatePage("100", "OldTitle", "<p>content</p>", versionNumber: 3);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "NewTitle", "<p>content</p>", null)).ReturnsAsync(new PageUpdateResult("100", 4));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "NewTitle", "<p>content</p>", null), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUpdateMarkerVersion_AfterSuccessfulUpload()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>new</p>", pageId: "100", version: 5);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>old</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new</p>", null)).ReturnsAsync(new PageUpdateResult("100", 6));

        var service = new UploadService(api.Object, LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        var markerInfo = LocalStorageHelper.ReadPageMarkerInfo(sourceDir);
        markerInfo.Should().NotBeNull();
        markerInfo!.PageId.Should().Be("100");
        markerInfo.Version.Should().Be(6);
    }
}
