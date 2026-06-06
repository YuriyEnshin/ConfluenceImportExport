using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace ConfluencePageExporter.Services;

/// <summary>
/// XML-based content normalizer using System.Xml.Linq.
/// Provides accurate canonicalization by parsing XHTML fragments as XML,
/// normalizing whitespace, sorting attributes, and serializing back.
/// Falls back to line-ending-only normalization on parse errors.
/// </summary>
/// <remarks>
/// ⚠️ CONTENT-HASH CONTRACT: this normalizer's output is hashed and persisted in
/// page markers (.id*) for double-edit conflict detection. If you change its
/// behaviour (whitespace / attribute / entity handling), you MUST bump
/// <see cref="NormalizationContract.CurrentEpoch"/> and refresh its golden value,
/// or stored hashes silently mismatch. The golden-vector test fails until you do.
/// See .cursor/rules/confluence-import-export.mdc §11.
/// </remarks>
public sealed class XmlContentNormalizer : IContentNormalizer
{
    public bool ContentEquals(string? left, string? right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left is null || right is null) return false;

        return string.Equals(
            NormalizeForComparison(left),
            NormalizeForComparison(right),
            StringComparison.Ordinal);
    }

    public string NormalizeForComparison(string content)
    {
        var lineNormalized = NormalizeLineEndings(content);
        return TryCanonicalize(lineNormalized) ?? lineNormalized;
    }

    private static string NormalizeLineEndings(string content)
    {
        return content.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    private static readonly Regex HtmlEntityRegex = new(@"&([a-zA-Z][a-zA-Z0-9]*);", RegexOptions.Compiled);

    private static string ReplaceHtmlNamedEntities(string content)
    {
        return HtmlEntityRegex.Replace(content, match =>
        {
            var entityName = match.Groups[1].Value;
            if (entityName is "amp" or "lt" or "gt" or "quot" or "apos")
                return match.Value;

            if (StorageFormatNormalizer.HtmlEntities.TryGetCodePoint(entityName, out var codePoint))
                return char.ConvertFromUtf32(codePoint);

            return match.Value;
        });
    }

    private static string? TryCanonicalize(string content)
    {
        try
        {
            content = ReplaceHtmlNamedEntities(content);

            var wrapped = $"<_root_ xmlns:ac=\"http://atlassian.com/confluence\" " +
                          $"xmlns:ri=\"http://atlassian.com/ri\">{content}</_root_>";

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Ignore,
                XmlResolver = null,
                ConformanceLevel = ConformanceLevel.Document
            };

            XDocument doc;
            using (var reader = XmlReader.Create(new StringReader(wrapped), settings))
            {
                doc = XDocument.Load(reader, LoadOptions.None);
            }

            NormalizeNode(doc.Root!);

            var sb = new StringBuilder();
            var writerSettings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                NamespaceHandling = NamespaceHandling.OmitDuplicates
            };

            foreach (var node in doc.Root!.Nodes())
            {
                using var writer = XmlWriter.Create(sb, writerSettings);
                node.WriteTo(writer);
            }

            return RemoveSyntheticNamespaceDeclarations(sb.ToString());
        }
        catch
        {
            return null;
        }
    }

    private static void NormalizeNode(XElement element)
    {
        RemoveInsignificantWhitespace(element);
        SortAttributes(element);

        foreach (var child in element.Elements())
            NormalizeNode(child);
    }

    private static void RemoveInsignificantWhitespace(XElement element)
    {
        var nodesToRemove = new List<XNode>();

        foreach (var node in element.Nodes())
        {
            if (node is XText textNode && string.IsNullOrWhiteSpace(textNode.Value) && element.HasElements)
            {
                nodesToRemove.Add(node);
            }
        }

        foreach (var node in nodesToRemove)
            node.Remove();
    }

    private static void SortAttributes(XElement element)
    {
        var attrs = element.Attributes()
            .Where(a => !a.IsNamespaceDeclaration)
            .OrderBy(a => a.Name.NamespaceName, StringComparer.Ordinal)
            .ThenBy(a => a.Name.LocalName, StringComparer.Ordinal)
            .ToList();

        var nsAttrs = element.Attributes()
            .Where(a => a.IsNamespaceDeclaration)
            .ToList();

        element.RemoveAttributes();

        foreach (var ns in nsAttrs)
            element.Add(ns);
        foreach (var attr in attrs)
            element.Add(attr);
    }

    private static string RemoveSyntheticNamespaceDeclarations(string xml)
    {
        return xml
            .Replace(" xmlns:ac=\"http://atlassian.com/confluence\"", "")
            .Replace(" xmlns:ri=\"http://atlassian.com/ri\"", "");
    }
}
