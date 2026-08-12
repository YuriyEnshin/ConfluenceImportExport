using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class CompareServiceTests
{
    private static CompareService CreateService(Mock<IConfluenceApiClient> api)
    {
        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        return new CompareService(api.Object, analyzer, new XmlContentNormalizer(), LoggerTestHelper.CreateLogger<CompareService>());
    }

    [Fact]
    public async Task CompareAsync_ShouldThrow_WhenRootPageCannotBeResolved()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Root")).ReturnsAsync((string?)null);
        var service = CreateService(api);

        var act = () => service.CompareAsync("SPACE", null, "Root", outputDir, recursive: false);

        await Should.ThrowAsync<InvalidOperationException>(act);
    }

    [Fact]
    public async Task CompareAsync_ShouldReportAddedInConfluence_WhenRemoteChildIsMissingLocally()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var localRoot = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        _ = localRoot;

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>child</p>", "1", "Root", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AddedInConfluence.Where(x => x.PageId == "2").ShouldHaveSingleItem();
        report.DeletedInConfluence.ShouldBeEmpty();
        report.RenamedOrMovedInConfluence.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportDeletedInConfluence_WhenLocalChildIsMissingRemotely()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.DeletedInConfluence.Where(x => x.PageId == "2").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportRenamedOrMoved_WhenSameIdHasDifferentPath()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "OldName", "<p>same</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "NewName", "<p>same</p>", "1", "Root", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.RenamedOrMovedInConfluence.Where(x => x.PageId == "2").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportContentChanged_WhenContentDiffers()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>local</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>remote</p>", "1", "Root", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.ContentChanged.Where(x => x.PageId == "2").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportAttachmentDifferences_ByNameAndSize()
    {
        // Cheap attachment detection: size mismatch, only-local and only-remote
        // are all flagged for a page whose body is otherwise identical.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>root</p>", "1",
            textAttachments: [("diagram", "new diagram bytes"), ("local-only.txt", "x")]);

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: true);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(
        [
            ApiClientMockFactory.CreateAttachment("ATT-1", "diagram", fileSize: 3),
            ApiClientMockFactory.CreateAttachment("ATT-2", "server-only.bin", fileSize: 10)
        ]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.ContentChanged.ShouldBeEmpty();
        var page = report.AttachmentsChanged.ShouldHaveSingleItem();
        page.PageId.ShouldBe("1");
        page.Differences.Count.ShouldBe(3);
        page.Differences.ShouldContain(d => d.FileName == "diagram" && d.Kind == AttachmentDiffKind.SizeDiffers);
        page.Differences.ShouldContain(d => d.FileName == "server-only.bin" && d.Kind == AttachmentDiffKind.OnlyRemote);
        page.Differences.ShouldContain(d => d.FileName == "local-only.txt" && d.Kind == AttachmentDiffKind.OnlyLocal);
    }

    [Fact]
    public async Task CompareAsync_ShouldNoteFailedAttachmentListing_InsteadOfReportingNoDifferences()
    {
        // A page whose attachment listing cannot be fetched is *unknown*, not
        // "identical": say so in Notes rather than silently comparing nothing.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>root</p>", "1",
            textAttachments: [("diagram", "bytes")]);

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: true);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetAttachmentsAsync("1"))
            .ThrowsAsync(new ConfluenceApiException(System.Net.HttpStatusCode.InternalServerError, "listing exploded"));

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AttachmentsChanged.ShouldBeEmpty();
        report.Notes.ShouldContain(n => n.Contains("listing exploded") && n.Contains("could not be compared"));
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportAttachments_WhenSizesMatch_AndNotDownload()
    {
        // Same-size attachments are treated as unchanged in cheap mode; no
        // content download is attempted (the strict mock has none configured).
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        const string content = "12345";
        LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>root</p>", "1",
            textAttachments: [("diagram", content)]);

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: true);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "diagram", fileSize: content.Length)]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AttachmentsChanged.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldNotFetchAttachments_WhenNoneLocallyOrOnServer()
    {
        // Optimisation guard: a page with no local attachment files and no
        // server-reported attachments must not trigger a GetAttachmentsAsync call.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AttachmentsChanged.ShouldBeEmpty();
        api.Verify(x => x.GetAttachmentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CompareAsync_ShouldReportAttachmentOrigin_FromBaseline()
    {
        // With a marker baseline, compare classifies the change source per
        // attachment (still download-free): local / server / conflict.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>root</p>", "1",
            textAttachments: [("loc", "LOCAL EDIT"), ("srv", "OLD"), ("cfl", "LOCAL EDIT")]);

        var oldHash = AttachmentHasher.ComputeHash(System.Text.Encoding.UTF8.GetBytes("OLD"));
        await PageMarker.WriteAsync(rootDir, "1", 5, "Root", "SPACE", null, null, new Dictionary<string, AttachmentBaseline>
        {
            ["loc"] = new AttachmentBaseline("loc", 5, oldHash, 3), // local changed (≠OLD), server v5 → Local
            ["srv"] = new AttachmentBaseline("srv", 5, oldHash, 3), // local == OLD, server v6 → Server
            ["cfl"] = new AttachmentBaseline("cfl", 5, oldHash, 3), // local changed + server v6 → Conflict
        });

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: true);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(
        [
            ApiClientMockFactory.CreateAttachment("a-loc", "loc", version: 5),
            ApiClientMockFactory.CreateAttachment("a-srv", "srv", version: 6),
            ApiClientMockFactory.CreateAttachment("a-cfl", "cfl", version: 6),
        ]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        var page = report.AttachmentsChanged.ShouldHaveSingleItem();
        page.Differences.Count.ShouldBe(3);
        page.Differences.ShouldContain(d => d.FileName == "loc" && d.Kind == AttachmentDiffKind.ChangedLocal);
        page.Differences.ShouldContain(d => d.FileName == "srv" && d.Kind == AttachmentDiffKind.ChangedServer);
        page.Differences.ShouldContain(d => d.FileName == "cfl" && d.Kind == AttachmentDiffKind.ChangedBoth);
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportContentChanged_WhenOnlyLineEndingsDiffer()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>Hello</p>\r\n<p>World</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>Hello</p>\n<p>World</p>", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.ContentChanged.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportAdded_WhenTitleFallbackMatchesWithoutId()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false, matchByTitleWhenNoId: true);

        report.AddedInConfluence.ShouldBeEmpty();
        report.Notes.ShouldContain(n => n.Contains("matched by title/folder name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompareAsync_ShouldIgnoreLocalChildren_WhenRecursiveIsDisabled()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.DeletedInConfluence.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldPopulateChangeSource_ForContentDifferences()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>local</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>remote</p>", "1", "Root", hasAttachments: false);
        child.Version = new VersionInfo { Number = 3, When = DateTime.UtcNow.AddDays(1) };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.ContentChanged.ShouldHaveSingleItem();
        var changed = report.ContentChanged[0];
        changed.ChangeSource.ShouldNotBeNull();
        changed.ChangeSource!.Origin.ShouldBe(ChangeOrigin.Server);
    }

    [Fact]
    public async Task CompareAsync_ShouldReportConflict_WhenBothSidesChanged()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        var childDir = LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>local-edit</p>", "2", version: 2);

        // Simulate: marker was written in the past, then the local file was modified after
        var markerPath = Directory.GetFiles(childDir, ".id*")[0];
        var syncTime = DateTime.UtcNow.AddHours(-2);
        File.SetLastWriteTimeUtc(markerPath, syncTime);
        var indexPath = Path.Combine(childDir, "index.html");
        File.SetLastWriteTimeUtc(indexPath, DateTime.UtcNow.AddMinutes(-30));

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>server-edit</p>", "1", "Root", hasAttachments: false);
        child.Version = new VersionInfo { Number = 4, When = DateTime.UtcNow.AddHours(-1) };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.Conflicts.Where(x => x.PageId == "2").ShouldHaveSingleItem();
        report.Conflicts[0].ChangeSource.ShouldNotBeNull();
        report.Conflicts[0].ChangeSource!.Origin.ShouldBe(ChangeOrigin.Conflict);
        report.ContentChanged.ShouldNotContain(x => x.PageId == "2");
    }

    [Fact]
    public async Task CompareAsync_ShouldPopulateRenameSource_WhenDetectSourceEnabled()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "OldName", "<p>same</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var child = ApiClientMockFactory.CreatePage("2", "NewName", "<p>same</p>", "1", "Root", hasAttachments: false);
        child.Version = new VersionInfo { Number = 3, When = DateTime.UtcNow.AddDays(1) };

        var historicalPage = ApiClientMockFactory.CreatePage("2", "OldName", "<p>same</p>", "1", "Root");
        historicalPage.Version = new VersionInfo { Number = 2, When = DateTime.UtcNow.AddDays(-1) };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);
        api.Setup(x => x.GetPageVersionsAsync("2", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 3, When = DateTime.UtcNow.AddDays(1) },
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow.AddDays(-1) }
        ]);
        api.Setup(x => x.GetPageAtVersionAsync("2", 2)).ReturnsAsync(historicalPage);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true,
            matchByTitleWhenNoId: false, detectSource: true);

        report.RenamedOrMovedInConfluence.ShouldHaveSingleItem();
        var renamed = report.RenamedOrMovedInConfluence[0];
        renamed.RenameSource.ShouldNotBeNull();
        renamed.RenameSource!.Origin.ShouldBe(ChangeOrigin.Server);
        renamed.RenameSource.Confidence.ShouldBe(ChangeConfidence.High);
    }

    [Fact]
    public async Task CompareAsync_ShouldDetectRootPageMove_WhenLocalParentIdDiffers()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var newParentDir = LocalPageTreeBuilder.CreatePage(outputDir, "NewParent", "<p>np</p>", "P2");
        LocalPageTreeBuilder.CreatePage(newParentDir, "Subpage4", "<p>content</p>", "400");

        var root = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>content</p>", parentId: "P1", parentTitle: "OldParent", hasAttachments: false);
        root.Version = new VersionInfo { Number = 2, When = DateTime.UtcNow };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "400", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.Where(x => x.PageId == "400").ShouldHaveSingleItem();
        var moved = report.RenamedOrMovedInConfluence[0];
        moved.MoveSource.ShouldNotBeNull();
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportRootMove_WhenParentIdsMatch()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        File.WriteAllText(Path.Combine(outputDir, ".idP1"), string.Empty);
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>content</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>content</p>", parentId: "P1", parentTitle: "Parent", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldSkipRootMoveCheck_WhenParentDirHasNoIdMarker()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>content</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>content</p>", parentId: "P1", hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.ShouldBeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldSkipChildrenApi_WhenChildTypesHasPagesFalse()
    {
        // Оптимизация: для листьев (childTypes.page.value=false) запрос /child/page
        // не должен делаться — strict mock без setup зафейлит тест, если мы всё-таки его вызовем.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Leaf", "<p>leaf</p>", "1");

        var leaf = ApiClientMockFactory.CreatePage(
            "1", "Leaf", "<p>leaf</p>",
            hasPages: false, hasAttachments: false);

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(leaf);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AddedInConfluence.ShouldBeEmpty();
        report.DeletedInConfluence.ShouldBeEmpty();
        api.Verify(x => x.GetChildrenPagesAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CompareAsync_ShouldStillCallChildrenApi_WhenChildTypesIsNull()
    {
        // Backward-compat: если сервер не вернул childTypes (старая версия API),
        // должны fallback к старому поведению и запросить /child/page.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>"); // ChildTypes: null

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        // null childTypes also means "attachments unknown" — compare checks them.
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync([]);

        var service = CreateService(api);

        await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        api.Verify(x => x.GetChildrenPagesAsync("1"), Times.Once);
    }

    [Fact]
    public async Task CompareAsync_ShouldFetchAttachments_WhenChildTypesIsNull()
    {
        // Cloud v2 payloads carry no childTypes. null must mean "unknown —
        // check", otherwise a server-only attachment would silently escape
        // compare on Cloud (the old `?? false` fallback did exactly that).
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>"); // ChildTypes: null

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);
        api.Setup(x => x.GetAttachmentsAsync("1")).ReturnsAsync(
            [ApiClientMockFactory.CreateAttachment("ATT-1", "server-only.bin", fileSize: 10)]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        var page = report.AttachmentsChanged.ShouldHaveSingleItem();
        page.Differences.ShouldContain(d => d.FileName == "server-only.bin" && d.Kind == AttachmentDiffKind.OnlyRemote);
    }

    [Fact]
    public async Task CompareAsync_ShouldCollectAllChildren_UnderParallelism()
    {
        // Параллельный обход не должен терять страницы из ConcurrentDictionary.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>", hasAttachments: false);
        var children = Enumerable.Range(1, 10)
            .Select(i => ApiClientMockFactory.CreatePage($"ch{i}", $"Child{i}", $"<p>child {i}</p>", "1", "Root", hasAttachments: false))
            .ToList();

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync(children);
        foreach (var ch in children)
            api.Setup(x => x.GetChildrenPagesAsync(ch.Id)).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AddedInConfluence.Count().ShouldBe(10);
        report.AddedInConfluence.Select(p => p.PageId).ShouldBe(children.Select(c => c.Id), ignoreOrder: true);
    }
}
