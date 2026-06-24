using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using Shouldly;

namespace ConfluencePageExporter.Tests.Services;

public class AttachmentChangeAnalyzerTests
{
    private static AttachmentBaseline Baseline(int? version = 5, string? hash = "sha256:aa", long size = 3)
        => new("name", version, hash, size);

    [Fact]
    public void Unchanged_WhenNeitherSideMoved()
        => AttachmentChangeAnalyzer.Analyze(Baseline(), serverVersion: 5, localChanged: false)
            .ShouldBe(AttachmentChangeOrigin.Unchanged);

    [Fact]
    public void Local_WhenOnlyLocalChanged()
        => AttachmentChangeAnalyzer.Analyze(Baseline(version: 5), serverVersion: 5, localChanged: true)
            .ShouldBe(AttachmentChangeOrigin.Local);

    [Fact]
    public void Server_WhenOnlyServerVersionBumped()
        => AttachmentChangeAnalyzer.Analyze(Baseline(version: 5), serverVersion: 6, localChanged: false)
            .ShouldBe(AttachmentChangeOrigin.Server);

    [Fact]
    public void Conflict_WhenBothChanged()
        => AttachmentChangeAnalyzer.Analyze(Baseline(version: 5), serverVersion: 6, localChanged: true)
            .ShouldBe(AttachmentChangeOrigin.Conflict);

    [Fact]
    public void Unknown_WhenNoBaseline()
        => AttachmentChangeAnalyzer.Analyze(null, serverVersion: 6, localChanged: true)
            .ShouldBe(AttachmentChangeOrigin.Unknown);

    [Fact]
    public void Unknown_WhenBaselineMissesVersionOrHash()
    {
        AttachmentChangeAnalyzer.Analyze(Baseline(version: null), 6, true).ShouldBe(AttachmentChangeOrigin.Unknown);
        AttachmentChangeAnalyzer.Analyze(Baseline(hash: null), 6, true).ShouldBe(AttachmentChangeOrigin.Unknown);
    }

    [Fact]
    public void Unknown_WhenServerVersionOrLocalVerdictUndeterminable()
    {
        AttachmentChangeAnalyzer.Analyze(Baseline(), serverVersion: null, localChanged: false).ShouldBe(AttachmentChangeOrigin.Unknown);
        AttachmentChangeAnalyzer.Analyze(Baseline(), serverVersion: 5, localChanged: null).ShouldBe(AttachmentChangeOrigin.Unknown);
    }
}
