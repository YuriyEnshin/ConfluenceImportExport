namespace ConfluencePageExporter.Models;

/// <summary>
/// Per-attachment sync baseline persisted in a page's <c>.id</c> marker (under
/// the <c>att</c> map, keyed by the sanitised local file name). It records:
/// <list type="bullet">
/// <item><see cref="ServerName"/> — the full, unsanitised Confluence attachment
/// title, so a sanitised local file round-trips back to the exact server
/// attachment (matched on upload and sent as the multipart filename, keeping the
/// server name unchanged — e.g. a draw.io diagram's macro reference stays
/// valid).</item>
/// <item><see cref="Version"/> — the server attachment version at last sync, the
/// cheap signal for "changed on the server" (no download needed).</item>
/// <item><see cref="Hash"/> — SHA-256 of the raw bytes at last sync, the signal
/// for "changed locally".</item>
/// <item><see cref="Size"/> — byte size, a fast pre-check.</item>
/// </list>
/// Any field may be null on a partially-known baseline (e.g. a local-only file
/// with no server counterpart yet).
/// </summary>
public sealed record AttachmentBaseline(
    string? ServerName,
    int? Version,
    string? Hash,
    long? Size);
