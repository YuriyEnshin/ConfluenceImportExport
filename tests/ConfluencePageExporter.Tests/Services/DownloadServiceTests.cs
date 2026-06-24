using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class DownloadServiceTests
{
    [Fact]
    public async Task DownloadUpdateAsync_ShouldPropagateCancellation_ToApiClient()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        // The mock observes the token it is handed and throws if cancelled,
        // proving the service threads the caller's token into the API call.
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.FromResult(ApiClientMockFactory.CreatePage("1", "Root", "<p/>"));
            });

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false, cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }

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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var pageDir = Path.Combine(outputDir, "Root");
        File.Exists(Path.Combine(pageDir, "index.html")).ShouldBeTrue();
        PageMarker.Load(pageDir).ShouldNotBeNull().PageId.ShouldBe("1");
        File.Exists(Path.Combine(pageDir, "file.txt")).ShouldBeTrue();
        api.Verify(x => x.GetChildrenPagesAsync(It.IsAny<string>()), Times.Never);
        report.HasIssues.ShouldBeFalse();
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldStampAttachmentBaseline_InMarker()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var attachments = new List<AttachmentData>
        {
            ApiClientMockFactory.CreateAttachment("a1", "file.txt", "/download/file.txt", version: 4)
        };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(attachments);
        api.Setup(x => x.DownloadAttachmentAsync("/download/file.txt")).ReturnsAsync([1, 2, 3]);

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var marker = PageMarker.Load(Path.Combine(outputDir, "Root"));
        marker.ShouldNotBeNull();
        marker!.Attachments.ShouldNotBeNull();
        var baseline = marker.Attachments!["file.txt"];
        baseline.ServerName.ShouldBe("file.txt");
        baseline.Version.ShouldBe(4);
        baseline.Size.ShouldBe(3);
        baseline.Hash.ShouldNotBeNull();
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        File.Exists(Path.Combine(outputDir, "Root", "index.html")).ShouldBeTrue();
        File.Exists(Path.Combine(outputDir, "Root", "Child", "index.html")).ShouldBeTrue();
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>(), dryRun: true);

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        Directory.Exists(Path.Combine(outputDir, "Root")).ShouldBeFalse();
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        Directory.Exists(oldDir).ShouldBeFalse();
        var newDir = Path.Combine(outputDir, "NewTitle");
        Directory.Exists(newDir).ShouldBeTrue();
        PageMarker.Load(newDir).ShouldNotBeNull().PageId.ShouldBe("1");
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.GetLastWriteTimeUtc(indexPath).ShouldBe(expectedTimestamp);
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.Exists(Path.Combine(outputDir, "Root", "good.txt")).ShouldBeTrue();
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var downloaded = await File.ReadAllBytesAsync(Path.Combine(pageDir, "file.txt"), TestContext.Current.CancellationToken);
        downloaded.ShouldBe(newContent);
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        File.GetLastWriteTimeUtc(filePath).ShouldBe(originalTimestamp);
        (await File.ReadAllBytesAsync(filePath, TestContext.Current.CancellationToken)).ShouldBe(actualContent);
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

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: false);

        var downloaded = await File.ReadAllBytesAsync(Path.Combine(pageDir, "file.txt"), TestContext.Current.CancellationToken);
        downloaded.ShouldBe(serverContent);
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
        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
        content.ShouldBe("<p>local edit</p>");
        report.SkippedPages.Count().ShouldBe(1);
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
        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
        content.ShouldBe("<p>new server</p>");
        report.HasIssues.ShouldBeFalse();
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
        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: false, analyzer);

        var content = await File.ReadAllTextAsync(indexPath, TestContext.Current.CancellationToken);
        content.ShouldBe("<p>local edit</p>");
        report.ConflictPages.Count().ShouldBe(1);
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
            new XmlContentNormalizer(),
            LoggerTestHelper.CreateLogger<DownloadService>(),
            maxParallelism: 8);

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        foreach (var ch in children)
        {
            var childDir = Path.Combine(outputDir, "Root", ch.Title);
            File.Exists(Path.Combine(childDir, "index.html")).ShouldBeTrue();
            PageMarker.Load(childDir).ShouldNotBeNull().PageId.ShouldBe(ch.Id);
        }

        foreach (var ch in children)
            api.Verify(x => x.GetAttachmentsAsync(ch.Id), Times.Once);
        api.VerifyAll();
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldSkipAttachmentsAndChildrenApi_WhenChildTypesFlagsAreFalse()
    {
        // Оптимизация: для листьев (childTypes.page.value=false, childTypes.attachment.value=false)
        // запросы /child/attachment и /child/page не должны делаться — strict mock без setup
        // зафейлит тест, если мы всё-таки их вызовем.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage(
            "1", "Leaf", "<p>leaf</p>",
            hasPages: false, hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        File.Exists(Path.Combine(outputDir, "Leaf", "index.html")).ShouldBeTrue();
        api.Verify(x => x.GetAttachmentsAsync(It.IsAny<string>()), Times.Never);
        api.Verify(x => x.GetChildrenPagesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldStillCallChildrenApi_WhenChildTypesIsNull()
    {
        // Backward-compat: если сервер не вернул childTypes (старая версия API
        // или expand не поддержан), должны fallback к старому поведению и запросить.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("SPACE", "1", null, outputDir, recursive: true);

        api.Verify(x => x.GetAttachmentsAsync("1"), Times.Once);
        api.Verify(x => x.GetChildrenPagesAsync("1"), Times.Once);
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
            new XmlContentNormalizer(),
            LoggerTestHelper.CreateLogger<DownloadService>(),
            maxParallelism: 8);

        var report = await service.DownloadMergeAsync("SPACE", "1", null, outputDir, recursive: true, analyzer);

        report.SkippedPages.Count().ShouldBe(5);
        report.SkippedPages.Select(x => x.PageId).ShouldBe(
            new[] { "ch1", "ch2", "ch3", "ch4", "ch5" }, ignoreOrder: true);
    }

    // ── space capture into markers ────────────────────────────────────────

    [Fact]
    public async Task DownloadUpdateAsync_ShouldStampServerSpaceIntoMarker()
    {
        using var temp = new TempDirectoryScope();

        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageByIdAsync("100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage(
                "100", "Root", "<p>x</p>", spaceKey: "DOCS", hasPages: false, hasAttachments: false));

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        // Configured/request space is CFG, but the page actually lives in DOCS —
        // the server value is the authority and is what gets persisted.
        await service.DownloadUpdateAsync("CFG", "100", null, temp.RootPath, recursive: false);

        PageMarker.Load(Path.Combine(temp.RootPath, "Root")).ShouldNotBeNull().SpaceKey.ShouldBe("DOCS");
    }

    [Fact]
    public async Task DownloadUpdateAsync_ShouldStampRootSpaceIntoChildMarkers_WhenChildrenLackSpace()
    {
        using var temp = new TempDirectoryScope();

        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageByIdAsync("100", It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("100", "Root", "<p>x</p>", spaceKey: "DOCS", hasAttachments: false));
        // Children come from the list endpoint, which is not expanded with space —
        // they must inherit the root's space, not end up space-less.
        api.Setup(x => x.GetChildrenPagesAsync("100", It.IsAny<CancellationToken>()))
            .ReturnsAsync([ApiClientMockFactory.CreatePage("200", "Child", "<p>c</p>", parentId: "100", hasPages: false, hasAttachments: false)]);

        var service = new DownloadService(api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<DownloadService>());

        await service.DownloadUpdateAsync("CFG", "100", null, temp.RootPath, recursive: true);

        PageMarker.Load(Path.Combine(temp.RootPath, "Root", "Child")).ShouldNotBeNull().SpaceKey.ShouldBe("DOCS");
    }
}
