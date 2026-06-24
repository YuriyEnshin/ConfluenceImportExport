using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// The parsed <c>.id</c> marker of a synced page directory — the single owner
/// of the marker file format and write policy. The file name carries the page
/// id and last-synced version (<c>.idPAGEID_VER</c>); the body carries the
/// original (unsanitised) title, the space key, the content hash of the
/// last-synced body and the normalization epoch that hash was computed under
/// (compact JSON — or, for legacy markers, a plain-text title). Any field may
/// be null: legacy markers predate space/hash capture.
/// <see cref="Load"/> reads everything in one directory scan;
/// <see cref="UpdateAsync"/> implements the shared "skip identical / upgrade
/// legacy" policy used by both the download and upload paths.
/// </summary>
public sealed record PageMarker(
    string PageId,
    int? Version,
    string? Title,
    string? SpaceKey,
    string? ContentHash,
    int? NormalizationEpoch,
    DateTime? FileTimeUtc,
    IReadOnlyDictionary<string, AttachmentBaseline>? Attachments = null)
{
    /// <summary>
    /// Serialisation options for the marker body JSON. Null title/space are
    /// omitted; the relaxed encoder keeps non-ASCII (e.g. Cyrillic) titles
    /// human-readable on disk instead of \uXXXX-escaping them.
    /// </summary>
    private static readonly JsonSerializerOptions MarkerJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private sealed class MarkerBody
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("space")] public string? Space { get; set; }
        [JsonPropertyName("h")] public string? Hash { get; set; }
        [JsonPropertyName("ne")] public int? Ne { get; set; }
        [JsonPropertyName("att")] public Dictionary<string, AttBody>? Att { get; set; }
    }

    private sealed class AttBody
    {
        [JsonPropertyName("n")] public string? Name { get; set; }
        [JsonPropertyName("v")] public int? Version { get; set; }
        [JsonPropertyName("h")] public string? Hash { get; set; }
        [JsonPropertyName("s")] public long? Size { get; set; }
    }

    /// <summary>
    /// Parses the id/version from a marker file name (<c>.idPAGEID</c> or
    /// <c>.idPAGEID_VER</c>). Exposed separately from <see cref="Load"/> for
    /// bulk scans that already enumerate marker files themselves.
    /// </summary>
    public static PageMarkerInfo ParseFileName(string fileName)
    {
        var markerValue = fileName[3..];
        var underscoreIdx = markerValue.LastIndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(markerValue[(underscoreIdx + 1)..], out var version))
            return new PageMarkerInfo(markerValue[..underscoreIdx], version);
        return new PageMarkerInfo(markerValue, null);
    }

    /// <summary>
    /// Reads the page directory's marker in a single scan: file name (id,
    /// version), body (title, space, hash, epoch) and the file's mtime — which
    /// doubles as the last-sync timestamp. Returns null when the directory or
    /// marker does not exist.
    /// </summary>
    public static PageMarker? Load(string pageDir)
    {
        var markerFile = FindMarkerFile(pageDir);
        if (markerFile == null)
            return null;

        var info = ParseFileName(Path.GetFileName(markerFile));
        var (title, space, hash, epoch, attachments) = ParseBody(File.ReadAllText(markerFile));
        return new PageMarker(
            info.PageId, info.Version, title, space, hash, epoch,
            File.GetLastWriteTimeUtc(markerFile), attachments);
    }

    private static string? FindMarkerFile(string pageDir)
    {
        if (!Directory.Exists(pageDir))
            return null;

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".id") && fileName.Length > 3)
                return file;
        }
        return null;
    }

    /// <summary>
    /// Unconditionally (re)writes the marker, deleting any previous one.
    /// Prefer <see cref="UpdateAsync"/>, which applies the skip/upgrade policy.
    /// </summary>
    public static async Task WriteAsync(
        string pageDir, string pageId, int? version = null, string? originalTitle = null,
        string? spaceKey = null, string? contentHash = null, int? normalizationEpoch = null,
        IReadOnlyDictionary<string, AttachmentBaseline>? attachments = null,
        CancellationToken ct = default)
    {
        if (!Directory.Exists(pageDir))
            throw new DirectoryNotFoundException($"Page directory does not exist: {pageDir}");

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            File.Delete(file);
        }

        var markerName = version.HasValue ? $".id{pageId}_{version.Value}" : $".id{pageId}";
        var markerPath = Path.Combine(pageDir, markerName);
        await File.WriteAllTextAsync(markerPath, SerializeBody(originalTitle, spaceKey, contentHash, normalizationEpoch, attachments), ct);
    }

    /// <summary>
    /// Stamps the marker after a sync, hashing <paramref name="syncedContent"/>
    /// (the body that is now the synced baseline) with <paramref name="hasher"/>.
    /// The hash is null when there is no body or the hashing regime is
    /// untrusted — then the previously stored hash (if any) is preserved rather
    /// than dropped. The rewrite is skipped only when id, version, title
    /// presence, space AND the content hash all already match, so a legacy
    /// (space-less / hash-less) marker is upgraded once the space or hash is
    /// known. Returns true when the marker was (re)written.
    /// </summary>
    public static async Task<bool> UpdateAsync(
        string pageDir, string pageId, int? version, string? originalTitle,
        string? spaceKey, string? syncedContent, IContentHasher hasher,
        IReadOnlyDictionary<string, AttachmentBaseline>? attachments = null,
        CancellationToken ct = default)
    {
        var contentHash = hasher.ComputeHash(syncedContent);

        var existing = Load(pageDir);

        // A null attachment map means "this operation didn't sync attachments" —
        // preserve whatever the marker already holds (mirrors the content-hash
        // preservation below). A non-null map (even empty) is the authoritative
        // current set and replaces it.
        var effectiveAttachments = attachments ?? existing?.Attachments;

        if (existing != null
            && string.Equals(existing.PageId, pageId, StringComparison.OrdinalIgnoreCase)
            && existing.Version == version
            && existing.Title != null
            && string.Equals(existing.SpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase)
            && (contentHash == null || string.Equals(existing.ContentHash, contentHash, StringComparison.Ordinal))
            && AttachmentMapsEqual(existing.Attachments, effectiveAttachments))
        {
            return false;
        }

        await WriteAsync(
            pageDir, pageId, version, originalTitle, spaceKey,
            contentHash ?? existing?.ContentHash,
            contentHash != null ? hasher.Epoch : existing?.NormalizationEpoch,
            effectiveAttachments,
            ct);
        return true;
    }

    /// <summary>
    /// Order-independent equality of two attachment baseline maps (null/empty
    /// treated as equal). Used by <see cref="UpdateAsync"/> to keep the
    /// "skip identical marker" optimisation — so an unchanged page with unchanged
    /// attachments doesn't get a needless marker rewrite (which would also move
    /// the marker mtime that doubles as the sync timestamp).
    /// </summary>
    private static bool AttachmentMapsEqual(
        IReadOnlyDictionary<string, AttachmentBaseline>? a,
        IReadOnlyDictionary<string, AttachmentBaseline>? b)
    {
        var countA = a?.Count ?? 0;
        var countB = b?.Count ?? 0;
        if (countA != countB)
            return false;
        if (countA == 0)
            return true;

        foreach (var (key, value) in a!)
            if (!b!.TryGetValue(key, out var other) || other != value)
                return false;
        return true;
    }

    /// <summary>
    /// Builds the marker file body. When a space key or a content hash is known
    /// the body is a compact JSON object (<c>{"title":…,"space":…,"h":…,"ne":…}</c>);
    /// otherwise the legacy plain-text title is written verbatim so older tooling
    /// keeps reading it unchanged. This keeps upgrade churn at zero until a sync
    /// captures space or hash. The normalization epoch is only emitted alongside
    /// a hash (it is meaningless on its own).
    /// </summary>
    private static string SerializeBody(
        string? title, string? spaceKey, string? contentHash, int? normalizationEpoch,
        IReadOnlyDictionary<string, AttachmentBaseline>? attachments)
    {
        var hasAttachments = attachments is { Count: > 0 };
        if (string.IsNullOrEmpty(spaceKey) && string.IsNullOrEmpty(contentHash) && !hasAttachments)
            return title ?? string.Empty;

        return JsonSerializer.Serialize(
            new MarkerBody
            {
                Title = string.IsNullOrEmpty(title) ? null : title,
                Space = string.IsNullOrEmpty(spaceKey) ? null : spaceKey,
                Hash = string.IsNullOrEmpty(contentHash) ? null : contentHash,
                Ne = string.IsNullOrEmpty(contentHash) ? null : normalizationEpoch,
                Att = hasAttachments
                    ? attachments!.ToDictionary(
                        kv => kv.Key,
                        kv => new AttBody
                        {
                            Name = kv.Value.ServerName,
                            Version = kv.Value.Version,
                            Hash = kv.Value.Hash,
                            Size = kv.Value.Size,
                        })
                    : null,
            },
            MarkerJsonOptions);
    }

    /// <summary>
    /// Parses a marker body written by <see cref="SerializeBody"/> or by any
    /// earlier version. New format: a JSON object with optional fields. Legacy
    /// format: the raw text is the title and everything else is unknown. A body
    /// that starts with '{' but does not parse to our shape is treated as a
    /// legacy title (defensive — a real Confluence title is extremely unlikely
    /// to be a JSON object with these keys).
    /// </summary>
    private static (string? Title, string? Space, string? Hash, int? Epoch,
        IReadOnlyDictionary<string, AttachmentBaseline>? Attachments) ParseBody(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return (null, null, null, null, null);

        if (trimmed[0] == '{')
        {
            try
            {
                var body = JsonSerializer.Deserialize<MarkerBody>(trimmed, MarkerJsonOptions);
                if (body != null && (body.Title != null || body.Space != null || body.Hash != null || body.Att != null))
                    return (
                        string.IsNullOrWhiteSpace(body.Title) ? null : body.Title,
                        string.IsNullOrWhiteSpace(body.Space) ? null : body.Space,
                        string.IsNullOrWhiteSpace(body.Hash) ? null : body.Hash,
                        body.Ne,
                        ParseAttachments(body.Att));
            }
            catch (JsonException)
            {
                // Not our JSON — fall through and treat it as a legacy plain title.
            }
        }

        return (trimmed, null, null, null, null);
    }

    private static IReadOnlyDictionary<string, AttachmentBaseline>? ParseAttachments(Dictionary<string, AttBody>? att)
    {
        if (att is not { Count: > 0 })
            return null;

        var map = new Dictionary<string, AttachmentBaseline>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in att)
            map[key] = new AttachmentBaseline(value.Name, value.Version, value.Hash, value.Size);
        return map;
    }
}
