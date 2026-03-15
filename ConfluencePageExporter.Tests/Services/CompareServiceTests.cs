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
}
