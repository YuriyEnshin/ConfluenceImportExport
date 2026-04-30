using System.Text;
using System.Text.RegularExpressions;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Normalizes Confluence storage format content before comparison.
///
/// Two-stage normalization:
/// 1. Line endings: CRLF / standalone CR → LF
/// 2. Canonicalization (regex-based, no System.Xml dependency):
///    - Replaces HTML named entities with numeric equivalents
///    - Strips whitespace-only text between tags (indentation)
///    - Sorts attributes alphabetically within each tag
///    - Normalizes self-closing tag spacing
///    - On any failure, falls back to line-ending-only normalization
///
/// Known limitation: whitespace-only text nodes between inline elements (e.g., a single
/// space between &lt;strong&gt; and &lt;em&gt;) are stripped during canonicalization.
/// This is acceptable for detecting formatting-level differences introduced by editors.
/// </summary>
public static partial class StorageFormatNormalizer
{
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
    /// (line endings + canonicalization with fallback).
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
        return TryCanonicalize(lineNormalized) ?? lineNormalized;
    }

    /// <summary>
    /// Regex-based canonicalization that avoids System.Xml entirely.
    /// This prevents AccessViolationException on macOS ARM64 in single-file
    /// compressed apps (dotnet/runtime#123324).
    /// </summary>
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

    /// <summary>
    /// Replaces HTML named entities (e.g. &amp;mdash; → the Unicode character —)
    /// with their decoded Unicode equivalents. Uses a static lookup table instead
    /// of WebUtility.HtmlDecode to avoid runtime issues on macOS ARM64.
    /// The five predefined XML entities (amp, lt, gt, quot, apos) are left intact.
    /// </summary>
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

            if (HtmlEntities.TryGetCodePoint(entityName, out var codePoint))
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
        @"<([a-zA-Z][a-zA-Z0-9:._-]*)" +    // tag name (group 1)
        @"(\s+[^>]*?)" +                      // attributes block (group 2)
        @"(\s*/?>)",                           // closing: /> or > (group 3)
        RegexOptions.Singleline)]
    private static partial Regex OpeningTagPattern();

    [GeneratedRegex(
        @"([a-zA-Z][a-zA-Z0-9:._-]*)\s*=\s*""([^""]*)""")]
    private static partial Regex AttributePattern();

    /// <summary>
    /// Sorts attributes alphabetically within each opening/self-closing tag.
    /// Normalizes self-closing tag spacing (removes space before />).
    /// </summary>
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

    // ── HTML Entity Lookup Table ────────────────────────────────────────

    /// <summary>
    /// Static dictionary of HTML named character references → Unicode code points.
    /// Covers all entities commonly found in Confluence storage format.
    /// </summary>
    private static class HtmlEntities
    {
        private static readonly Dictionary<string, int> Entities = new(StringComparer.Ordinal)
        {
            // Latin-1 supplement
            ["nbsp"] = 160,
            ["iexcl"] = 161,
            ["cent"] = 162,
            ["pound"] = 163,
            ["curren"] = 164,
            ["yen"] = 165,
            ["brvbar"] = 166,
            ["sect"] = 167,
            ["uml"] = 168,
            ["copy"] = 169,
            ["ordf"] = 170,
            ["laquo"] = 171,
            ["not"] = 172,
            ["shy"] = 173,
            ["reg"] = 174,
            ["macr"] = 175,
            ["deg"] = 176,
            ["plusmn"] = 177,
            ["sup2"] = 178,
            ["sup3"] = 179,
            ["acute"] = 180,
            ["micro"] = 181,
            ["para"] = 182,
            ["middot"] = 183,
            ["cedil"] = 184,
            ["sup1"] = 185,
            ["ordm"] = 186,
            ["raquo"] = 187,
            ["frac14"] = 188,
            ["frac12"] = 189,
            ["frac34"] = 190,
            ["iquest"] = 191,
            ["Agrave"] = 192,
            ["Aacute"] = 193,
            ["Acirc"] = 194,
            ["Atilde"] = 195,
            ["Auml"] = 196,
            ["Aring"] = 197,
            ["AElig"] = 198,
            ["Ccedil"] = 199,
            ["Egrave"] = 200,
            ["Eacute"] = 201,
            ["Ecirc"] = 202,
            ["Euml"] = 203,
            ["Igrave"] = 204,
            ["Iacute"] = 205,
            ["Icirc"] = 206,
            ["Iuml"] = 207,
            ["ETH"] = 208,
            ["Ntilde"] = 209,
            ["Ograve"] = 210,
            ["Oacute"] = 211,
            ["Ocirc"] = 212,
            ["Otilde"] = 213,
            ["Ouml"] = 214,
            ["times"] = 215,
            ["Oslash"] = 216,
            ["Ugrave"] = 217,
            ["Uacute"] = 218,
            ["Ucirc"] = 219,
            ["Uuml"] = 220,
            ["Yacute"] = 221,
            ["THORN"] = 222,
            ["szlig"] = 223,
            ["agrave"] = 224,
            ["aacute"] = 225,
            ["acirc"] = 226,
            ["atilde"] = 227,
            ["auml"] = 228,
            ["aring"] = 229,
            ["aelig"] = 230,
            ["ccedil"] = 231,
            ["egrave"] = 232,
            ["eacute"] = 233,
            ["ecirc"] = 234,
            ["euml"] = 235,
            ["igrave"] = 236,
            ["iacute"] = 237,
            ["icirc"] = 238,
            ["iuml"] = 239,
            ["eth"] = 240,
            ["ntilde"] = 241,
            ["ograve"] = 242,
            ["oacute"] = 243,
            ["ocirc"] = 244,
            ["otilde"] = 245,
            ["ouml"] = 246,
            ["divide"] = 247,
            ["oslash"] = 248,
            ["ugrave"] = 249,
            ["uacute"] = 250,
            ["ucirc"] = 251,
            ["uuml"] = 252,
            ["yacute"] = 253,
            ["thorn"] = 254,
            ["yuml"] = 255,
            // General punctuation & typography
            ["ndash"] = 8211,
            ["mdash"] = 8212,
            ["lsquo"] = 8216,
            ["rsquo"] = 8217,
            ["sbquo"] = 8218,
            ["ldquo"] = 8220,
            ["rdquo"] = 8221,
            ["bdquo"] = 8222,
            ["dagger"] = 8224,
            ["Dagger"] = 8225,
            ["bull"] = 8226,
            ["hellip"] = 8230,
            ["permil"] = 8240,
            ["prime"] = 8242,
            ["Prime"] = 8243,
            ["lsaquo"] = 8249,
            ["rsaquo"] = 8250,
            ["oline"] = 8254,
            ["frasl"] = 8260,
            ["euro"] = 8364,
            ["trade"] = 8482,
            // Mathematical / technical
            ["forall"] = 8704,
            ["part"] = 8706,
            ["exist"] = 8707,
            ["empty"] = 8709,
            ["nabla"] = 8711,
            ["isin"] = 8712,
            ["notin"] = 8713,
            ["ni"] = 8715,
            ["prod"] = 8719,
            ["sum"] = 8721,
            ["minus"] = 8722,
            ["lowast"] = 8727,
            ["radic"] = 8730,
            ["prop"] = 8733,
            ["infin"] = 8734,
            ["ang"] = 8736,
            ["and"] = 8743,
            ["or"] = 8744,
            ["cap"] = 8745,
            ["cup"] = 8746,
            ["int"] = 8747,
            ["there4"] = 8756,
            ["sim"] = 8764,
            ["cong"] = 8773,
            ["asymp"] = 8776,
            ["ne"] = 8800,
            ["equiv"] = 8801,
            ["le"] = 8804,
            ["ge"] = 8805,
            ["sub"] = 8834,
            ["sup"] = 8835,
            ["nsub"] = 8836,
            ["sube"] = 8838,
            ["supe"] = 8839,
            ["oplus"] = 8853,
            ["otimes"] = 8855,
            ["perp"] = 8869,
            ["sdot"] = 8901,
            // Arrows
            ["larr"] = 8592,
            ["uarr"] = 8593,
            ["rarr"] = 8594,
            ["darr"] = 8595,
            ["harr"] = 8596,
            ["lArr"] = 8656,
            ["uArr"] = 8657,
            ["rArr"] = 8658,
            ["dArr"] = 8659,
            ["hArr"] = 8660,
            // Greek
            ["Alpha"] = 913,
            ["Beta"] = 914,
            ["Gamma"] = 915,
            ["Delta"] = 916,
            ["Epsilon"] = 917,
            ["Zeta"] = 918,
            ["Eta"] = 919,
            ["Theta"] = 920,
            ["Iota"] = 921,
            ["Kappa"] = 922,
            ["Lambda"] = 923,
            ["Mu"] = 924,
            ["Nu"] = 925,
            ["Xi"] = 926,
            ["Omicron"] = 927,
            ["Pi"] = 928,
            ["Rho"] = 929,
            ["Sigma"] = 931,
            ["Tau"] = 932,
            ["Upsilon"] = 933,
            ["Phi"] = 934,
            ["Chi"] = 935,
            ["Psi"] = 936,
            ["Omega"] = 937,
            ["alpha"] = 945,
            ["beta"] = 946,
            ["gamma"] = 947,
            ["delta"] = 948,
            ["epsilon"] = 949,
            ["zeta"] = 950,
            ["eta"] = 951,
            ["theta"] = 952,
            ["iota"] = 953,
            ["kappa"] = 954,
            ["lambda"] = 955,
            ["mu"] = 956,
            ["nu"] = 957,
            ["xi"] = 958,
            ["omicron"] = 959,
            ["pi"] = 960,
            ["rho"] = 961,
            ["sigmaf"] = 962,
            ["sigma"] = 963,
            ["tau"] = 964,
            ["upsilon"] = 965,
            ["phi"] = 966,
            ["chi"] = 967,
            ["psi"] = 968,
            ["omega"] = 969,
            ["thetasym"] = 977,
            ["upsih"] = 978,
            ["piv"] = 982,
            // Spacing / formatting
            ["ensp"] = 8194,
            ["emsp"] = 8195,
            ["thinsp"] = 8201,
            ["zwnj"] = 8204,
            ["zwj"] = 8205,
            ["lrm"] = 8206,
            ["rlm"] = 8207,
            // Miscellaneous symbols
            ["spades"] = 9824,
            ["clubs"] = 9827,
            ["hearts"] = 9829,
            ["diams"] = 9830,
            // Latin Extended
            ["OElig"] = 338,
            ["oelig"] = 339,
            ["Scaron"] = 352,
            ["scaron"] = 353,
            ["Yuml"] = 376,
            ["fnof"] = 402,
            ["circ"] = 710,
            ["tilde"] = 732,
        };

        public static bool TryGetCodePoint(string entityName, out int codePoint)
            => Entities.TryGetValue(entityName, out codePoint);
    }
}
