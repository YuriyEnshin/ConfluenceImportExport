using System.Net;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class UploadServiceTests
{
    [Fact]
    public async Task UploadUpdateAsync_ShouldRecordConflict_AndContinueBatch_When409OnOneChild()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>", pageId: "111");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child1", "<p>c1</p>", pageId: "222");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child2", "<p>c2</p>", pageId: "333");

        // Every page's server content differs from local, so an update is attempted for each.
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.TryGetPageByIdAsync("111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>server</p>"));
        api.Setup(x => x.GetPageByIdAsync("111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>server</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageUpdateResult("111", 2));

        api.Setup(x => x.TryGetPageByIdAsync("222", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child1", "<p>server</p>", parentId: "111"));
        api.Setup(x => x.GetPageByIdAsync("222", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child1", "<p>server</p>", parentId: "111"));
        api.Setup(x => x.UpdatePageAsync("222", "Child1", "<p>c1</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConfluenceConflictException("simulated 409"));

        api.Setup(x => x.TryGetPageByIdAsync("333", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Child2", "<p>server</p>", parentId: "111"));
        api.Setup(x => x.GetPageByIdAsync("333", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Child2", "<p>server</p>", parentId: "111"));
        api.Setup(x => x.UpdatePageAsync("333", "Child2", "<p>c2</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageUpdateResult("333", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true);

        // The 409 surfaces as a conflict in the report instead of being silently dropped...
        report.ConflictPages.Where(p => p.PageId == "222").ShouldHaveSingleItem();
        // ...and the sibling is still processed (the batch is not aborted).
        api.Verify(x => x.UpdatePageAsync("333", "Child2", "<p>c2</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldRethrow_OnAuthFailure()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>", pageId: "111");

        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.TryGetPageByIdAsync("111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>server</p>"));
        api.Setup(x => x.GetPageByIdAsync("111", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>server</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConfluenceApiException(HttpStatusCode.Forbidden, "no permission"));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: false);

        // Auth failures are global — the run aborts rather than recording per-page and continuing.
        await Should.ThrowAsync<ConfluenceApiException>(act);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrow_WhenNoMatchingRootPageFound()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Root")).ReturnsAsync((string?)null);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldPreferExplicitPageId_ForRootResolution()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", "IgnoredTitle", recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>()), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("200", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("200", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

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
        api.Setup(x => x.UpdatePageAsync("300", "RootByTitle", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("300", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.VerifyAll();
        PageMarker.Load(sourceDir).ShouldNotBeNull().PageId.ShouldBe("300");
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldAlwaysMoveChild_WhenParentMismatch()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.GetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("222", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true);

        api.Verify(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>()), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));

        api.Setup(x => x.FindPageByTitleAsync("SPACE", "111", "ChildNew")).ReturnsAsync((string?)null);
        api.SetupSequence(x => x.FindPageByTitleAsync("SPACE", null, "ChildNew"))
            .ReturnsAsync((string?)null)
            .ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>")).ReturnsAsync(new PageUpdateResult("500", 1));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true);

        api.Verify(x => x.CreatePageAsync("SPACE", "111", "ChildNew", "<p>child</p>"), Times.Once);
        var childDir = Path.Combine(rootDir, "ChildNew");
        PageMarker.Load(childDir).ShouldNotBeNull().PageId.ShouldBe("500");
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldResolveParentByTitle_AndCreateRootPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootToCreate", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "ParentTitle")).ReturnsAsync("P100");
        api.Setup(x => x.TryGetPageByIdAsync("P100")).ReturnsAsync(ApiClientMockFactory.CreatePage("P100", "ParentTitle", "<p>x</p>"));
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootToCreate")).ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("SPACE", "P100", "RootToCreate", "<p>content</p>")).ReturnsAsync(new PageUpdateResult("C100", 1));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadCreateAsync("SPACE", sourceDir, null, "ParentTitle", recursive: false);

        api.VerifyAll();
        PageMarker.Load(sourceDir).ShouldNotBeNull().PageId.ShouldBe("C100");
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldNotCreatePagesInDryRun()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "RootToCreate", "<p>content</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "RootToCreate")).ReturnsAsync((string?)null);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>(), dryRun: true);

        await service.UploadCreateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.CreatePageAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        PageMarker.Load(sourceDir).ShouldBeNull();
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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: oldRemoteContent.Length, mediaType: "text/plain")]);
        api.Setup(x => x.DownloadAttachmentAsync(It.IsAny<string>())).ReturnsAsync(oldRemoteContent);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("file.txt")), "file.txt", "text/plain")).ReturnsAsync(true);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        // The stored server media type is threaded through so Confluence keeps it
        // instead of re-inferring from the extension (the extensionless-twin bug).
        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "file.txt", "text/plain"), Times.Once);
        api.Verify(x => x.DeleteAttachmentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        api.Verify(x => x.UploadAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldUpdateExtensionlessAttachment_PreservingStoredMediaType()
    {
        // Regression: a draw.io diagram's source twin has no file extension. The
        // stored server media type must be sent on update, otherwise Confluence
        // re-infers it from the (missing) extension and refuses the data update —
        // leaving the source a version behind its updated .png preview.
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("diagram", "new diagram bytes")]);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Remote", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        // fileSize differs from the local file, so the change is detected without a download.
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "diagram", fileSize: 3, mediaType: "application/vnd.jgraph.mxfile")]);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("diagram")), "diagram", "application/vnd.jgraph.mxfile")).ReturnsAsync(true);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "diagram", "application/vnd.jgraph.mxfile"), Times.Once);
        api.Verify(x => x.UploadAttachmentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSyncChangedAttachment_WhenPageBodyUnchanged()
    {
        // Regression: an attachment-only change (e.g. a re-saved draw.io diagram)
        // must still be uploaded even though the page body/title/parent match the
        // server — previously the "unchanged" early-return skipped attachments.
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>content</p>",
            textAttachments: [("diagram", "new diagram bytes")]);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>content</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        // fileSize differs from the local file, so the change is detected without a download.
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "diagram", fileSize: 3, mediaType: "application/vnd.jgraph.mxfile")]);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("diagram")), "diagram", "application/vnd.jgraph.mxfile")).ReturnsAsync(true);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "diagram", "application/vnd.jgraph.mxfile"), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: localBytes.Length)]);
        api.Setup(x => x.DownloadAttachmentAsync(It.IsAny<string>())).ReturnsAsync(localBytes);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync([]);
        api.Setup(x => x.UploadAttachmentAsync("100", It.Is<string>(p => p.EndsWith("new-file.txt")), "new-file.txt")).ReturnsAsync(true);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "file.txt", fileSize: 5)]);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("file.txt")), "file.txt", It.IsAny<string>())).ReturnsAsync(true);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, "100", null, recursive: false);

        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "file.txt", It.IsAny<string>()), Times.Once);
        api.Verify(x => x.DownloadAttachmentAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveChild_WhenFoundGloballyByTitle()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>");
        LocalPageTreeBuilder.CreatePage(rootDir, "MovedChild", "<p>child</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.FindPageByTitleAsync("SPACE", "111", "MovedChild")).ReturnsAsync((string?)null);
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "MovedChild")).ReturnsAsync("333");
        api.Setup(x => x.GetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "MovedChild", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("333", "MovedChild", "<p>child</p>", "111", It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("333", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true);

        api.Verify(x => x.UpdatePageAsync("333", "MovedChild", "<p>child</p>", "111", It.IsAny<int?>()), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.GetPageByIdAsync("222")).ReturnsAsync(ApiClientMockFactory.CreatePage("222", "Child", "<p>x</p>", parentId: "old-parent"));
        api.Setup(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("222", 2));
        api.Setup(x => x.TryGetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Grandchild", "<p>x</p>", parentId: "222"));
        api.Setup(x => x.GetPageByIdAsync("333")).ReturnsAsync(ApiClientMockFactory.CreatePage("333", "Grandchild", "<p>x</p>", parentId: "222"));
        api.Setup(x => x.UpdatePageAsync("333", "Grandchild", "<p>gc</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("333", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", rootDir, "111", null, recursive: true);

        api.Verify(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>()), Times.Once);
        api.Verify(x => x.UpdatePageAsync("333", "Grandchild", "<p>gc</p>", null, It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldMoveRootPage_WhenParentIdMarkerDiffers()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>content</p>", "400");

        var serverPage = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>old</p>", parentId: "P1", parentTitle: "OldParent");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.TryGetPageByIdAsync("P2")).ReturnsAsync(ApiClientMockFactory.CreatePage("P2", "NewParent", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", "P2", It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", "P2", It.IsAny<int?>()), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>content</p>", null, It.IsAny<int?>()), Times.Once);
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

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 6));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>new content</p>", null, It.IsAny<int?>()), Times.Once);
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
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 6));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("SPACE", sourceDir, null, null, recursive: false);

        var marker = PageMarker.Load(sourceDir);
        marker.ShouldNotBeNull();
        marker!.PageId.ShouldBe("100");
        marker.Version.ShouldBe(6);
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldUploadLocallyChangedPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>local edit</p>", pageId: "100", version: 3);

        var indexPath = Path.Combine(sourceDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow);

        var markerPath = Directory.GetFiles(sourceDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow.AddHours(-1));

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>old</p>", versionNumber: 3);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>local edit</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 4));

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>local edit</p>", null, It.IsAny<int?>()), Times.Once);
        report.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldSkipServerChangedPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>old local</p>", pageId: "100", version: 2);

        var markerPath = Directory.GetFiles(sourceDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow);
        var indexPath = Path.Combine(sourceDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow.AddHours(-1));

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>server edit</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        report.SkippedPages.Count().ShouldBe(1);
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldSyncChangedAttachment_WhenPageBodyUnchanged()
    {
        // Symmetric to the update-path regression: with the page body unchanged,
        // merge must still push a changed attachment instead of returning early.
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(
            temp.RootPath,
            "Root",
            "<p>same</p>",
            pageId: "100",
            textAttachments: [("diagram", "new diagram bytes")],
            version: 2);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>same</p>", versionNumber: 2);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetAttachmentsAsync("100")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "diagram", fileSize: 3, mediaType: "application/vnd.jgraph.mxfile")]);
        api.Setup(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.Is<string>(p => p.EndsWith("diagram")), "diagram", "application/vnd.jgraph.mxfile")).ReturnsAsync(true);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        api.Verify(x => x.UpdateAttachmentDataAsync("100", "ATT-1", It.IsAny<string>(), "diagram", "application/vnd.jgraph.mxfile"), Times.Once);
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldMoveRootPage_WhenParentMarkerDiffersAndContentUnchanged()
    {
        // Локальный сценарий: пользователь перенёс папку Subpage4 под NewParent (P2).
        // Контент и заголовок страницы не менялись — должно произойти структурное
        // перемещение на сервере без перетирания контента.
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>same content</p>", "400", version: 5);

        var serverPage = ApiClientMockFactory.CreatePage(
            "400", "Subpage4", "<p>same content</p>", parentId: "P1", parentTitle: "OldParent", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.TryGetPageByIdAsync("P2")).ReturnsAsync(ApiClientMockFactory.CreatePage("P2", "NewParent", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>same content</p>", "P2", It.IsAny<int?>()))
            .ReturnsAsync(new PageUpdateResult("400", 6));

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>same content</p>", "P2", It.IsAny<int?>()), Times.Once);
        report.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldMoveAndUploadContent_WhenLocallyChangedAndMoved()
    {
        // Сценарий: пользователь перенёс папку И отредактировал контент.
        // Анализатор увидит совпадение версий маркера и сервера -> локальные правки.
        // Должен быть выполнен один UpdatePage с новым контентом и новым родителем.
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>local edit</p>", "400", version: 5);

        var indexPath = Path.Combine(sourceDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow);
        var markerPath = Directory.GetFiles(sourceDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow.AddHours(-1));

        var serverPage = ApiClientMockFactory.CreatePage(
            "400", "Subpage4", "<p>old server</p>", parentId: "P1", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.TryGetPageByIdAsync("P2")).ReturnsAsync(ApiClientMockFactory.CreatePage("P2", "NewParent", "<p>x</p>"));
        api.Setup(x => x.UpdatePageAsync("400", "Subpage4", "<p>local edit</p>", "P2", It.IsAny<int?>()))
            .ReturnsAsync(new PageUpdateResult("400", 6));

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync("400", "Subpage4", "<p>local edit</p>", "P2", It.IsAny<int?>()), Times.Once);
        report.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldDeferMove_WhenServerContentNewer()
    {
        // Сценарий: пользователь перенёс папку локально, но контент на сервере
        // изменился после последней синхронизации. Перемещение должно быть
        // отложено (страница попадает в Skipped с поясняющей подсказкой).
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewParent", "<p>parent</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Subpage4", "<p>old local</p>", "400", version: 2);

        var markerPath = Directory.GetFiles(sourceDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow);
        var indexPath = Path.Combine(sourceDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow.AddHours(-1));

        var serverPage = ApiClientMockFactory.CreatePage(
            "400", "Subpage4", "<p>server edit</p>", parentId: "P1", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.TryGetPageByIdAsync("P2")).ReturnsAsync(ApiClientMockFactory.CreatePage("P2", "NewParent", "<p>x</p>"));

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        report.SkippedPages.Count().ShouldBe(1);
        report.SkippedPages.First().Reason.ShouldContain("перемещение отложено");
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldMoveChild_WhenParentDiffersAndContentUnchanged()
    {
        // Рекурсивный сценарий: source-dir указывает на корень, дочерняя страница
        // перенесена локально под другого родителя. Контент не менялся —
        // должно произойти перемещение дочерней страницы на сервере.
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>", "111", version: 5);
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "222", version: 3);

        var rootPage = ApiClientMockFactory.CreatePage("111", "Root", "<p>root</p>", versionNumber: 5);
        var childPage = ApiClientMockFactory.CreatePage("222", "Child", "<p>child</p>", parentId: "old-parent", versionNumber: 3);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(rootPage);
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(rootPage);
        api.Setup(x => x.TryGetPageByIdAsync("222")).ReturnsAsync(childPage);
        api.Setup(x => x.GetPageByIdAsync("222")).ReturnsAsync(childPage);
        api.Setup(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>()))
            .ReturnsAsync(new PageUpdateResult("222", 4));

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", rootDir, "111", null, recursive: true, analyzer);

        api.Verify(x => x.UpdatePageAsync("222", "Child", "<p>child</p>", "111", It.IsAny<int?>()), Times.Once);
        report.HasIssues.ShouldBeFalse();
    }

    [Fact]
    public async Task UploadMergeAsync_ShouldWarnOnConflict()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>local edit</p>", pageId: "100", version: 2);

        var markerPath = Directory.GetFiles(sourceDir, ".id*").First();
        var syncTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(markerPath, syncTime);
        var indexPath = Path.Combine(sourceDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>server edit</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadMergeAsync("SPACE", sourceDir, null, null, recursive: false, analyzer);

        api.Verify(x => x.UpdatePageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        report.ConflictPages.Count().ShouldBe(1);
    }

    // ── multi-space: server-truth space flows down, cross-space is refused ──

    [Fact]
    public async Task UploadUpdateAsync_ShouldCreateNewChild_InRootServerSpace_NotConfigDefault()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>", "111");
        LocalPageTreeBuilder.CreatePage(rootDir, "NewChild", "<p>child</p>"); // no marker → created

        // Root actually lives in REAL; the configured default (CFG) must NOT be
        // used to place the new child — it inherits the root's server space.
        var rootServer = ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>", spaceKey: "REAL");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(rootServer);
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(rootServer);
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));
        api.Setup(x => x.FindPageByTitleAsync("REAL", "111", "NewChild")).ReturnsAsync((string?)null);
        api.Setup(x => x.FindPageByTitleAsync("REAL", null, "NewChild")).ReturnsAsync((string?)null);
        api.Setup(x => x.CreatePageAsync("REAL", "111", "NewChild", "<p>child</p>")).ReturnsAsync(new PageUpdateResult("500", 1));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("CFG", rootDir, "111", null, recursive: true);

        api.Verify(x => x.CreatePageAsync("REAL", "111", "NewChild", "<p>child</p>"), Times.Once);
        api.Verify(x => x.CreatePageAsync("CFG", It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldSkipCrossSpaceChild_AndNotProcessItsSubtree()
    {
        using var temp = new TempDirectoryScope();
        var rootDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>root</p>", "111");
        var childDir = LocalPageTreeBuilder.CreatePage(rootDir, "ForeignChild", "<p>child</p>", "222");
        LocalPageTreeBuilder.CreatePage(childDir, "Grandchild", "<p>gc</p>", "333");

        var rootServer = ApiClientMockFactory.CreatePage("111", "Root", "<p>x</p>", spaceKey: "REAL");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("111")).ReturnsAsync(rootServer);
        api.Setup(x => x.GetPageByIdAsync("111")).ReturnsAsync(rootServer);
        api.Setup(x => x.UpdatePageAsync("111", "Root", "<p>root</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("111", 2));
        // The child's marker resolves to a page in a DIFFERENT space.
        api.Setup(x => x.TryGetPageByIdAsync("222"))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("222", "ForeignChild", "<p>x</p>", parentId: "111", spaceKey: "OTHER"));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadUpdateAsync("CFG", rootDir, "111", null, recursive: true);

        // Cross-space page is reported and not written...
        report.SkippedPages.Where(p => p.PageId == "222").ShouldHaveSingleItem();
        api.Verify(x => x.UpdatePageAsync("222", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        // ...and its subtree is not descended into: the strict mock has NO setup
        // for grandchild "333", so any attempt to resolve it would throw.
        api.Verify(x => x.TryGetPageByIdAsync("333"), Times.Never);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldRefuseRootMove_WhenTargetParentInDifferentSpace()
    {
        using var temp = new TempDirectoryScope();
        var parentDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "ForeignParent", "<p>p</p>", "P2");
        var sourceDir = LocalPageTreeBuilder.CreatePage(parentDir, "Page", "<p>content</p>", "400");

        // Page is in REAL; the local parent folder maps to P2 which is in OTHER —
        // the cross-space move must be refused, but the content update proceeds.
        var serverPage = ApiClientMockFactory.CreatePage("400", "Page", "<p>old</p>", parentId: "P1", spaceKey: "REAL");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(serverPage);
        api.Setup(x => x.TryGetPageByIdAsync("P2"))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("P2", "ForeignParent", "<p>x</p>", spaceKey: "OTHER"));
        api.Setup(x => x.UpdatePageAsync("400", "Page", "<p>content</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("400", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadUpdateAsync("CFG", sourceDir, null, null, recursive: false);

        api.Verify(x => x.UpdatePageAsync("400", "Page", "<p>content</p>", null, It.IsAny<int?>()), Times.Once);
        api.Verify(x => x.UpdatePageAsync("400", It.IsAny<string>(), It.IsAny<string>(), "P2", It.IsAny<int?>()), Times.Never);
        report.SkippedPages.Where(p => p.PageId == "400").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldStampServerSpaceIntoMarker()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>new</p>", "100", version: 5);

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>old</p>", versionNumber: 5, spaceKey: "DOCS");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 6));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        await service.UploadUpdateAsync("CFG", sourceDir, null, null, recursive: false);

        PageMarker.Load(sourceDir).ShouldNotBeNull().SpaceKey.ShouldBe("DOCS");
    }

    // ── multi-tree mode + explicit-space conflict ──────────────────────────

    [Fact]
    public async Task UploadUpdateAsync_MultiTree_ShouldProcessEachTree_WithItsOwnSpace()
    {
        using var temp = new TempDirectoryScope();
        var t1 = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree1", "<p>t1</p>", "100");
        var t2 = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree2", "<p>t2</p>", "200");

        var p1 = ApiClientMockFactory.CreatePage("100", "Tree1", "<p>x</p>", spaceKey: "DEV");
        var p2 = ApiClientMockFactory.CreatePage("200", "Tree2", "<p>x</p>", spaceKey: "DOCS");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(p1);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(p1);
        api.Setup(x => x.UpdatePageAsync("100", "Tree1", "<p>t1</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));
        api.Setup(x => x.TryGetPageByIdAsync("200")).ReturnsAsync(p2);
        api.Setup(x => x.GetPageByIdAsync("200")).ReturnsAsync(p2);
        api.Setup(x => x.UpdatePageAsync("200", "Tree2", "<p>t2</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("200", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        // sourceDir is a container of two trees from different spaces.
        await service.UploadUpdateAsync("CFG", temp.RootPath, null, null, recursive: false, multiTree: true);

        api.Verify(x => x.UpdatePageAsync("100", "Tree1", "<p>t1</p>", null, It.IsAny<int?>()), Times.Once);
        api.Verify(x => x.UpdatePageAsync("200", "Tree2", "<p>t2</p>", null, It.IsAny<int?>()), Times.Once);
        PageMarker.Load(t1).ShouldNotBeNull().SpaceKey.ShouldBe("DEV");
        PageMarker.Load(t2).ShouldNotBeNull().SpaceKey.ShouldBe("DOCS");
    }

    [Fact]
    public async Task UploadUpdateAsync_MultiTree_ShouldIsolateTreeFailures()
    {
        using var temp = new TempDirectoryScope();
        var t1 = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree1", "<p>t1</p>", "100");
        LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree2", "<p>t2</p>", "999");

        var p1 = ApiClientMockFactory.CreatePage("100", "Tree1", "<p>x</p>", spaceKey: "DEV");
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.TryGetPageByIdAsync("100", It.IsAny<CancellationToken>())).ReturnsAsync(p1);
        api.Setup(x => x.GetPageByIdAsync("100", It.IsAny<CancellationToken>())).ReturnsAsync(p1);
        api.Setup(x => x.UpdatePageAsync("100", "Tree1", "<p>t1</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PageUpdateResult("100", 2));
        // Tree2's page is gone and not found by title → that one tree fails…
        api.Setup(x => x.TryGetPageByIdAsync("999", It.IsAny<CancellationToken>())).ReturnsAsync((PageData?)null);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var report = await service.UploadUpdateAsync("CFG", temp.RootPath, null, null, recursive: false, multiTree: true);

        // …without aborting the rest: Tree1 is still updated, Tree2 is reported.
        api.Verify(x => x.UpdatePageAsync("100", "Tree1", "<p>t1</p>", null, It.IsAny<int?>(), It.IsAny<CancellationToken>()), Times.Once);
        report.SkippedPages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrow_WhenContainerWithoutMultiTree()
    {
        using var temp = new TempDirectoryScope();
        LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree1", "<p>t1</p>", "100");

        var api = ApiClientMockFactory.CreateStrict();
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("CFG", temp.RootPath, null, null, recursive: false);

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("multiTree");
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrow_WhenMultiTreeCombinedWithPageId()
    {
        using var temp = new TempDirectoryScope();
        LocalPageTreeBuilder.CreatePage(temp.RootPath, "Tree1", "<p>t1</p>", "100");

        var api = ApiClientMockFactory.CreateStrict();
        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadUpdateAsync("CFG", temp.RootPath, "100", null, recursive: false, multiTree: true);

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("pageId");
    }

    [Fact]
    public async Task UploadUpdateAsync_MultiTreeIgnored_WhenSourceDirIsItselfAPage()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>new</p>", "100");

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>old</p>", spaceKey: "DEV");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.UpdatePageAsync("100", "Root", "<p>new</p>", null, It.IsAny<int?>())).ReturnsAsync(new PageUpdateResult("100", 2));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        // multiTree=true but sourceDir is itself a page folder → single-tree path.
        await service.UploadUpdateAsync("CFG", sourceDir, null, null, recursive: false, multiTree: true);

        api.Verify(x => x.UpdatePageAsync("100", "Root", "<p>new</p>", null, It.IsAny<int?>()), Times.Once);
    }

    [Fact]
    public async Task UploadUpdateAsync_ShouldThrow_WhenExplicitSpaceConflictsWithServer()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "Root", "<p>x</p>", "100");

        var serverPage = ApiClientMockFactory.CreatePage("100", "Root", "<p>x</p>", spaceKey: "REAL");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("100")).ReturnsAsync(serverPage);
        api.Setup(x => x.GetPageByIdAsync("100")).ReturnsAsync(serverPage);

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        // An explicitly requested space that contradicts the page's real space errors.
        var act = () => service.UploadUpdateAsync("REAL", sourceDir, null, null, recursive: false, explicitSpaceKey: "WRONG");

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("WRONG");
    }

    [Fact]
    public async Task UploadCreateAsync_ShouldThrow_WhenExplicitSpaceConflictsWithParent()
    {
        using var temp = new TempDirectoryScope();
        var sourceDir = LocalPageTreeBuilder.CreatePage(temp.RootPath, "NewRoot", "<p>x</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.TryGetPageByIdAsync("P1")).ReturnsAsync(ApiClientMockFactory.CreatePage("P1", "Parent", "<p>x</p>", spaceKey: "REAL"));

        var service = new UploadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<UploadService>());

        var act = () => service.UploadCreateAsync("REAL", sourceDir, "P1", null, recursive: false, explicitSpaceKey: "WRONG");

        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("WRONG");
    }
}
