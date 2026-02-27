using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class DownloadServiceTests
{
    [Fact]
    public async Task DownloadAsync_ShouldThrow_WhenOutputDirectoryIsNonEmpty_AndStrategyFail()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        File.WriteAllText(Path.Combine(outputDir, "existing.txt"), "x");

        var api = ApiClientMockFactory.CreateStrict();
        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        var act = () => service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "fail");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DownloadAsync_ShouldReturnWithoutDownloading_WhenOutputDirectoryIsNonEmpty_AndStrategySkip()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        File.WriteAllText(Path.Combine(outputDir, "existing.txt"), "x");

        var api = ApiClientMockFactory.CreateStrict();
        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "skip");

        api.Verify(x => x.GetPageByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DownloadAsync_ShouldDownloadSinglePageAndAttachments()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "file.txt", "/download/file.txt")
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/file.txt")).ReturnsAsync([1, 2, 3]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "overwrite");

        var pageDir = Path.Combine(outputDir, "Root");
        File.Exists(Path.Combine(pageDir, "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(pageDir, ".id1")).Should().BeTrue();
        File.Exists(Path.Combine(pageDir, "file.txt")).Should().BeTrue();
        api.Verify(x => x.GetChildrenPagesAsync(It.IsAny<string>()), Times.Never);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadAsync_ShouldDownloadChildPages_WhenRecursiveEnabled()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>child</p>", "1", "Root");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetAttachmentsAsync("2")).ReturnsAsync([]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: true, overwriteStrategy: "overwrite");

        File.Exists(Path.Combine(outputDir, "Root", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "Root", "Child", "index.html")).Should().BeTrue();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadAsync_ShouldNotWriteFiles_WhenDryRun()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>(), dryRun: true);

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "overwrite");

        Directory.Exists(Path.Combine(outputDir, "Root")).Should().BeFalse();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadAsync_ShouldMoveDirectory_WhenSamePageIdExistsAtOldLocation()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var oldDir = LocalPageTreeBuilder.CreatePage(outputDir, "OldTitle", "<p>old</p>", "1");

        var page = ApiClientMockFactory.CreatePage("1", "NewTitle", "<p>new</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "overwrite");

        Directory.Exists(oldDir).Should().BeFalse();
        var newDir = Path.Combine(outputDir, "NewTitle");
        Directory.Exists(newDir).Should().BeTrue();
        File.Exists(Path.Combine(newDir, ".id1")).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadAsync_ShouldKeepIndexTimestamp_WhenContentIsUnchanged()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>same</p>", "1");
        var indexPath = Path.Combine(pageDir, "index.html");
        var expectedTimestamp = new DateTime(2024, 1, 1, 1, 1, 1, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(indexPath, expectedTimestamp);

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>same</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "overwrite");

        File.GetLastWriteTimeUtc(indexPath).Should().Be(expectedTimestamp);
    }

    [Fact]
    public async Task DownloadAsync_ShouldContinue_WhenAttachmentDownloadFails()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "bad.txt", "/download/bad"),
            ApiClientMockFactory.CreateAttachment("a2", "good.txt", "/download/good")
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/bad")).ThrowsAsync(new HttpRequestException("boom"));
        api.Setup(x => x.DownloadAttachmentAsync("/download/good")).ReturnsAsync([7, 8]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadAsync("SPACE", "1", null, outputDir, recursive: false, overwriteStrategy: "overwrite");

        File.Exists(Path.Combine(outputDir, "Root", "good.txt")).Should().BeTrue();
    }
}
