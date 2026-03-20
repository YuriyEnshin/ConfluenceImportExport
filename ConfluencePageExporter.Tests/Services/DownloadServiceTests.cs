using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class DownloadServiceTests
{
    [Fact]
    public async Task DownloadUpdateAsync_ShouldDownloadSinglePageAndAttachments()
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

        var report = await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var pageDir = Path.Combine(outputDir, "Root");
        File.Exists(Path.Combine(pageDir, "index.html")).Should().BeTrue();
        LocalStorageHelper.ReadPageIdFromMarker(pageDir).Should().Be("1");
        File.Exists(Path.Combine(pageDir, "file.txt")).Should().BeTrue();
        api.Verify(x => x.GetChildrenPagesAsync(It.IsAny<string>()), Times.Never);
        report.HasIssues.Should().BeFalse();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldDownloadChildPages_WhenRecursiveEnabled()
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

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        File.Exists(Path.Combine(outputDir, "Root", "index.html")).Should().BeTrue();
        File.Exists(Path.Combine(outputDir, "Root", "Child", "index.html")).Should().BeTrue();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldNotWriteFiles_WhenDryRun()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>(), dryRun: true);

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        Directory.Exists(Path.Combine(outputDir, "Root")).Should().BeFalse();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldMoveDirectory_WhenSamePageIdExistsAtOldLocation()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var oldDir = LocalPageTreeBuilder.CreatePage(outputDir, "OldTitle", "<p>old</p>", "1");

        var page = ApiClientMockFactory.CreatePage("1", "NewTitle", "<p>new</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        Directory.Exists(oldDir).Should().BeFalse();
        var newDir = Path.Combine(outputDir, "NewTitle");
        Directory.Exists(newDir).Should().BeTrue();
        LocalStorageHelper.ReadPageIdFromMarker(newDir).Should().Be("1");
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldKeepIndexTimestamp_WhenContentIsUnchanged()
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

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.GetLastWriteTimeUtc(indexPath).Should().Be(expectedTimestamp);
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldContinue_WhenAttachmentDownloadFails()
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

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.Exists(Path.Combine(outputDir, "Root", "good.txt")).Should().BeTrue();
    }

    [Fact]
    public async Task DownloadMergeAsync_ShouldSkipLocallyChangedPage()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>local edit</p>", "1", version: 3);

        var indexPath = Path.Combine(pageDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow);

        var markerPath = Directory.GetFiles(pageDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow.AddHours(-1));

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>server content</p>", versionNumber: 3);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath);
        content.Should().Be("<p>local edit</p>");
        report.SkippedPages.Should().HaveCount(1);
    }

    [Fact]
    public async Task DownloadMergeAsync_ShouldOverwriteServerChangedPage()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>old</p>", "1", version: 2);

        var markerPath = Directory.GetFiles(pageDir, ".id*").First();
        File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow);
        var indexPath = Path.Combine(pageDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow.AddHours(-1));

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>new server</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath);
        content.Should().Be("<p>new server</p>");
        report.HasIssues.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadMergeAsync_ShouldWarnOnConflict()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>local edit</p>", "1", version: 2);

        var markerPath = Directory.GetFiles(pageDir, ".id*").First();
        var syncTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(markerPath, syncTime);

        var indexPath = Path.Combine(pageDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow);

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>server edit</p>", versionNumber: 5);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath);
        content.Should().Be("<p>local edit</p>");
        report.ConflictPages.Should().HaveCount(1);
    }
}
