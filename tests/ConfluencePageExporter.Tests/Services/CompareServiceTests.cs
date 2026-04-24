using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class CompareServiceTests
{
    private static CompareService CreateService(Mock<IConfluenceApiClient> api)
    {
        var analyzer = new ChangeSourceAnalyzer(api.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
        return new CompareService(api.Object, analyzer, LoggerTestHelper.CreateLogger<CompareService>());
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

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportAddedInConfluence_WhenRemoteChildIsMissingLocally()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var localRoot = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        _ = localRoot;

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>child</p>", "1", "Root");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AddedInConfluence.Should().ContainSingle(x => x.PageId == "2");
        report.DeletedInConfluence.Should().BeEmpty();
        report.RenamedOrMovedInConfluence.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldReportDeletedInConfluence_WhenLocalChildIsMissingRemotely()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.DeletedInConfluence.Should().ContainSingle(x => x.PageId == "2");
    }

    [Fact]
    public async Task CompareAsync_ShouldReportRenamedOrMoved_WhenSameIdHasDifferentPath()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "OldName", "<p>same</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "NewName", "<p>same</p>", "1", "Root");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.RenamedOrMovedInConfluence.Should().ContainSingle(x => x.PageId == "2");
    }

    [Fact]
    public async Task CompareAsync_ShouldReportContentChanged_WhenContentDiffers()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>local</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>remote</p>", "1", "Root");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.ContentChanged.Should().ContainSingle(x => x.PageId == "2");
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportContentChanged_WhenOnlyLineEndingsDiffer()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(
            outputDir, "Root", "<p>Hello</p>\r\n<p>World</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>Hello</p>\n<p>World</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.ContentChanged.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportAdded_WhenTitleFallbackMatchesWithoutId()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false, matchByTitleWhenNoId: true);

        report.AddedInConfluence.Should().BeEmpty();
        report.Notes.Should().Contain(n => n.Contains("matched by title/folder name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CompareAsync_ShouldIgnoreLocalChildren_WhenRecursiveIsDisabled()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>child</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.DeletedInConfluence.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldPopulateChangeSource_ForContentDifferences()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "Child", "<p>local</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "Child", "<p>remote</p>", "1", "Root");
        child.Version = new VersionInfo { Number = 3, When = DateTime.UtcNow.AddDays(1) };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([child]);
        api.Setup(x => x.GetChildrenPagesAsync("2")).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.ContentChanged.Should().ContainSingle();
        var changed = report.ContentChanged[0];
        changed.ChangeSource.Should().NotBeNull();
        changed.ChangeSource!.Origin.Should().Be(ChangeOrigin.Server);
    }

    [Fact]
    public async Task CompareAsync_ShouldPopulateRenameSource_WhenDetectSourceEnabled()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var rootDir = LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");
        LocalPageTreeBuilder.CreatePage(rootDir, "OldName", "<p>same</p>", "2");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var child = ApiClientMockFactory.CreatePage("2", "NewName", "<p>same</p>", "1", "Root");
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

        report.RenamedOrMovedInConfluence.Should().ContainSingle();
        var renamed = report.RenamedOrMovedInConfluence[0];
        renamed.RenameSource.Should().NotBeNull();
        renamed.RenameSource!.Origin.Should().Be(ChangeOrigin.Server);
        renamed.RenameSource.Confidence.Should().Be(ChangeConfidence.High);
    }

    [Fact]
    public async Task CompareAsync_ShouldDetectRootPageMove_WhenLocalParentIdDiffers()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        var newParentDir = LocalPageTreeBuilder.CreatePage(outputDir, "NewParent", "<p>np</p>", "P2");
        LocalPageTreeBuilder.CreatePage(newParentDir, "Subpage4", "<p>content</p>", "400");

        var root = ApiClientMockFactory.CreatePage("400", "Subpage4", "<p>content</p>", parentId: "P1", parentTitle: "OldParent");
        root.Version = new VersionInfo { Number = 2, When = DateTime.UtcNow };

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("400")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "400", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.Should().ContainSingle(x => x.PageId == "400");
        var moved = report.RenamedOrMovedInConfluence[0];
        moved.MoveSource.Should().NotBeNull();
    }

    [Fact]
    public async Task CompareAsync_ShouldNotReportRootMove_WhenParentIdsMatch()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        File.WriteAllText(Path.Combine(outputDir, ".idP1"), string.Empty);
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>content</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>content</p>", parentId: "P1", parentTitle: "Parent");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.Should().BeEmpty();
    }

    [Fact]
    public async Task CompareAsync_ShouldSkipRootMoveCheck_WhenParentDirHasNoIdMarker()
    {
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>content</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>content</p>", parentId: "P1");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: false);

        report.RenamedOrMovedInConfluence.Should().BeEmpty();
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

        report.AddedInConfluence.Should().BeEmpty();
        report.DeletedInConfluence.Should().BeEmpty();
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

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync([]);

        var service = CreateService(api);

        await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        api.Verify(x => x.GetChildrenPagesAsync("1"), Times.Once);
    }

    [Fact]
    public async Task CompareAsync_ShouldCollectAllChildren_UnderParallelism()
    {
        // Параллельный обход не должен терять страницы из ConcurrentDictionary.
        using var temp = new TempDirectoryScope();
        var outputDir = temp.CreateDirectory("out");
        LocalPageTreeBuilder.CreatePage(outputDir, "Root", "<p>root</p>", "1");

        var root = ApiClientMockFactory.CreatePage("1", "Root", "<p>root</p>");
        var children = Enumerable.Range(1, 10)
            .Select(i => ApiClientMockFactory.CreatePage($"ch{i}", $"Child{i}", $"<p>child {i}</p>", "1", "Root"))
            .ToList();

        var api = ApiClientMockFactory.CreateStrict();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(root);
        api.Setup(x => x.GetChildrenPagesAsync("1")).ReturnsAsync(children);
        foreach (var ch in children)
            api.Setup(x => x.GetChildrenPagesAsync(ch.Id)).ReturnsAsync([]);

        var service = CreateService(api);

        var report = await service.CompareAsync("SPACE", "1", null, outputDir, recursive: true);

        report.AddedInConfluence.Should().HaveCount(10);
        report.AddedInConfluence.Select(p => p.PageId).Should()
            .BeEquivalentTo(children.Select(c => c.Id));
    }
}
