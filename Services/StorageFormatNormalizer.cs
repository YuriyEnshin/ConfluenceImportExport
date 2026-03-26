using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Normalizes Confluence storage format content before comparison.
///
/// Two-stage normalization:
/// 1. Line endings: CRLF / standalone CR → LF
/// 2. XML canonicalization (with graceful fallback):
///    - Wraps content in a root element with Confluence namespace declarations (ac:, ri:, at:)
///    - Parses as XML, stripping whitespace-only text nodes (indentation between tags)
///    - Sorts attributes by expanded name for consistent ordering
///    - Serializes without formatting
///    - Replaces HTML named entities with numeric equivalents before parsing
///    - On any parse failure, falls back to line-ending-only normalization
///
/// Known limitation: whitespace-only text nodes between inline elements (e.g., a single
/// space between &lt;strong&gt; and &lt;em&gt;) are stripped during XML canonicalization.
/// This is acceptable for detecting formatting-level differences introduced by editors.
/// </summary>
public static partial class StorageFormatNormalizer
{
    private const string ConfluenceNamespaces =
        "xmlns:ac=\"http://atlassian.com/content\" " +
        "xmlns:ri=\"http://atlassian.com/resource/identifier\" " +
        "xmlns:at=\"http://atlassian.com/template\"";

    /// <summary>
    /// Normalizes line endings to LF (\n).
    /// Handles CRLF (\r\n) and standalone CR (\r) → LF.
    /// </summary>
    public static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    /// <summary>
    /// Compares two storage format strings after full normalization
    /// (line endings + XML canonicalization with fallback).
    /// </summary>
    public static bool ContentEquals(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            StringComparison.Ordinal);
    }

    internal static string NormalizeForComparison(string content)
    {
        var lineNormalized = NormalizeLineEndings(content);
        return TryCanonicalizeXml(lineNormalized) ?? lineNormalized;
    }

    private static string? TryCanonicalizeXml(string content)
    {
        try
        {
            var prepared = ReplaceHtmlNamedEntities(content);
            var wrapped = $"<cfx-root {ConfluenceNamespaces}>{prepared}</cfx-root>";
            var doc = XDocument.Parse(wrapped, LoadOptions.None);

            if (doc.Root is null) return null;

            CanonicalizeElement(doc.Root);
            return doc.Root.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return null;
        }
    }

    [GeneratedRegex(@"&([a-zA-Z][a-zA-Z0-9]*);", RegexOptions.Compiled)]
    private static partial Regex HtmlEntityPattern();

    /// <summary>
    /// Replaces HTML named entities (e.g. &amp;mdash; → &amp;#8212;) with numeric
    /// XML equivalents so the content can be parsed by a standard XML parser.
    /// The five predefined XML entities (amp, lt, gt, quot, apos) are left intact.
    /// </summary>
    private static string ReplaceHtmlNamedEntities(string content)
    {
        return HtmlEntityPattern().Replace(content, match =>
        {
            var entityName = match.Groups[1].Value;
            if (entityName is "amp" or "lt" or "gt" or "quot" or "apos")
                return match.Value;

            var decoded = WebUtility.HtmlDecode(match.Value);
            if (decoded == match.Value)
                return match.Value;

            if (decoded.Length == 1)
                return $"&#{(int)decoded[0]};";

            if (decoded.Length == 2
                && char.IsHighSurrogate(decoded[0])
                && char.IsLowSurrogate(decoded[1]))
                return $"&#{char.ConvertToUtf32(decoded[0], decoded[1])};";

            return match.Value;
        });
    }

    /// <summary>
    /// Recursively sorts attributes on every element by expanded name
    /// (namespace URI, then local name) to produce a deterministic order.
    /// </summary>
    private static void CanonicalizeElement(XElement element)
    {
        var sortedAttributes = element.Attributes()
            .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal)
            .ToList();

        element.ReplaceAttributes(sortedAttributes);

        foreach (var child in element.Elements())
            CanonicalizeElement(child);
    }
}
