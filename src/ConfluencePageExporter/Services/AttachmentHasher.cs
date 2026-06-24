using System.Security.Cryptography;

namespace ConfluencePageExporter.Services;

/// <summary>
/// SHA-256 over the raw bytes of an attachment, tagged with a self-describing
/// <c>sha256:</c> prefix. Distinct from <see cref="IContentHasher"/>, which
/// hashes <em>normalized storage-format text</em> and is bound to the
/// <see cref="NormalizationContract"/> epoch: attachments are opaque binary, so
/// there is no normalization and no epoch — the hash recipe is fixed.
/// </summary>
public static class AttachmentHasher
{
    public const string HashPrefix = "sha256:";

    public static string ComputeHash(byte[] bytes) =>
        HashPrefix + Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static async Task<string> ComputeFileHashAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = File.OpenRead(filePath);
        var digest = await SHA256.HashDataAsync(stream, ct);
        return HashPrefix + Convert.ToHexStringLower(digest);
    }
}
