using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Services;

public class ChangeSourceAnalyzerTests
{
    private static ChangeSourceAnalyzer CreateAnalyzer(Mock<IConfluenceApiClient>? api = null)
    {
        var mock = api ?? ApiClientMockFactory.CreateLoose();
        return new ChangeSourceAnalyzer(mock.Object, LoggerTestHelper.CreateLogger<ChangeSourceAnalyzer>());
    }

    // --- Content change date analysis ---

    [Fact]
    public void AnalyzeContentChange_ShouldReturnLocal_WhenLocalFileIsNewer()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(serverDate, localDate);

        result.Origin.Should().Be(ChangeOrigin.Local);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnServer_WhenServerIsNewer()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(serverDate, localDate);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnUnknown_WhenDatesEqual()
    {
        var analyzer = CreateAnalyzer();
        var date = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(date, date);

        result.Origin.Should().Be(ChangeOrigin.Unknown);
        result.Confidence.Should().Be(ChangeConfidence.Low);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnUnknown_WhenBothDatesNull()
    {
        var analyzer = CreateAnalyzer();

        var result = analyzer.AnalyzeContentChange(null, null);

        result.Origin.Should().Be(ChangeOrigin.Unknown);
        result.Confidence.Should().Be(ChangeConfidence.Low);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnLocal_WhenServerDateNull()
    {
        var analyzer = CreateAnalyzer();
        var localDate = new DateTime(2026, 3, 12, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(null, localDate);

        result.Origin.Should().Be(ChangeOrigin.Local);
        result.Confidence.Should().Be(ChangeConfidence.Low);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnServer_WhenLocalDateNull()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(serverDate, null);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Low);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnLocalHigh_WhenVersionsMatch()
    {
        var analyzer = CreateAnalyzer();

        var result = analyzer.AnalyzeContentChange(DateTime.UtcNow, DateTime.UtcNow,
            localMarkerVersion: 5, serverVersion: 5);

        result.Origin.Should().Be(ChangeOrigin.Local);
        result.Confidence.Should().Be(ChangeConfidence.High);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldReturnServerHigh_WhenServerVersionNewer()
    {
        var analyzer = CreateAnalyzer();

        var result = analyzer.AnalyzeContentChange(DateTime.UtcNow, DateTime.UtcNow,
            localMarkerVersion: 3, serverVersion: 5);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.High);
    }

    [Fact]
    public void AnalyzeContentChange_ShouldFallbackToDates_WhenLocalVersionNull()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = analyzer.AnalyzeContentChange(serverDate, localDate,
            localMarkerVersion: null, serverVersion: 5);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    // --- Rename analysis with dates only ---

    [Fact]
    public async Task AnalyzeRenameAsync_ShouldReturnLocal_WhenLocalDirIsNewer_WithoutHistory()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await analyzer.AnalyzeRenameAsync("1", "NewTitle", "OldTitle",
            serverDate, localDate, useVersionHistory: false);

        result.Origin.Should().Be(ChangeOrigin.Local);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    [Fact]
    public async Task AnalyzeRenameAsync_ShouldReturnServer_WhenServerIsNewer_WithoutHistory()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = await analyzer.AnalyzeRenameAsync("1", "NewTitle", "OldTitle",
            serverDate, localDate, useVersionHistory: false);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    // --- Rename analysis with version history ---

    [Fact]
    public async Task AnalyzeRenameAsync_ShouldReturnServerHigh_WhenOldTitleFoundInHistory()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageVersionsAsync("1", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 3, When = DateTime.UtcNow },
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow.AddDays(-1) },
            new PageVersionSummary { Number = 1, When = DateTime.UtcNow.AddDays(-2) }
        ]);
        var oldPage = ApiClientMockFactory.CreatePage("1", "OldTitle", "<p>x</p>");
        api.Setup(x => x.GetPageAtVersionAsync("1", 2)).ReturnsAsync(oldPage);

        var analyzer = CreateAnalyzer(api);

