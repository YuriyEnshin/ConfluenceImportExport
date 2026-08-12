using System.Collections.Concurrent;
using ConfluencePageExporter.Infrastructure;

namespace ConfluencePageExporter.Models;

public class SyncReport
{
    private readonly ConcurrentBag<SyncReportItem> _conflictPages = [];
    private readonly ConcurrentBag<SyncReportItem> _orphanPages = [];
    private readonly ConcurrentBag<SyncReportItem> _skippedPages = [];
    private readonly ConcurrentBag<SyncReportAttachmentItem> _failedAttachments = [];

    public IReadOnlyCollection<SyncReportItem> ConflictPages => GetSorted(_conflictPages);
    public IReadOnlyCollection<SyncReportItem> OrphanPages => GetSorted(_orphanPages);
    public IReadOnlyCollection<SyncReportItem> SkippedPages => GetSorted(_skippedPages);

    /// <summary>
    /// Attachments that could not be synchronised at all. Unlike a skip or a
    /// conflict this is not a decision but a failure: the local mirror is
    /// knowingly out of sync with the server, so it counts as an issue.
    /// </summary>
    public IReadOnlyCollection<SyncReportAttachmentItem> FailedAttachments => _failedAttachments
        .OrderBy(x => x.PageTitle, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(x => x.PageId, StringComparer.Ordinal)
        .ToArray();

    public bool HasIssues => !_conflictPages.IsEmpty || !_orphanPages.IsEmpty || !_failedAttachments.IsEmpty;

    public void AddConflict(string pageId, string title, string reason)
    {
        _conflictPages.Add(new SyncReportItem(pageId, title, reason));
    }

    public void AddOrphan(string pageId, string title, string reason)
    {
        _orphanPages.Add(new SyncReportItem(pageId, title, reason));
    }

    public void AddSkipped(string pageId, string title, string reason)
    {
        _skippedPages.Add(new SyncReportItem(pageId, title, reason));
    }

    /// <summary>
    /// Records an attachment that failed to synchronise. <paramref name="fileName"/>
    /// is <see cref="AttachmentListingFileName"/> when the failure was the server
    /// listing itself (then the page's whole attachment set is unaccounted for).
    /// </summary>
    public void AddFailedAttachment(string pageId, string pageTitle, string fileName, string reason)
    {
        _failedAttachments.Add(new SyncReportAttachmentItem(pageId, pageTitle, fileName, reason));
    }

    /// <summary>
    /// Placeholder file name used when the failure is the attachment listing of a
    /// whole page rather than one file.
    /// </summary>
    public const string AttachmentListingFileName = "(список вложений)";

    public void PrintReport(IConsoleWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("=== Отчёт о синхронизации ===");

        if (!_conflictPages.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine($"Конфликты (изменения с обеих сторон): {_conflictPages.Count}");
            foreach (var item in ConflictPages)
                writer.WriteLine($"  !! [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!_orphanPages.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine($"Требуют ручного удаления: {_orphanPages.Count}");
            foreach (var item in OrphanPages)
                writer.WriteLine($"  -- [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!_skippedPages.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine($"Пропущены (изменения на противоположной стороне): {_skippedPages.Count}");
            foreach (var item in SkippedPages)
                writer.WriteLine($"  ~~ [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!_failedAttachments.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine($"Не синхронизированы вложения (зеркало неполное): {_failedAttachments.Count}");
            foreach (var item in FailedAttachments)
                writer.WriteLine($"  XX [{item.PageId}] {item.PageTitle} → {item.FileName} — {item.Reason}");
        }

        if (!HasIssues && _skippedPages.IsEmpty)
        {
            writer.WriteLine();
            writer.WriteLine("Проблем не обнаружено.");
        }

        writer.WriteLine();
    }

    /// <summary>
    /// One-line warning about failures that leave the mirror incomplete, for the
    /// default (no <c>--report</c>) output: a silent gap must not hide behind an
    /// "operation completed" line. <see cref="PrintReport"/> already lists the
    /// details, so callers print one or the other.
    /// </summary>
    public void PrintFailureWarning(IConsoleWriter writer)
    {
        if (_failedAttachments.IsEmpty)
            return;

        writer.WriteLine(
            $"ВНИМАНИЕ: не удалось синхронизировать вложений: {_failedAttachments.Count}. "
            + "Зеркало неполное — подробности выводятся с --report.");
    }

    private static IReadOnlyCollection<SyncReportItem> GetSorted(ConcurrentBag<SyncReportItem> bag)
    {
        return bag
            .OrderBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.PageId, StringComparer.Ordinal)
            .ToArray();
    }
}

public sealed record SyncReportItem(string PageId, string Title, string Reason);

/// <summary>
/// One attachment-level failure: the page it belongs to, the file name (or
/// <see cref="SyncReport.AttachmentListingFileName"/> for a failed listing) and
/// the reason, so CLI and MCP callers can act without digging through stderr.
/// </summary>
public sealed record SyncReportAttachmentItem(string PageId, string PageTitle, string FileName, string Reason);
