using ConfluencePageExporter.Services;
using Shouldly;

namespace ConfluencePageExporter.Tests.Services;

public class ContentHasherTests
{
    private static ContentHasher Xml() => new(new XmlContentNormalizer());

    /// A normalizer whose output deliberately diverges from the golden contract,
    /// so the hashing regime is guaranteed "untrusted" (deterministic — unlike
    /// relying on RegexContentNormalizer's incidental output for this input).
    private sealed class DivergentNormalizer : IContentNormalizer
    {
        public bool ContentEquals(string? left, string? right) => string.Equals(left, right, StringComparison.Ordinal);
        public string NormalizeForComparison(string content) => "DIVERGENT::" + content;
    }

    // ── Golden-vector tripwire ────────────────────────────────────────
    // If this fails after a normalizer/runtime change, that is the signal to bump
    // NormalizationContract.CurrentEpoch and refresh GoldenNormalized.
    [Fact]
    public void Golden_DefaultNormalizerReproducesContract()
    {
        new XmlContentNormalizer().NormalizeForComparison(NormalizationContract.GoldenInput)
            .ShouldBe(NormalizationContract.GoldenNormalized);
    }

    [Fact]
    public void Xml_RegimeTrusted_AndEpochCurrent()
    {
        var h = Xml();
        h.RegimeTrusted.ShouldBeTrue();
        h.Epoch.ShouldBe(NormalizationContract.CurrentEpoch);
    }

    [Fact]
    public void ComputeHash_IsPrefixedSha256Hex()
    {
        var h = Xml().ComputeHash("<p>x</p>");
        h.ShouldNotBeNull();
        h.ShouldStartWith("sha256:");
        h!.Length.ShouldBe("sha256:".Length + 64); // SHA-256 = 32 bytes = 64 hex chars
    }

    [Fact]
    public void ComputeHash_IgnoresCosmeticDifferences()
    {
        var h = Xml();
        // Reordered attributes + extra whitespace + entity-vs-char → same canonical form.
        h.ComputeHash("<p  id=\"a\" class=\"b\">x&nbsp;y</p>")
            .ShouldBe(h.ComputeHash("<p class=\"b\" id=\"a\">x y</p>"));
    }

    [Fact]
    public void ComputeHash_DiffersOnRealChange()
    {
        var h = Xml();
        h.ComputeHash("<p>one</p>").ShouldNotBe(h.ComputeHash("<p>two</p>"));
    }

    // ── EvaluateLocalChange ───────────────────────────────────────────
    private static PageMarker Marker(string? hash, int? epoch) =>
        new("1", 1, "t", "S", hash, epoch, FileTimeUtc: null);

    private static PageMarker MarkerHashing(ContentHasher h, string content) =>
        Marker(h.ComputeHash(content), NormalizationContract.CurrentEpoch);

    [Fact]
    public void EvaluateLocalChange_Null_WhenNoMarker()
    {
        Xml().EvaluateLocalChange(null, "<p>x</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBeNull();
    }

    [Fact]
    public void EvaluateLocalChange_Null_WhenNoStoredHash()
    {
        var marker = Marker(hash: null, epoch: null);
        Xml().EvaluateLocalChange(marker, "<p>x</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBeNull();
    }

    [Fact]
    public void EvaluateLocalChange_False_WhenContentMatchesStoredHash_EvenIfTouched()
    {
        var h = Xml();
        var marker = MarkerHashing(h, "<p>x</p>");
        // Touched after sync (mtime > sync) but content identical → NOT a change.
        // This is the false-conflict class the hash eliminates.
        h.EvaluateLocalChange(marker, "<p>x</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBe(false);
    }

    [Fact]
    public void EvaluateLocalChange_True_WhenContentDiffersFromStoredHash()
    {
        var h = Xml();
        var marker = MarkerHashing(h, "<p>x</p>");
        h.EvaluateLocalChange(marker, "<p>EDITED</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBe(true);
    }

    [Fact]
    public void EvaluateLocalChange_False_FastPath_WhenNotTouchedSinceSync()
    {
        var h = Xml();
        // mtime <= syncTime ⇒ file untouched ⇒ "unchanged" without hashing the
        // (here intentionally different) current content.
        var marker = MarkerHashing(h, "<p>x</p>");
        h.EvaluateLocalChange(marker, "<p>WHATEVER</p>", DateTime.UtcNow.AddHours(-2), DateTime.UtcNow)
            .ShouldBe(false);
    }

    [Fact]
    public void EvaluateLocalChange_Null_WhenEpochMismatch()
    {
        var h = Xml();
        var marker = Marker(h.ComputeHash("<p>x</p>"), epoch: 999);
        h.EvaluateLocalChange(marker, "<p>EDITED</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBeNull();
    }

    // ── Untrusted regime disables hashing ─────────────────────────────
    [Fact]
    public void DivergentNormalizer_DisablesHashing()
    {
        var h = new ContentHasher(new DivergentNormalizer());
        h.RegimeTrusted.ShouldBeFalse();
        h.Epoch.ShouldBeNull();
        h.ComputeHash("<p>x</p>").ShouldBeNull();

        var marker = Marker("sha256:deadbeef", NormalizationContract.CurrentEpoch);
        h.EvaluateLocalChange(marker, "<p>x</p>", DateTime.UtcNow, DateTime.UtcNow.AddHours(-1))
            .ShouldBeNull();
    }
}
