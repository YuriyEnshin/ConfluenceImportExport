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
}
