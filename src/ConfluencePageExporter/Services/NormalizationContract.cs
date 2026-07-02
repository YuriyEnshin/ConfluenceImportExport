namespace ConfluencePageExporter.Services;

/// <summary>
/// Versioned identity of the content-canonicalisation + hashing recipe whose
/// output is persisted in page markers (<c>.id*</c>) and used for double-edit
/// conflict detection (see <see cref="ContentHasher"/>). A stored hash is only
/// trusted when both the epoch matches <see cref="CurrentEpoch"/> and the active
/// normalizer still reproduces <see cref="GoldenNormalized"/> from
/// <see cref="GoldenInput"/> at runtime; otherwise hashing is disabled and the
/// caller falls back to file-mtime comparison.
///
/// ⚠️ CONTENT-HASH CONTRACT — READ BEFORE TOUCHING THE NORMALIZERS:
///    Bump <see cref="CurrentEpoch"/> AND refresh <see cref="GoldenNormalized"/>
///    whenever ANYTHING can change normalized output: edits to
///    XmlContentNormalizer / RegexContentNormalizer / the HtmlEntities table /
///    attribute-sort / whitespace rules / volatile-artifact stripping / the hash
///    algorithm, or switching the active IContentNormalizer. Stored hashes
///    computed under the old recipe will otherwise silently mismatch. The
///    golden-vector unit test fails until you update both, which is the intended
///    tripwire. See .cursor/rules/confluence-import-export.mdc §11.
/// </summary>
public static class NormalizationContract
{
    /// <summary>
    /// Epoch stamped into markers alongside a freshly computed hash.
    /// History: 1 = initial; 2 = strip ac:macro-id and empty ac:parameter;
    /// 3 = also strip ac:schema-version and local-id / ac:local-id
    /// (server- and Cloud-editor-assigned noise, verified live on Cloud).
    /// </summary>
    public const int CurrentEpoch = 3;

    /// <summary>Algorithm tag prefixed to every stored hash, making it self-describing.</summary>
    public const string HashPrefix = "sha256:";

    /// <summary>
    /// Golden canary input. Deliberately exercises the dimensions normalization
    /// touches: out-of-order attributes, a named HTML entity, inter-tag
    /// whitespace, element nesting, stripped <c>ac:macro-id</c> /
    /// <c>ac:schema-version</c> / <c>ac:local-id</c> / <c>local-id</c>, a
    /// dropped empty <c>ac:parameter</c>, and a retained named parameter.
    /// </summary>
    internal const string GoldenInput =
        "<p  local-id=\"p-loc\" class=\"b\" id=\"a\">x&nbsp;<b>y</b>  </p>" +
        "<ac:structured-macro ac:macro-id=\"abc-123\" ac:schema-version=\"1\" ac:local-id=\"m-loc\" ac:name=\"note\">" +
        "<ac:parameter ac:name=\"\" /><ac:parameter ac:name=\"title\">T</ac:parameter>" +
        "</ac:structured-macro>";

    /// <summary>
    /// Expected <c>NormalizeForComparison(GoldenInput)</c> under
    /// <see cref="CurrentEpoch"/>, captured from the default XmlContentNormalizer.
    /// Contains a non-breaking space (U+00A0, decoded from <c>&amp;nbsp;</c>) — not
    /// an ASCII space. If the active normalizer/runtime no longer reproduces this
    /// exact string, the hashing regime is "untrusted" and conflict detection
    /// falls back to mtime — safely and self-healingly.
    /// </summary>
    internal const string GoldenNormalized =
        "<p class=\"b\" id=\"a\">x <b>y</b></p>" +
        "<ac:structured-macro ac:name=\"note\">" +
        "<ac:parameter ac:name=\"title\">T</ac:parameter></ac:structured-macro>";
}
