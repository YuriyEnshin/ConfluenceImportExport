using System.Text;
using System.Text.RegularExpressions;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Regex-based content normalizer that avoids System.Xml entirely. Kept as a
/// one-line-swappable backup for <see cref="XmlContentNormalizer"/> (the default).
/// During the macOS ARM64 crash investigation — root cause was a compressed
/// single-file runtime bug (dotnet/runtime#123324), fixed by disabling
/// single-file compression, NOT by this class — System.Xml was briefly suspected
/// of thread-unsafety, so a System.Xml-free path was kept around to swap in and
/// compare. Less accurate than XML-based normalization but has no native
/// dependencies.
/// </summary>
/// <remarks>
/// ⚠️ CONTENT-HASH CONTRACT: see <see cref="XmlContentNormalizer"/> and
/// <see cref="NormalizationContract"/>. This normalizer produces DIFFERENT
/// canonical output from the XML one; swapping it in makes the golden-vector
/// self-check fail, which by design disables hashing and falls back to mtime
/// until the epoch/golden are updated for it.
/// </remarks>
public sealed partial class RegexContentNormalizer : IContentNormalizer
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

    private static string? TryCanonicalize(string content)
    {
        try
        {
            var result = ReplaceHtmlNamedEntities(content);
            result = StripInterTagWhitespace(result);
            result = NormalizeTagAttributes(result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    // ── HTML Entity Replacement ─────────────────────────────────────────

    [GeneratedRegex(@"&([a-zA-Z][a-zA-Z0-9]*);")]
    private static partial Regex HtmlEntityPattern();

    private static string ReplaceHtmlNamedEntities(string content)
    {
        var matches = HtmlEntityPattern().Matches(content);
        if (matches.Count == 0)
            return content;

        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (Match match in matches)
        {
            var entity = match.Value;
            if (replacements.ContainsKey(entity))
                continue;

            var entityName = match.Groups[1].Value;
            if (entityName is "amp" or "lt" or "gt" or "quot" or "apos")
                continue;

            if (StorageFormatNormalizer.HtmlEntities.TryGetCodePoint(entityName, out var codePoint))
                replacements[entity] = char.ConvertFromUtf32(codePoint);
        }

        if (replacements.Count == 0)
            return content;

        var result = content;
        foreach (var (original, replacement) in replacements)
            result = result.Replace(original, replacement);

        return result;
    }

    // ── Inter-tag Whitespace Stripping ──────────────────────────────────

    [GeneratedRegex(@">\s+<")]
    private static partial Regex InterTagWhitespacePattern();

    private static string StripInterTagWhitespace(string content)
    {
        return InterTagWhitespacePattern().Replace(content, "><");
    }

    // ── Attribute Sorting ───────────────────────────────────────────────

    [GeneratedRegex(
        @"<([a-zA-Z][a-zA-Z0-9:._-]*)" +
        @"(\s+[^>]*?)" +
        @"(\s*/?>)",
        RegexOptions.Singleline)]
    private static partial Regex OpeningTagPattern();

    [GeneratedRegex(
        @"([a-zA-Z][a-zA-Z0-9:._-]*)\s*=\s*""([^""]*)""")]
    private static partial Regex AttributePattern();

    private static string NormalizeTagAttributes(string content)
    {
        return OpeningTagPattern().Replace(content, match =>
        {
            var tagName = match.Groups[1].Value;
            var attrsBlock = match.Groups[2].Value;
            var closing = match.Groups[3].Value.TrimStart();

            var attrMatches = AttributePattern().Matches(attrsBlock);
            if (attrMatches.Count == 0)
                return $"<{tagName}{closing}";

            var attrs = new List<(string Name, string Value)>(attrMatches.Count);
            foreach (Match am in attrMatches)
                attrs.Add((am.Groups[1].Value, am.Groups[2].Value));

            attrs.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));

            var sb = new StringBuilder();
            sb.Append('<').Append(tagName);
            foreach (var (name, value) in attrs)
                sb.Append(' ').Append(name).Append("=\"").Append(value).Append('"');
            sb.Append(closing);

            return sb.ToString();
        });
    }
}
