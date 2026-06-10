namespace ConfluencePageExporter.Services;

/// <summary>
/// Computes and compares content hashes used to tell a real local edit apart
/// from an mtime-only touch (editor re-save, pretty-print, copy, <c>touch</c>,
/// VCS checkout, cloud-sync) when detecting double-edit conflicts. The hash is
/// taken over the <see cref="IContentNormalizer"/>-normalized body, so it ignores
/// exactly the cosmetic differences the equality comparison already ignores.
/// Bound to <see cref="NormalizationContract"/> for versioning and a runtime
/// self-check; when the regime can't be trusted, every method degrades to a
/// null/none result so callers fall back to mtime.
/// </summary>
public interface IContentHasher
{
    /// <summary>
    /// True when the active normalizer reproduces the golden contract — i.e. its
    /// output matches what stored hashes were computed under. False disables all
    /// hashing (callers fall back to mtime).
    /// </summary>
    bool RegimeTrusted { get; }

    /// <summary>Epoch to stamp into a marker, or null when <see cref="RegimeTrusted"/> is false.</summary>
    int? Epoch { get; }

    /// <summary>
    /// Marker hash (<c>"sha256:&lt;hex&gt;"</c>) of the normalized content, or null
    /// when the regime is untrusted or the content is null.
    /// </summary>
    string? ComputeHash(string? rawContent);

    /// <summary>
    /// Decides whether local content changed since the last sync, combining an
    /// mtime fast-path (untouched file ⇒ unchanged, no hashing) with the stored
    /// marker hash. Returns <c>true</c> = changed, <c>false</c> = unchanged,
    /// <c>null</c> = undeterminable (caller should fall back to mtime: no
    /// marker, regime untrusted, no stored hash, or stored epoch mismatch).
    /// <paramref name="localContentMaybe"/>, when provided, is reused to avoid
    /// re-reading <c>index.html</c>.
    /// </summary>
    bool? EvaluateLocalChange(
        PageMarker? marker,
        string? localContentMaybe,
        DateTime? indexMtimeUtc,
        DateTime? syncTimeUtc);
}
