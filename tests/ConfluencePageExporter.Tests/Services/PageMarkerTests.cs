using ConfluencePageExporter.Models;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;

namespace ConfluencePageExporter.Tests.Services;

public class PageMarkerTests
{
    // ── Load: file name (id / version) ────────────────────────────────────

    [Fact]
    public void Load_ShouldReturnNull_WhenDirectoryDoesNotExist()
    {
        PageMarker.Load("X:\\not-existing-dir").ShouldBeNull();
    }

    [Fact]
    public void Load_ShouldReturnNull_WhenNoMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        PageMarker.Load(pageDir).ShouldBeNull();
    }

    [Fact]
    public void Load_ShouldReturnPageId_WhenMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.PageId.ShouldBe("123");
        marker.Version.ShouldBeNull();
    }

    [Fact]
    public void Load_ShouldReturnIdAndVersion_WhenVersionedMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id456_7"), string.Empty);

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.PageId.ShouldBe("456");
        marker.Version.ShouldBe(7);
    }

    [Fact]
    public void Load_ShouldExposeMarkerFileTime_AsSyncTimestamp()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var markerPath = Path.Combine(pageDir, ".id123_5");
        File.WriteAllText(markerPath, string.Empty);

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.FileTimeUtc.ShouldBe(File.GetLastWriteTimeUtc(markerPath));
    }

    [Fact]
    public void ParseFileName_ShouldParseVersionedFormat()
    {
        var result = PageMarker.ParseFileName(".id12345_42");

        result.PageId.ShouldBe("12345");
        result.Version.ShouldBe(42);
    }

    [Fact]
    public void ParseFileName_ShouldParseOldFormat()
    {
        var result = PageMarker.ParseFileName(".id12345");

        result.PageId.ShouldBe("12345");
        result.Version.ShouldBeNull();
    }

    // ── Load: body (title / space / hash / epoch) ─────────────────────────

    [Fact]
    public void Load_ShouldReturnTitle_WhenMarkerHasPlainTextBody()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), "Original Title");

        PageMarker.Load(pageDir).ShouldNotBeNull().Title.ShouldBe("Original Title");
    }

    [Fact]
    public void Load_ShouldReturnNullTitle_WhenMarkerIsEmpty()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), string.Empty);

        PageMarker.Load(pageDir).ShouldNotBeNull().Title.ShouldBeNull();
    }

    [Fact]
    public void Load_ShouldParseJsonBody()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), """{"title":"My Page","space":"DOCS"}""");

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.Title.ShouldBe("My Page");
        marker.SpaceKey.ShouldBe("DOCS");
    }

    [Fact]
    public void Load_ShouldParseHashAndEpoch()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"),
            """{"title":"My Page","space":"DOCS","h":"sha256:ff00","ne":1}""");

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.ContentHash.ShouldBe("sha256:ff00");
        marker.NormalizationEpoch.ShouldBe(1);
    }

    [Fact]
    public void Load_ShouldReturnNullSpaceAndHash_ForLegacyPlainTitleMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), "Legacy Title");

        var marker = PageMarker.Load(pageDir);

        marker.ShouldNotBeNull();
        marker!.Title.ShouldBe("Legacy Title");
        marker.SpaceKey.ShouldBeNull();
        marker.ContentHash.ShouldBeNull();
        marker.NormalizationEpoch.ShouldBeNull();
    }

    // ── WriteAsync: file name ─────────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ShouldWriteVersionedMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "123", 5);

        File.Exists(Path.Combine(pageDir, ".id123_5")).ShouldBeTrue();
    }

    [Fact]
    public async Task WriteAsync_ShouldReplaceOldMarker_WithVersionedMarker()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123"), string.Empty);

        await PageMarker.WriteAsync(pageDir, "123", 3);

        File.Exists(Path.Combine(pageDir, ".id123")).ShouldBeFalse();
        File.Exists(Path.Combine(pageDir, ".id123_3")).ShouldBeTrue();
    }

    // ── WriteAsync: body format ───────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ShouldStoreOriginalTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "123", 5, "Модуль \"Провайдеры\"");

        var markerPath = Path.Combine(pageDir, ".id123_5");
        File.Exists(markerPath).ShouldBeTrue();
        (await File.ReadAllTextAsync(markerPath, TestContext.Current.CancellationToken)).ShouldBe("Модуль \"Провайдеры\"");
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteEmptyContent_WhenNoTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "123", 5);

        var content = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        content.ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteJsonBody_WhenSpaceProvided()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "123", 5, "Модуль \"Провайдеры\"", "DEV");

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        body.ShouldContain("\"space\":\"DEV\"");
        body.ShouldContain("Модуль"); // readable Cyrillic, not \uXXXX-escaped

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.Title.ShouldBe("Модуль \"Провайдеры\"");
        marker.SpaceKey.ShouldBe("DEV");
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteLegacyPlainTitle_WhenNoSpace()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "123", 5, "Plain Title");

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id123_5"), TestContext.Current.CancellationToken);
        body.ShouldBe("Plain Title"); // unchanged on-disk format — zero churn until space is captured
        PageMarker.Load(pageDir).ShouldNotBeNull().SpaceKey.ShouldBeNull();
    }

    [Fact]
    public async Task WriteAsync_ShouldRoundTripJsonBody_WithSpecialCharsInTitle()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "777", 2, "A \"quoted\" & <odd> title", "TEAM");

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.Title.ShouldBe("A \"quoted\" & <odd> title");
        marker.SpaceKey.ShouldBe("TEAM");
    }

    [Fact]
    public async Task WriteAsync_ShouldRoundTripHashAndEpoch()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(pageDir, "555", 7, "Title", "DEV", "sha256:abc123", 1);

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id555_7"), TestContext.Current.CancellationToken);
        body.ShouldContain("\"h\":\"sha256:abc123\"");
        body.ShouldContain("\"ne\":1");

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.ContentHash.ShouldBe("sha256:abc123");
        marker.NormalizationEpoch.ShouldBe(1);
        marker.SpaceKey.ShouldBe("DEV");
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteJsonWithHash_EvenWithoutSpace()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        await PageMarker.WriteAsync(
            pageDir, "555", 7, "Title", spaceKey: null, contentHash: "sha256:deadbeef", normalizationEpoch: 1);

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.ContentHash.ShouldBe("sha256:deadbeef");
        marker.NormalizationEpoch.ShouldBe(1);
        marker.SpaceKey.ShouldBeNull();
        marker.Title.ShouldBe("Title");
    }

    [Fact]
    public async Task WriteAsync_ShouldOmitEpochAndHash_WhenNoHash()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");

        // Epoch without a hash is meaningless and must not be persisted.
        await PageMarker.WriteAsync(
            pageDir, "555", 7, "Title", "DEV", contentHash: null, normalizationEpoch: 1);

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id555_7"), TestContext.Current.CancellationToken);
        body.ShouldNotContain("\"ne\"");
        body.ShouldNotContain("\"h\"");
        PageMarker.Load(pageDir).ShouldNotBeNull().NormalizationEpoch.ShouldBeNull();
    }

    // ── UpdateAsync: the shared skip/upgrade write policy ─────────────────

    private static ContentHasher Hasher() => new(new XmlContentNormalizer());

    [Fact]
    public async Task UpdateAsync_ShouldWriteMarkerWithHashAndEpoch_WhenNoMarkerExists()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();

        var written = await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher);

        written.ShouldBeTrue();
        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.PageId.ShouldBe("100");
        marker.Version.ShouldBe(5);
        marker.Title.ShouldBe("Title");
        marker.SpaceKey.ShouldBe("DOCS");
        marker.ContentHash.ShouldBe(hasher.ComputeHash("<p>x</p>"));
        marker.NormalizationEpoch.ShouldBe(hasher.Epoch);
    }

    [Fact]
    public async Task UpdateAsync_ShouldSkipRewrite_WhenEverythingMatches()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();
        await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher);

        var writtenAgain = await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher);

        writtenAgain.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldUpgradeLegacyMarker_WhenSpaceBecomesKnown()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        await PageMarker.WriteAsync(pageDir, "100", 5, "Title"); // legacy: plain title, no space/hash

        var written = await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", syncedContent: null, Hasher());

        written.ShouldBeTrue();
        PageMarker.Load(pageDir).ShouldNotBeNull().SpaceKey.ShouldBe("DOCS");
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreserveStoredHash_WhenNoContentToHash()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();
        await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher);
        var storedHash = PageMarker.Load(pageDir)!.ContentHash;

        // Version moves but there is no body to re-hash — the old hash must
        // survive the rewrite instead of being dropped.
        var written = await PageMarker.UpdateAsync(pageDir, "100", 6, "Title", "DOCS", syncedContent: null, hasher);

        written.ShouldBeTrue();
        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.Version.ShouldBe(6);
        marker.ContentHash.ShouldBe(storedHash);
        marker.NormalizationEpoch.ShouldBe(hasher.Epoch);
    }

    // ── attachments baseline (att) ────────────────────────────────────────

    [Fact]
    public async Task WriteAsync_ShouldRoundTripAttachmentBaselines()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var att = new Dictionary<string, AttachmentBaseline>
        {
            ["diagram_v2"] = new AttachmentBaseline("diagram:v2", 5, "sha256:aa", 1234),
            ["preview.png"] = new AttachmentBaseline("preview.png", 6, "sha256:bb", 5678),
        };

        await PageMarker.WriteAsync(pageDir, "100", 5, "Title", "DOCS", "sha256:cc", 2, att);

        var body = await File.ReadAllTextAsync(Path.Combine(pageDir, ".id100_5"), TestContext.Current.CancellationToken);
        body.ShouldContain("\"att\"");
        body.ShouldContain("\"n\":\"diagram:v2\""); // full server name (with a Windows-invalid ':') preserved

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.Attachments.ShouldNotBeNull();
        marker.Attachments!.Count.ShouldBe(2);
        var diagram = marker.Attachments["diagram_v2"];
        diagram.ServerName.ShouldBe("diagram:v2");
        diagram.Version.ShouldBe(5);
        diagram.Hash.ShouldBe("sha256:aa");
        diagram.Size.ShouldBe(1234);
    }

    [Fact]
    public async Task WriteAsync_ShouldWriteJsonWithAttachments_EvenWithoutSpaceOrHash()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var att = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 1, "sha256:aa", 3) };

        await PageMarker.WriteAsync(pageDir, "100", 5, "Title", spaceKey: null, contentHash: null, normalizationEpoch: null, attachments: att);

        PageMarker.Load(pageDir).ShouldNotBeNull().Attachments.ShouldNotBeNull();
    }

    [Fact]
    public void Load_ShouldReturnNullAttachments_ForMarkerWithoutAtt()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        File.WriteAllText(Path.Combine(pageDir, ".id123_5"), """{"title":"T","space":"D"}""");

        PageMarker.Load(pageDir).ShouldNotBeNull().Attachments.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateAsync_ShouldRewrite_WhenAttachmentsChange()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();
        var att1 = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 1, "sha256:aa", 3) };
        await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher, att1);

        var att2 = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 2, "sha256:bb", 9) };
        var written = await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher, att2);

        written.ShouldBeTrue();
        PageMarker.Load(pageDir)!.Attachments!["a"].Version.ShouldBe(2);
    }

    [Fact]
    public async Task UpdateAsync_ShouldSkipRewrite_WhenAttachmentsAlsoMatch()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();
        var att = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 1, "sha256:aa", 3) };
        await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher, att);

        var sameAtt = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 1, "sha256:aa", 3) };
        var writtenAgain = await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher, sameAtt);

        writtenAgain.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_ShouldPreserveAttachments_WhenNullPassed()
    {
        using var temp = new TempDirectoryScope();
        var pageDir = temp.CreateDirectory("Page");
        var hasher = Hasher();
        var att = new Dictionary<string, AttachmentBaseline> { ["a"] = new AttachmentBaseline("a", 1, "sha256:aa", 3) };
        await PageMarker.UpdateAsync(pageDir, "100", 5, "Title", "DOCS", "<p>x</p>", hasher, att);

        // A later marker update that didn't sync attachments (null) must keep them.
        await PageMarker.UpdateAsync(pageDir, "100", 6, "Title", "DOCS", "<p>x</p>", hasher, attachments: null);

        var marker = PageMarker.Load(pageDir);
        marker.ShouldNotBeNull();
        marker!.Version.ShouldBe(6);
        marker.Attachments.ShouldNotBeNull();
        marker.Attachments!["a"].ServerName.ShouldBe("a");
    }
}
