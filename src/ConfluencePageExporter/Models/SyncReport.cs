namespace ConfluencePageExporter.Models;

public class SyncReport
{
    public List<SyncReportItem> ConflictPages { get; } = [];
    public List<SyncReportItem> OrphanPages { get; } = [];
    public List<SyncReportItem> SkippedPages { get; } = [];

    public bool HasIssues => ConflictPages.Count > 0 || OrphanPages.Count > 0;

    public void AddConflict(string pageId, string title, string reason)
    {
        ConflictPages.Add(new SyncReportItem(pageId, title, reason));
    }

    public void AddOrphan(string pageId, string title, string reason)
    {
        OrphanPages.Add(new SyncReportItem(pageId, title, reason));
    }

    public void AddSkipped(string pageId, string title, string reason)
    {
        SkippedPages.Add(new SyncReportItem(pageId, title, reason));
    }

    public void PrintReport()
    {
        Console.WriteLine();
        Console.WriteLine("=== Отчёт о синхронизации ===");

        if (ConflictPages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Конфликты (изменения с обеих сторон): {ConflictPages.Count}");
            foreach (var item in ConflictPages)
                Console.WriteLine($"  !! [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (OrphanPages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Требуют ручного удаления: {OrphanPages.Count}");
            foreach (var item in OrphanPages)
                Console.WriteLine($"  -- [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (SkippedPages.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Пропущены (изменения на противоположной стороне): {SkippedPages.Count}");
            foreach (var item in SkippedPages)
                Console.WriteLine($"  ~~ [{item.PageId}] {item.Title} — {item.Reason}");
        }

        if (!HasIssues && SkippedPages.Count == 0)
        {
            Console.WriteLine();
            Console.WriteLine("Проблем не обнаружено.");
        }

        Console.WriteLine();
    }
}

public sealed record SyncReportItem(string PageId, string Title, string Reason);
