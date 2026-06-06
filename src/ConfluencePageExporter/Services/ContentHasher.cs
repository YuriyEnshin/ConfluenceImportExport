using System.Security.Cryptography;
using System.Text;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Default <see cref="IContentHasher"/>: SHA-256 over the normalized storage
/// format, gated by a runtime golden-vector self-check against
/// <see cref="NormalizationContract"/>. Uses the SAME <see cref="IContentNormalizer"/>
/// as content-equality comparison, so "hash says unchanged" is consistent with
/// "ContentEquals says unchanged" on a given machine.
/// </summary>
public sealed class ContentHasher : IContentHasher
{
    private readonly IContentNormalizer _normalizer;
    private readonly bool _regimeTrusted;

    public ContentHasher(IContentNormalizer normalizer)
    {
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));

        // Self-check: does the active normalizer still reproduce the canonical
        // form that stored hashes were computed under? If not (normalizer swapped,
        // its code changed without an epoch bump, or the XML stack drifted), the
        // regime is untrusted and we never emit or trust a hash.
        _regimeTrusted = string.Equals(
            _normalizer.NormalizeForComparison(NormalizationContract.GoldenInput),
            NormalizationContract.GoldenNormalized,
            StringComparison.Ordinal);
    }

    public bool RegimeTrusted => _regimeTrusted;

    public int? Epoch => _regimeTrusted ? NormalizationContract.CurrentEpoch : null;

    public string? ComputeHash(string? rawContent)
    {
        if (!_regimeTrusted || rawContent is null)
            return null;

        var normalized = _normalizer.NormalizeForComparison(rawContent);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return NormalizationContract.HashPrefix + Convert.ToHexStringLower(digest);
    }

    public bool? EvaluateLocalChange(
        PageMarkerContent marker,
        string? localContentMaybe,
        DateTime? indexMtimeUtc,
        DateTime? syncTimeUtc)
    {
        if (!_regimeTrusted || string.IsNullOrEmpty(marker.ContentHash))
            return null;

        // mtime fast-path: the file was not touched after the last sync, so the
        // content cannot have changed — skip hashing entirely.
        if (indexMtimeUtc.HasValue && syncTimeUtc.HasValue && indexMtimeUtc.Value <= syncTimeUtc.Value)
            return false;

        if (localContentMaybe is null)
            return null;

        return HasChanged(marker.ContentHash, marker.NormalizationEpoch, localContentMaybe);
    }

    private bool? HasChanged(string? storedHash, int? storedEpoch, string rawCurrent)
    {
        if (string.IsNullOrEmpty(storedHash) || storedEpoch != NormalizationContract.CurrentEpoch)
            return null;

        var current = ComputeHash(rawCurrent);
        return current is null ? null : !string.Equals(current, storedHash, StringComparison.Ordinal);
    }
}
