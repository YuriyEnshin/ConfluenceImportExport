namespace ConfluencePageExporter.Services;

/// <summary>
/// Snapshot of a page directory's local sync state, read once per page: the
/// parsed <c>.id</c> marker (or null), the mtime of <c>index.html</c>, and the
/// hash-based verdict on whether local content changed since the last sync
/// (see <see cref="IContentHasher.EvaluateLocalChange"/>; null = no usable
/// hash, caller falls back to mtime). Replaces the read-marker / read-mtimes /
/// evaluate ritual that upload, download and compare each used to inline.
/// </summary>
public sealed record LocalSyncState(
    PageMarker? Marker,
    DateTime? LocalFileTimeUtc,
    bool? LocalContentChanged)
{
    /// <summary>Last-synced version from the marker, or null without a marker.</summary>
    public int? MarkerVersion => Marker?.Version;

    /// <summary>Last sync time — the marker file's mtime, or null without a marker.</summary>
    public DateTime? SyncTimeUtc => Marker?.FileTimeUtc;

    /// <summary>
    /// Reads the directory's sync state. <paramref name="localContent"/> is the
    /// already-read <c>index.html</c> body (or null when absent) — passed in so
    /// the file is not read twice.
    /// </summary>
    public static LocalSyncState Read(string pageDir, string? localContent, IContentHasher hasher)
    {
        var marker = PageMarker.Load(pageDir);
        var indexPath = Path.Combine(pageDir, "index.html");
        DateTime? localFileTime = File.Exists(indexPath) ? File.GetLastWriteTimeUtc(indexPath) : null;
        var localContentChanged = hasher.EvaluateLocalChange(marker, localContent, localFileTime, marker?.FileTimeUtc);
        return new LocalSyncState(marker, localFileTime, localContentChanged);
    }
}
