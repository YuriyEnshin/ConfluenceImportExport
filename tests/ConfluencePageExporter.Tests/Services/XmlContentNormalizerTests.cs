using ConfluencePageExporter.Services;
using Shouldly;

namespace ConfluencePageExporter.Tests.Services;

// Confluence canonicalises storage format on save: it assigns ac:macro-id to
// macros that lack one and drops empty-named ac:parameter elements. The
// normalizer strips both so a local copy compares equal to the round-tripped
// server content instead of reporting a permanent phantom diff.
public class XmlContentNormalizerTests
{
    private static readonly XmlContentNormalizer N = new();

    [Fact]
    public void ContentEquals_IgnoresMacroId_AddedByServer()
    {
        var local  = "<ac:structured-macro ac:name=\"code\" ac:schema-version=\"1\"><ac:plain-text-body>x</ac:plain-text-body></ac:structured-macro>";
        var server = "<ac:structured-macro ac:macro-id=\"a96a26d3-ef5b-4086-bea9-f686fd1a49d5\" ac:name=\"code\" ac:schema-version=\"1\"><ac:plain-text-body>x</ac:plain-text-body></ac:structured-macro>";
        N.ContentEquals(local, server).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_IgnoresMacroId_WithDifferentValues()
    {
        var a = "<ac:structured-macro ac:macro-id=\"11111111-1111-1111-1111-111111111111\" ac:name=\"note\"><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        var b = "<ac:structured-macro ac:macro-id=\"22222222-2222-2222-2222-222222222222\" ac:name=\"note\"><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        N.ContentEquals(a, b).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_IgnoresEmptyParameter_DroppedByServer()
    {
        var local  = "<ac:structured-macro ac:name=\"note\"><ac:parameter ac:name=\"\" /><ac:parameter ac:name=\"title\">T</ac:parameter><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        var server = "<ac:structured-macro ac:name=\"note\"><ac:parameter ac:name=\"title\">T</ac:parameter><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        N.ContentEquals(local, server).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_IgnoresSchemaVersion_AssignedByServer()
    {
        // Verified live on Cloud: POST/PUT of a macro without ac:schema-version
        // stores it with ac:schema-version="1" (Server behaves the same).
        var local  = "<ac:structured-macro ac:name=\"info\"><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        var server = "<ac:structured-macro ac:name=\"info\" ac:schema-version=\"1\"><ac:rich-text-body><p>x</p></ac:rich-text-body></ac:structured-macro>";
        N.ContentEquals(local, server).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_IgnoresLocalId_StampedByCloudEditor()
    {
        // The Cloud editor stamps local-id on elements and ac:local-id on
        // macros; REST round-trips preserve them, editor saves inject them.
        var local  = "<p>Text</p><ac:structured-macro ac:name=\"code\"><ac:plain-text-body>x</ac:plain-text-body></ac:structured-macro>";
        var server = "<p local-id=\"35ce97f93c5c\">Text</p><ac:structured-macro ac:name=\"code\" ac:local-id=\"f1de5627211d\"><ac:plain-text-body>x</ac:plain-text-body></ac:structured-macro>";
        N.ContentEquals(local, server).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_StillDetectsRealParameterChange()
    {
        var a = "<ac:structured-macro ac:name=\"note\"><ac:parameter ac:name=\"title\">A</ac:parameter></ac:structured-macro>";
        var b = "<ac:structured-macro ac:name=\"note\"><ac:parameter ac:name=\"title\">B</ac:parameter></ac:structured-macro>";
        N.ContentEquals(a, b).ShouldBeFalse();
    }

    [Fact]
    public void ContentEquals_StillDetectsRealTextChange_AlongsideMacroIdNoise()
    {
        // macro-id differs (noise) AND the body text differs (real) → not equal.
        var a = "<ac:structured-macro ac:macro-id=\"11111111-1111-1111-1111-111111111111\" ac:name=\"note\"><ac:rich-text-body><p>one</p></ac:rich-text-body></ac:structured-macro>";
        var b = "<ac:structured-macro ac:macro-id=\"22222222-2222-2222-2222-222222222222\" ac:name=\"note\"><ac:rich-text-body><p>two</p></ac:rich-text-body></ac:structured-macro>";
        N.ContentEquals(a, b).ShouldBeFalse();
    }

    // ── ported from the deleted StorageFormatNormalizer facade tests ──
    // (same behaviour, now exercised on the instance directly)

    // ── line-ending normalization ─────────────────────────────────────

    [Fact]
    public void NormalizeForComparison_ShouldReplaceCrLfAndCrWithLf_InText()
    {
        N.NormalizeForComparison("line1\r\nline2\nline3\rline4")
            .ShouldBe("line1\nline2\nline3\nline4");
    }

    [Fact]
    public void NormalizeForComparison_ShouldPreserveLf_InText()
    {
        N.NormalizeForComparison("Hello\nWorld").ShouldBe("Hello\nWorld");
    }

    [Fact]
    public void NormalizeForComparison_ShouldHandleEmptyString()
    {
        N.NormalizeForComparison("").ShouldBeEmpty();
    }

    // ── ContentEquals: null / identity ────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenBothNull()
    {
        N.ContentEquals(null, null).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenOneIsNull()
    {
        N.ContentEquals("<p>text</p>", null).ShouldBeFalse();
        N.ContentEquals(null, "<p>text</p>").ShouldBeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenIdentical()
    {
        var content = "<p>Hello</p>\n<p>World</p>";
        N.ContentEquals(content, content).ShouldBeTrue();
    }

    // ── ContentEquals: line endings ───────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenOnlyLineEndingsDiffer()
    {
        var lf = "<p>Hello</p>\n<p>World</p>\n<ul>\n<li>item</li>\n</ul>";
        var crlf = "<p>Hello</p>\r\n<p>World</p>\r\n<ul>\r\n<li>item</li>\r\n</ul>";
        N.ContentEquals(lf, crlf).ShouldBeTrue();
    }

    // ── ContentEquals: XML whitespace / indentation ──────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenOnlyIndentationDiffers()
    {
        var compact = "<p><strong>Hello</strong></p>";
        var indented = "<p>\n  <strong>Hello</strong>\n</p>";
        N.ContentEquals(compact, indented).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenDeepIndentationDiffers()
    {
        var compact = "<ul><li><p>Item</p></li></ul>";
        var indented = "<ul>\n  <li>\n    <p>Item</p>\n  </li>\n</ul>";
        N.ContentEquals(compact, indented).ShouldBeTrue();
    }

    // ── ContentEquals: attribute ordering ─────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenAttributeOrderDiffers()
    {
        var a = "<ac:structured-macro ac:name=\"toc\" ac:schema-version=\"1\" ac:macro-id=\"abc\" />";
        var b = "<ac:structured-macro ac:macro-id=\"abc\" ac:name=\"toc\" ac:schema-version=\"1\" />";
        N.ContentEquals(a, b).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenAttributeOrderDiffers_NestedElements()
    {
        var a = """
                <ac:structured-macro ac:name="toc" ac:schema-version="1" ac:macro-id="x">
                  <ac:parameter ac:name="outline">true</ac:parameter>
                </ac:structured-macro>
                """;
        var b = "<ac:structured-macro ac:macro-id=\"x\" ac:name=\"toc\" ac:schema-version=\"1\">" +
                "<ac:parameter ac:name=\"outline\">true</ac:parameter>" +
                "</ac:structured-macro>";
        N.ContentEquals(a, b).ShouldBeTrue();
    }

    // ── ContentEquals: self-closing tags ──────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenSelfClosingTagFormatDiffers()
    {
        var withSpace = "<p><br /></p>";
        var withoutSpace = "<p><br/></p>";
        N.ContentEquals(withSpace, withoutSpace).ShouldBeTrue();
    }

    // ── ContentEquals: HTML entities ──────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenEntityVsUnicodeChar()
    {
        var withEntity = "<p>&mdash;</p>";
        var withChar = "<p>—</p>";
        N.ContentEquals(withEntity, withChar).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenEntityRepresentsDifferentChar()
    {
        var emDash = "<p>&mdash;</p>";
        var hyphen = "<p>-</p>";
        N.ContentEquals(emDash, hyphen).ShouldBeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldPreserveXmlEntities()
    {
        var a = "<p>&amp; &lt; &gt;</p>";
        var b = "<p>&amp; &lt; &gt;</p>";
        N.ContentEquals(a, b).ShouldBeTrue();
    }

    // ── ContentEquals: real content differences ───────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenContentActuallyDiffers()
    {
        N.ContentEquals("<p>local</p>", "<p>remote</p>").ShouldBeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenStructureDiffers()
    {
        var a = "<p><strong>Hello</strong></p>";
        var b = "<p><em>Hello</em></p>";
        N.ContentEquals(a, b).ShouldBeFalse();
    }

    // ── ContentEquals: combined formatting differences (real-world) ───

    [Fact]
    public void ContentEquals_ShouldReturnTrue_ForConfluenceMacro_WithFormattingDifferences()
    {
        var remote =
            "<p><ac:structured-macro ac:name=\"toc\" ac:schema-version=\"1\" ac:macro-id=\"fac-toc\">" +
            "<ac:parameter ac:name=\"outline\">true</ac:parameter>" +
            "</ac:structured-macro></p>" +
            "<h1>Title</h1>";

        var local =
            "<p>\r\n" +
            "    <ac:structured-macro ac:macro-id=\"fac-toc\" ac:name=\"toc\" ac:schema-version=\"1\">\r\n" +
            "      <ac:parameter ac:name=\"outline\">true</ac:parameter>\r\n" +
            "    </ac:structured-macro>\r\n" +
            "  </p>\r\n" +
            "  <h1>Title</h1>";

        N.ContentEquals(remote, local).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_ForConfluenceLink_WithFormattingDifferences()
    {
        var remote = "<ac:link><ri:page ri:content-title=\"My Page\" /></ac:link>";
        var local = "<ac:link\r\n      ><ri:page\r\n        ri:content-title=\"My Page\"\r\n    /></ac:link>";
        N.ContentEquals(remote, local).ShouldBeTrue();
    }

    // ── ContentEquals: fallback to line-ending comparison ─────────────

    [Fact]
    public void ContentEquals_ShouldFallbackGracefully_WhenXmlIsInvalid()
    {
        var invalid = "<p>Unclosed paragraph";
        N.ContentEquals(invalid, invalid).ShouldBeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldFallbackAndDetectCrlfDifference_WhenXmlIsInvalid()
    {
        var lf = "<p>Unclosed\n<b>also unclosed";
        var crlf = "<p>Unclosed\r\n<b>also unclosed";
        N.ContentEquals(lf, crlf).ShouldBeTrue();
    }

    // ── NormalizeForComparison: detailed canonicalization checks ──────

    [Fact]
    public void NormalizeForComparison_ShouldStripIndentation()
    {
        var input = "<p>\n  <strong>Hello</strong>\n</p>";
        var result = N.NormalizeForComparison(input);
        result.ShouldContain("<p><strong>Hello</strong></p>");
    }

    [Fact]
    public void NormalizeForComparison_ShouldSortAttributes()
    {
        // Volatile artifacts (ac:macro-id, ac:schema-version, …) are stripped,
        // so sorting is verified with two retained ac:parameter-style
        // attributes on a plain element (class < id, Ordinal).
        var input = "<p id=\"a\" class=\"b\">x</p>";
        var result = N.NormalizeForComparison(input);
        result.ShouldContain("<p class=\"b\" id=\"a\">x</p>");
    }

    [Fact]
    public void NormalizeForComparison_ShouldPreserveTextContent()
    {
        var input = "<p>Hello World</p>";
        var result = N.NormalizeForComparison(input);
        result.ShouldContain("<p>Hello World</p>");
    }

    [Fact]
    public void NormalizeForComparison_ShouldFallbackToLineNormalization_WhenXmlInvalid()
    {
        var input = "<p>Not closed\r\n<b>also broken";
        var result = N.NormalizeForComparison(input);
        result.ShouldBe("<p>Not closed\n<b>also broken");
    }
}
