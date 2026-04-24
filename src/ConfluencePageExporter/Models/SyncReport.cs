using System.Collections.Concurrent;

namespace ConfluencePageExporter.Models;

public class SyncReport
{
    private readonly ConcurrentBag<SyncReportItem> _conflictPages = [];
    private readonly ConcurrentBag<SyncReportItem> _orphanPages = [];
    private readonly ConcurrentBag<SyncReportItem> _skippedPages = [];

    public IReadOnlyCollection<SyncReportItem> ConflictPages => GetSorted(_conflictPages);
    public IReadOnlyCollection<SyncReportItem> OrphanPages => GetSorted(_orphanPages);
    public IReadOnlyCollection<SyncReportItem> SkippedPages => GetSorted(_skippedPages);

    public bool HasIssues => !_conflictPages.IsEmpty || !_orphanPages.IsEmpty;

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

    public void PrintReport()
    {
        Console.WriteLine();
        Console.WriteLine("=== Отчёт о синхронизации ===");

        if (!_conflictPages.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"Конфликты (изменения с обеих сторон): {_conflictPages.Count}");
            foreach (var item in ConflictPages)
                Console.WriteLine($"  !! [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!_orphanPages.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"Требуют ручного удаления: {_orphanPages.Count}");
            foreach (var item in OrphanPages)
                Console.WriteLine($"  -- [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!_skippedPages.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine($"Пропущены (изменения на противоположной стороне): {_skippedPages.Count}");
            foreach (var item in SkippedPages)
                Console.WriteLine($"  ~~ [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!HasIssues && _skippedPages.IsEmpty)
        {
            Console.WriteLine();
            Console.WriteLine("Проблем не обнаружено.");
        }

        Console.WriteLine();
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
