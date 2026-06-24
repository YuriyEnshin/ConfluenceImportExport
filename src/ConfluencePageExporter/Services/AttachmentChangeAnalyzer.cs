using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Per-attachment change-source verdict, the attachment analogue of
/// <see cref="ChangeSourceAnalyzer.AnalyzeContentChange"/> for page content.
/// </summary>
public enum AttachmentChangeOrigin
{
    /// <summary>Neither side moved from the baseline.</summary>
    Unchanged,
    /// <summary>Only the local file changed since the last sync.</summary>
    Local,
    /// <summary>Only the server attachment changed (version bumped).</summary>
    Server,
    /// <summary>Both sides changed.</summary>
    Conflict,
    /// <summary>No usable baseline (legacy/first sync) — source cannot be told.</summary>
    Unknown,
}

/// <summary>
/// Decides which side changed an attachment, fully "cheap": the server side from
/// the attachment's version number (already returned by the attachments listing),
/// the local side from the raw-bytes hash vs the baseline stored in the marker —
/// no attachment download required. Mirrors the content-change source detection
/// so merge can protect both sides instead of letting the command direction win.
/// </summary>
public static class AttachmentChangeAnalyzer
{
    /// <param name="baseline">The last-synced baseline from the marker, or null.</param>
    /// <param name="serverVersion">The current server attachment version, or null.</param>
    /// <param name="localChanged">
    /// Whether the local file differs from the baseline (true/false), or null when
    /// it cannot be determined (no baseline hash / file missing).
    /// </param>
    public static AttachmentChangeOrigin Analyze(AttachmentBaseline? baseline, int? serverVersion, bool? localChanged)
    {
        // Without a complete baseline (version + hash) or a determinable local
        // verdict / server version, the source is unknown — callers fall back to
        // the direction-follows-command behaviour and stamp a baseline this sync.
        if (baseline?.Version is not int baselineVersion
            || string.IsNullOrEmpty(baseline.Hash)
            || serverVersion is not int currentServerVersion
            || localChanged is not bool localDidChange)
        {
            return AttachmentChangeOrigin.Unknown;
        }

        var serverChanged = currentServerVersion != baselineVersion;

        return (localDidChange, serverChanged) switch
        {
            (false, false) => AttachmentChangeOrigin.Unchanged,
            (true, false) => AttachmentChangeOrigin.Local,
            (false, true) => AttachmentChangeOrigin.Server,
            (true, true) => AttachmentChangeOrigin.Conflict,
        };
    }
}
