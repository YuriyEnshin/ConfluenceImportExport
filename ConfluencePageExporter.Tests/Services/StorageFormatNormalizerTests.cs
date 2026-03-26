using ConfluencePageExporter.Services;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Services;

public class StorageFormatNormalizerTests
{
    // ── NormalizeLineEndings ──────────────────────────────────────────

    [Fact]
    public void NormalizeLineEndings_ShouldReplaceCrLfWithLf()
    {
        StorageFormatNormalizer.NormalizeLineEndings("<p>Hello</p>\r\n<p>World</p>")
            .Should().Be("<p>Hello</p>\n<p>World</p>");
    }

    [Fact]
    public void NormalizeLineEndings_ShouldReplaceStandaloneCrWithLf()
    {
        StorageFormatNormalizer.NormalizeLineEndings("<p>Hello</p>\r<p>World</p>")
            .Should().Be("<p>Hello</p>\n<p>World</p>");
    }

    [Fact]
    public void NormalizeLineEndings_ShouldPreserveLf()
    {
        var input = "<p>Hello</p>\n<p>World</p>";
        StorageFormatNormalizer.NormalizeLineEndings(input).Should().Be(input);
    }

    [Fact]
    public void NormalizeLineEndings_ShouldHandleMixedLineEndings()
    {
        StorageFormatNormalizer.NormalizeLineEndings("line1\r\nline2\nline3\rline4")
            .Should().Be("line1\nline2\nline3\nline4");
    }

    [Fact]
    public void NormalizeLineEndings_ShouldHandleEmptyString()
    {
        StorageFormatNormalizer.NormalizeLineEndings("").Should().BeEmpty();
    }