        var result = await analyzer.AnalyzeRenameAsync("1", "NewTitle", "OldTitle",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(-5), useVersionHistory: true);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.High);
        result.Reason.Should().Contain("OldTitle");
        result.Reason.Should().Contain("версии 2");
    }

    [Fact]
    public async Task AnalyzeRenameAsync_ShouldFallbackToDates_WhenOldTitleNotInHistory()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageVersionsAsync("1", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow },
            new PageVersionSummary { Number = 1, When = DateTime.UtcNow.AddDays(-1) }
        ]);
        var historicalPage = ApiClientMockFactory.CreatePage("1", "DifferentOldTitle", "<p>x</p>");
        api.Setup(x => x.GetPageAtVersionAsync("1", 1)).ReturnsAsync(historicalPage);

        var analyzer = CreateAnalyzer(api);
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = await analyzer.AnalyzeRenameAsync("1", "NewTitle", "OldTitle",
            serverDate, localDate, useVersionHistory: true);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    // --- Move analysis with dates only ---

    [Fact]
    public async Task AnalyzeMoveAsync_ShouldReturnLocal_WhenLocalDirIsNewer_WithoutHistory()
    {
        var analyzer = CreateAnalyzer();
        var serverDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);

        var result = await analyzer.AnalyzeMoveAsync("1", "NewParent", "OldParent",
            serverDate, localDate, useVersionHistory: false);

        result.Origin.Should().Be(ChangeOrigin.Local);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    // --- Move analysis with version history ---

    [Fact]
    public async Task AnalyzeMoveAsync_ShouldReturnServerHigh_WhenOldParentFoundInHistory()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageVersionsAsync("1", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 3, When = DateTime.UtcNow },
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow.AddDays(-1) }
        ]);
        var historicalPage = new PageData
        {
            Id = "1",
            Title = "Page",
            Ancestors = [new PageAncestor { Id = "10", Title = "OldParent" }]
        };
        api.Setup(x => x.GetPageAtVersionAsync("1", 2)).ReturnsAsync(historicalPage);

        var analyzer = CreateAnalyzer(api);

        var result = await analyzer.AnalyzeMoveAsync("1", "NewParent", "OldParent",
            DateTime.UtcNow, DateTime.UtcNow.AddDays(-5), useVersionHistory: true);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.High);
        result.Reason.Should().Contain("OldParent");
    }

    [Fact]
    public async Task AnalyzeMoveAsync_ShouldFallbackToDates_WhenOldParentNotInHistory()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageVersionsAsync("1", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow },
            new PageVersionSummary { Number = 1, When = DateTime.UtcNow.AddDays(-1) }
        ]);
        var historicalPage = new PageData
        {
            Id = "1",
            Title = "Page",
            Ancestors = [new PageAncestor { Id = "99", Title = "DifferentParent" }]
        };
        api.Setup(x => x.GetPageAtVersionAsync("1", 1)).ReturnsAsync(historicalPage);

        var analyzer = CreateAnalyzer(api);
        var serverDate = new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc);
        var localDate = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

        var result = await analyzer.AnalyzeMoveAsync("1", "NewParent", "OldParent",
            serverDate, localDate, useVersionHistory: true);

        result.Origin.Should().Be(ChangeOrigin.Server);
        result.Confidence.Should().Be(ChangeConfidence.Medium);
    }

    // --- Caching ---

    [Fact]
    public async Task AnalyzeRenameAsync_ShouldCacheVersionHistory_AcrossMultipleCalls()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageVersionsAsync("1", 10)).ReturnsAsync([
            new PageVersionSummary { Number = 2, When = DateTime.UtcNow },
            new PageVersionSummary { Number = 1, When = DateTime.UtcNow.AddDays(-1) }
        ]);
        var historicalPage = ApiClientMockFactory.CreatePage("1", "X", "<p>x</p>");
        api.Setup(x => x.GetPageAtVersionAsync("1", 1)).ReturnsAsync(historicalPage);

        var analyzer = CreateAnalyzer(api);

        await analyzer.AnalyzeRenameAsync("1", "A", "B", DateTime.UtcNow, DateTime.UtcNow, useVersionHistory: true);
        await analyzer.AnalyzeRenameAsync("1", "A", "B", DateTime.UtcNow, DateTime.UtcNow, useVersionHistory: true);

        api.Verify(x => x.GetPageVersionsAsync("1", 10), Times.Once());
    }
}
