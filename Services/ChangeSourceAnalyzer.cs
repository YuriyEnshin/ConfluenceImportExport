using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public class ChangeSourceAnalyzer
{
    private readonly IConfluenceApiClient _apiClient;
    private readonly ILogger<ChangeSourceAnalyzer> _logger;
    private readonly Dictionary<string, List<PageVersionSummary>> _versionListCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PageData?> _historicalPageCache = new(StringComparer.OrdinalIgnoreCase);

    public ChangeSourceAnalyzer(IConfluenceApiClient apiClient, ILogger<ChangeSourceAnalyzer> logger)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ChangeSourceInfo> AnalyzeRenameAsync(
        string pageId,
        string serverTitle,
        string localFolderName,
        DateTime? serverModifiedUtc,
        DateTime? localDirModifiedUtc,
        bool useVersionHistory)
    {
        if (useVersionHistory)
        {
            var versions = await GetVersionsCachedAsync(pageId);
            var sanitizedLocal = LocalStorageHelper.SanitizeFileName(localFolderName);

            foreach (var ver in versions.Where(v => v.Number > 0).OrderByDescending(v => v.Number).Skip(1))
            {
                var historicalPage = await GetHistoricalPageCachedAsync(pageId, ver.Number);
                if (historicalPage == null)
                    continue;

                var historicalSanitized = LocalStorageHelper.SanitizeFileName(historicalPage.Title);
                if (string.Equals(historicalSanitized, sanitizedLocal, StringComparison.OrdinalIgnoreCase))
                {
                    return new ChangeSourceInfo(
                        ChangeOrigin.Server,
                        ChangeConfidence.High,
                        $"Заголовок '{localFolderName}' найден в серверной версии {ver.Number}, " +
                        $"переименован в '{serverTitle}' в текущей версии");
                }
            }

            _logger.LogDebug(
                "Title '{LocalTitle}' not found in version history for page {PageId}; falling back to date comparison",
                localFolderName, pageId);
        }

        return CompareByDates(serverModifiedUtc, localDirModifiedUtc, "папка", "переименование");
    }

    public async Task<ChangeSourceInfo> AnalyzeMoveAsync(
        string pageId,
        string serverParentPath,
        string localParentPath,
        DateTime? serverModifiedUtc,
        DateTime? localDirModifiedUtc,
        bool useVersionHistory)
    {
        if (useVersionHistory)
        {
            var versions = await GetVersionsCachedAsync(pageId);

            foreach (var ver in versions.Where(v => v.Number > 0).OrderByDescending(v => v.Number).Skip(1))
            {
                var historicalPage = await GetHistoricalPageCachedAsync(pageId, ver.Number);
                if (historicalPage == null)
                    continue;

                var historicalParentTitle = historicalPage.Ancestors.Count > 0
                    ? LocalStorageHelper.SanitizeFileName(historicalPage.Ancestors[^1].Title)
                    : null;
                var localParentName = GetLastSegment(localParentPath);

                if (historicalParentTitle != null
                    && string.Equals(historicalParentTitle, localParentName, StringComparison.OrdinalIgnoreCase))
                {
                    var serverParentName = GetLastSegment(serverParentPath);
                    return new ChangeSourceInfo(
                        ChangeOrigin.Server,
                        ChangeConfidence.High,
                        $"Родитель '{localParentName}' найден в серверной версии {ver.Number}, " +
                        $"перемещён под '{serverParentName}' в текущей версии");
                }
            }

            _logger.LogDebug(
                "Parent path '{LocalParent}' not found in version history for page {PageId}; falling back to date comparison",
                localParentPath, pageId);
        }

        return CompareByDates(serverModifiedUtc, localDirModifiedUtc, "папка", "перемещение");
    }

    public ChangeSourceInfo AnalyzeContentChange(
        DateTime? serverModifiedUtc,
        DateTime? localFileModifiedUtc)
    {
        return CompareByDates(serverModifiedUtc, localFileModifiedUtc, "файл", "контент");
    }

    private static ChangeSourceInfo CompareByDates(
        DateTime? serverDate,
        DateTime? localDate,
        string localEntityName,
        string changeKind)
    {
        if (serverDate == null && localDate == null)
        {
            return new ChangeSourceInfo(
                ChangeOrigin.Unknown,
                ChangeConfidence.Low,
                $"Даты недоступны для определения источника ({changeKind})");
        }

        if (serverDate == null)
        {
            return new ChangeSourceInfo(
                ChangeOrigin.Local,
                ChangeConfidence.Low,
                $"Дата сервера недоступна; локальный {localEntityName} изменён {FormatDate(localDate)}");
        }

        if (localDate == null)
        {
            return new ChangeSourceInfo(
                ChangeOrigin.Server,
                ChangeConfidence.Low,
                $"Локальная дата недоступна; сервер изменён {FormatDate(serverDate)}");
        }

        if (localDate > serverDate)
        {
            return new ChangeSourceInfo(
                ChangeOrigin.Local,
                ChangeConfidence.Medium,
                $"Локальный {localEntityName} изменён ({FormatDate(localDate)}) позже сервера ({FormatDate(serverDate)})");
        }

        if (serverDate > localDate)
        {
            return new ChangeSourceInfo(
                ChangeOrigin.Server,
                ChangeConfidence.Medium,
                $"Сервер изменён ({FormatDate(serverDate)}) позже локального {localEntityName}а ({FormatDate(localDate)})");
        }

        return new ChangeSourceInfo(
            ChangeOrigin.Unknown,
            ChangeConfidence.Low,
            $"Даты совпадают ({FormatDate(serverDate)}); источник неопределён ({changeKind})");
    }

    private async Task<List<PageVersionSummary>> GetVersionsCachedAsync(string pageId)
    {
        if (_versionListCache.TryGetValue(pageId, out var cached))
            return cached;

        var versions = await _apiClient.GetPageVersionsAsync(pageId);
        _versionListCache[pageId] = versions;
        return versions;
    }

    private async Task<PageData?> GetHistoricalPageCachedAsync(string pageId, int versionNumber)
    {
        var cacheKey = $"{pageId}@{versionNumber}";
        if (_historicalPageCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var page = await _apiClient.GetPageAtVersionAsync(pageId, versionNumber);
        _historicalPageCache[cacheKey] = page;
        return page;
    }

    private static string GetLastSegment(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    private static string FormatDate(DateTime? date) =>
        date?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A";
}
