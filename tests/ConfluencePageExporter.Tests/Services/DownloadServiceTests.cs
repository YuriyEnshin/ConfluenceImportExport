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
    public async Task DownloadUpdateAsync_ShouldSkipAttachment_WhenLocalFileSizeMatchesServer()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var existingContent = new byte[] { 1, 2, 3 };
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        await File.WriteAllBytesAsync(Path.Combine(pageDir, "file.txt"), existingContent, TestContext.Current.CancellationToken);

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "file.txt", "/download/file.txt", fileSize: 3)
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        api.Verify(x => x.DownloadAttachmentAsync(It.IsAny<string>()), Times.Never);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldRedownloadAttachment_WhenLocalFileSizeDiffers()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var oldContent = new byte[] { 1, 2 };
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        await File.WriteAllBytesAsync(Path.Combine(pageDir, "file.txt"), oldContent, TestContext.Current.CancellationToken);

        var newContent = new byte[] { 1, 2, 3, 4, 5 };
        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "file.txt", "/download/file.txt", fileSize: 5)
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/file.txt")).ReturnsAsync(newContent);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var downloaded = await File.ReadAllBytesAsync(Path.Combine(pageDir, "file.txt"), TestContext.Current.CancellationToken);
        downloaded.Should().Equal(newContent);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldSkipRewrite_WhenApiFileSizeMismatchButContentIdentical()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var actualContent = new byte[] { 10, 20, 30, 40, 50 };
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        var filePath = Path.Combine(pageDir, "image.jpg");
        await File.WriteAllBytesAsync(filePath, actualContent, TestContext.Current.CancellationToken);
        var originalTimestamp = new DateTime(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(filePath, originalTimestamp);

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "image.jpg", "/download/image.jpg", fileSize: 3)
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/image.jpg")).ReturnsAsync(actualContent);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.GetLastWriteTimeUtc(filePath).Should().Be(originalTimestamp);
        (await File.ReadAllBytesAsync(filePath, TestContext.Current.CancellationToken)).Should().Equal(actualContent);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldDownloadAttachment_WhenServerFileSizeNotAvailable()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var existingContent = new byte[] { 1, 2, 3 };
        var pageDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        await File.WriteAllBytesAsync(Path.Combine(pageDir, "file.txt"), existingContent, TestContext.Current.CancellationToken);

        var serverContent = new byte[] { 4, 5, 6 };
        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "file.txt", "/download/file.txt", fileSize: null)
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/file.txt")).ReturnsAsync(serverContent);

        var service = new DownloadService(api.Object, LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var downloaded = await File.ReadAllBytesAsync(Path.Combine(pageDir, "file.txt"), TestContext.Current.CancellationToken);
        downloaded.Should().Equal(serverContent);
        api.VerifyAll();
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

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
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

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
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

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
        content.Should().Be("<p>local edit</p>");
        report.ConflictPages.Should().HaveCount(1);
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldDownloadAllSiblings_UnderParallelism()
    {
        // Регрессионный: параллельный обход 5 сиблингов (upper bound для реалистичного
        // recursive-сценария) не должен терять страницы, путать пути или ломать SyncReport.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var children = Enumerable.Range(1, 5)
            .Select(i => ApiClientMockFactory.CreatePage($"ch{i}", $"Child{i}", $"<p>child {i}</p>", "1", "Root"))
            .ToList();

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync(children);
        foreach (var ch in children)
        {
            api.Setup(x => x.GetAttachmentsAsync(ch.Id)).ReturnsAsync([]);
            api.Setup(x => x.GetChildrenPagesAsync(ch.Id)).ReturnsAsync([]);
        }

        var service = new DownloadService(
            api.Object,
            LoggerTestHelper.CreateLogger<DownloadService>(),
            maxParallelism: 8);

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        foreach (var ch in children)
        {
            var childDir = Path.Combine(outputDir, "Root", ch.Title);
            File.Exists(Path.Combine(childDir, "index.html")).Should().BeTrue();
            LocalStorageHelper.ReadPageIdFromMarker(childDir).Should().Be(ch.Id);
        }

        foreach (var ch in children)
            api.Verify(x => x.GetAttachmentsAsync(ch.Id), Times.Once);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadMergeAsync_ShouldCollectAllSkipReasons_UnderParallelism()
    {
        // Параллельный обход не должен терять записи в SyncReport (ConcurrentBag).
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        // Версии маркера и сервера совпадают → ChangeOrigin.Local → AddSkipped.
        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", versionNumber: 3);
        var children = Enumerable.Range(1, 5)
            .Select(i => ApiClientMockFactory.CreatePage($"ch{i}", $"Child{i}", $"<p>server {i}</p>", "1", "Root", versionNumber: 3))
            .ToList();

        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1", version: 3);
        var rootLocalDir = Path.Combine(outputDir, "Root");
        foreach (var i in Enumerable.Range(1, 5))
        {
            var childDir = LocalPageTreeBuilder.CreatePage(rootLocalDir, $"Child{i}", $"<p>local {i}</p>", $"ch{i}", version: 3);
            File.SetLastWriteTimeUtc(Path.Combine(childDir, "index.html"), DateTime.UtcNow);
            var markerPath = Directory.GetFiles(childDir, ".id*").First();
            File.SetLastWriteTimeUtc(markerPath, DateTime.UtcNow.AddHours(-1));
        }

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync(children);
        foreach (var ch in children)
        {
            api.Setup(x => x.GetAttachmentsAsync(ch.Id)).ReturnsAsync([]);
            api.Setup(x => x.GetChildrenPagesAsync(ch.Id)).ReturnsAsync([]);
        }

        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        var service = new DownloadService(
            api.Object,
            LoggerTestHelper.CreateLogger<DownloadService>(),
            maxParallelism: 8);

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: true, analyzer);

        report.SkippedPages.Should().HaveCount(5);
        report.SkippedPages.Select(x => x.PageId).Should().BeEquivalentTo(
            new[] { "ch1", "ch2", "ch3", "ch4", "ch5" });
    }
}