    // ── ContentEquals: null / identity ────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenBothNull()
    {
        StorageFormatNormalizer.ContentEquals(null, null).Should().BeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenOneIsNull()
    {
        StorageFormatNormalizer.ContentEquals("<p>text</p>", null).Should().BeFalse();
        StorageFormatNormalizer.ContentEquals(null, "<p>text</p>").Should().BeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenIdentical()
    {
        var content = "<p>Hello</p>\n<p>World</p>";
        StorageFormatNormalizer.ContentEquals(content, content).Should().BeTrue();
    }

    // ── ContentEquals: line endings ───────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenOnlyLineEndingsDiffer()
    {
        var lf = "<p>Hello</p>\n<p>World</p>\n<ul>\n<li>item</li>\n</ul>";
        var crlf = "<p>Hello</p>\r\n<p>World</p>\r\n<ul>\r\n<li>item</li>\r\n</ul>";
        StorageFormatNormalizer.ContentEquals(lf, crlf).Should().BeTrue();
    }

    // ── ContentEquals: XML whitespace / indentation ──────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenOnlyIndentationDiffers()
    {
        var compact = "<p><strong>Hello</strong></p>";
        var indented = "<p>\n  <strong>Hello</strong>\n</p>";
        StorageFormatNormalizer.ContentEquals(compact, indented).Should().BeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenDeepIndentationDiffers()
    {
        var compact = "<ul><li><p>Item</p></li></ul>";
        var indented = "<ul>\n  <li>\n    <p>Item</p>\n  </li>\n</ul>";
        StorageFormatNormalizer.ContentEquals(compact, indented).Should().BeTrue();
    }

    // ── ContentEquals: attribute ordering ─────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenAttributeOrderDiffers()
    {
        var a = "<ac:structured-macro ac:name=\"toc\" ac:schema-version=\"1\" ac:macro-id=\"abc\" />";
        var b = "<ac:structured-macro ac:macro-id=\"abc\" ac:name=\"toc\" ac:schema-version=\"1\" />";
        StorageFormatNormalizer.ContentEquals(a, b).Should().BeTrue();
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
        StorageFormatNormalizer.ContentEquals(a, b).Should().BeTrue();
    }

    // ── ContentEquals: self-closing tags ──────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenSelfClosingTagFormatDiffers()
    {
        var withSpace = "<p><br /></p>";
        var withoutSpace = "<p><br/></p>";
        StorageFormatNormalizer.ContentEquals(withSpace, withoutSpace).Should().BeTrue();
    }

    // ── ContentEquals: HTML entities ──────────────────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnTrue_WhenEntityVsUnicodeChar()
    {
        var withEntity = "<p>&mdash;</p>";
        var withChar = "<p>\u2014</p>";
        StorageFormatNormalizer.ContentEquals(withEntity, withChar).Should().BeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenEntityRepresentsDifferentChar()
    {
        var emDash = "<p>&mdash;</p>";
        var hyphen = "<p>-</p>";
        StorageFormatNormalizer.ContentEquals(emDash, hyphen).Should().BeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldPreserveXmlEntities()
    {
        var a = "<p>&amp; &lt; &gt;</p>";
        var b = "<p>&amp; &lt; &gt;</p>";
        StorageFormatNormalizer.ContentEquals(a, b).Should().BeTrue();
    }

    // ── ContentEquals: real content differences ───────────────────────

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenContentActuallyDiffers()
    {
        StorageFormatNormalizer.ContentEquals("<p>local</p>", "<p>remote</p>").Should().BeFalse();
    }

    [Fact]
    public void ContentEquals_ShouldReturnFalse_WhenStructureDiffers()
    {
        var a = "<p><strong>Hello</strong></p>";
        var b = "<p><em>Hello</em></p>";
        StorageFormatNormalizer.ContentEquals(a, b).Should().BeFalse();
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

        StorageFormatNormalizer.ContentEquals(remote, local).Should().BeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldReturnTrue_ForConfluenceLink_WithFormattingDifferences()
    {
        var remote = "<ac:link><ri:page ri:content-title=\"My Page\" /></ac:link>";
        var local = "<ac:link\r\n      ><ri:page\r\n        ri:content-title=\"My Page\"\r\n    /></ac:link>";
        StorageFormatNormalizer.ContentEquals(remote, local).Should().BeTrue();
    }

    // ── ContentEquals: fallback to line-ending comparison ─────────────

    [Fact]
    public void ContentEquals_ShouldFallbackGracefully_WhenXmlIsInvalid()
    {
        var invalid = "<p>Unclosed paragraph";
        StorageFormatNormalizer.ContentEquals(invalid, invalid).Should().BeTrue();
    }

    [Fact]
    public void ContentEquals_ShouldFallbackAndDetectCrlfDifference_WhenXmlIsInvalid()
    {
        var lf = "<p>Unclosed\n<b>also unclosed";
        var crlf = "<p>Unclosed\r\n<b>also unclosed";
        StorageFormatNormalizer.ContentEquals(lf, crlf).Should().BeTrue();
    }

    // ── NormalizeForComparison: detailed canonicalization checks ──────

    [Fact]
    public void NormalizeForComparison_ShouldStripIndentation()
    {
        var input = "<p>\n  <strong>Hello</strong>\n</p>";
        var result = StorageFormatNormalizer.NormalizeForComparison(input);
        result.Should().Contain("<p><strong>Hello</strong></p>");
    }

    [Fact]
    public void NormalizeForComparison_ShouldSortAttributes()
    {
        var input = "<ac:structured-macro ac:name=\"toc\" ac:macro-id=\"x\" />";
        var result = StorageFormatNormalizer.NormalizeForComparison(input);
        result.Should().Contain("ac:macro-id=\"x\" ac:name=\"toc\"");
    }

    [Fact]
    public void NormalizeForComparison_ShouldPreserveTextContent()
    {
        var input = "<p>Hello World</p>";
        var result = StorageFormatNormalizer.NormalizeForComparison(input);
        result.Should().Contain("<p>Hello World</p>");
    }

    [Fact]
    public void NormalizeForComparison_ShouldFallbackToLineNormalization_WhenXmlInvalid()
    {
        var input = "<p>Not closed\r\n<b>also broken";
        var result = StorageFormatNormalizer.NormalizeForComparison(input);
        result.Should().Be("<p>Not closed\n<b>also broken");
    }
}
