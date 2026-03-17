using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public static class LocalStorageHelper
{
    public static string SanitizeFileName(string title)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Join("_", title.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
    }

    public static PageMarkerInfo ParseMarkerFileName(string fileName)
    {
        var markerValue = fileName[3..];
        var underscoreIdx = markerValue.LastIndexOf('_');
        if (underscoreIdx > 0 && int.TryParse(markerValue[(underscoreIdx + 1)..], out var version))
            return new PageMarkerInfo(markerValue[..underscoreIdx], version);
        return new PageMarkerInfo(markerValue, null);
    }

    public static string? ReadPageIdFromMarker(string pageDir)
    {
        return ReadPageMarkerInfo(pageDir)?.PageId;
    }

    public static PageMarkerInfo? ReadPageMarkerInfo(string pageDir)
    {
        if (!Directory.Exists(pageDir)) return null;

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".id") && fileName.Length > 3)
                return ParseMarkerFileName(fileName);
        }
        return null;
    }

    public static async Task WritePageIdMarkerAsync(string pageDir, string pageId, int? version = null)
    {
        if (!Directory.Exists(pageDir))
            throw new DirectoryNotFoundException($"Page directory does not exist: {pageDir}");

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            File.Delete(file);
        }

        var markerName = version.HasValue ? $".id{pageId}_{version.Value}" : $".id{pageId}";
        var markerPath = Path.Combine(pageDir, markerName);
        await File.WriteAllTextAsync(markerPath, string.Empty);
    }

    public static async Task<string> ReadPageContent(string pageDir)
    {
        var indexPath = Path.Combine(pageDir, "index.html");
        if (!File.Exists(indexPath))
            throw new InvalidOperationException($"No index.html found in '{pageDir}'");
        return await File.ReadAllTextAsync(indexPath);
    }

    public static async Task<string?> ReadLocalPageContentOrNull(string pageDirectory)
    {
        var indexPath = Path.Combine(pageDirectory, "index.html");
        if (!File.Exists(indexPath))
            return null;

        return await File.ReadAllTextAsync(indexPath);
    }

    public static IEnumerable<string> GetAttachmentFiles(string pageDir)
    {
        return Directory.GetFiles(pageDir)
            .Where(f =>
            {
                var name = Path.GetFileName(f);
                return !name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith(".id");
            });
    }

    public static IEnumerable<string> GetPageSubdirectories(string pageDir)
    {
        return Directory.Exists(pageDir) ? Directory.GetDirectories(pageDir) : [];
    }

    /// <summary>
    /// Returns the page title from a directory path (folder name).
    /// Handles trailing directory separators that would cause Path.GetFileName to return empty.
    /// </summary>
    public static string GetPageTitleFromDirectory(string pageDir)
    {
        var normalized = Path.TrimEndingDirectorySeparator(pageDir);
        var title = Path.GetFileName(normalized);
        return string.IsNullOrEmpty(title) ? pageDir : title;
    }

    public static void ValidateSourceDirectory(string sourceDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDir}");

        if (!File.Exists(Path.Combine(sourceDir, "index.html")))
            throw new FileNotFoundException($"No index.html found in source directory: {sourceDir}");

        var title = GetPageTitleFromDirectory(sourceDir);
        if (string.IsNullOrWhiteSpace(title))
            throw new InvalidOperationException($"Source directory path yields an empty page title: {sourceDir}");
    }

    public static string NormalizeRelativePath(string path)
    {
        return path.Replace(Path.DirectorySeparatorChar, '/')
            .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    public static IEnumerable<string> EnumeratePageDirectories(string rootDir)
    {
        if (!Directory.Exists(rootDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories).Prepend(rootDir))
        {
            if (File.Exists(Path.Combine(dir, "index.html")))
                yield return dir;
        }
    }

    public static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    public static Dictionary<string, string> BuildPageDirectoryIndex(string rootDir, ILogger? logger = null)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(rootDir))
            return index;

        foreach (var markerFile in Directory.EnumerateFiles(rootDir, ".id*", SearchOption.AllDirectories))
        {
            var markerName = Path.GetFileName(markerFile);
            if (!markerName.StartsWith(".id", StringComparison.OrdinalIgnoreCase) || markerName.Length <= 3)
                continue;

            var pageId = ParseMarkerFileName(markerName).PageId;
            var pageDir = Path.GetDirectoryName(markerFile);
            if (string.IsNullOrEmpty(pageDir))
                continue;

            var normalizedPageDir = Path.GetFullPath(pageDir);
            if (!index.TryAdd(pageId, normalizedPageDir))
            {
                logger?.LogWarning(
                    "Found duplicate page marker for ID {PageId}. Keeping first path {KeptPath}, ignoring {IgnoredPath}",
                    pageId,
                    index[pageId],
                    normalizedPageDir);
            }
        }

        return index;
    }

    public static void UpdateDirectoryIndexPaths(
        Dictionary<string, string> pageDirectoryIndex,
        string oldRootDir,
        string newRootDir)
    {
        var oldRoot = Path.GetFullPath(oldRootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var newRoot = Path.GetFullPath(newRootDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var oldPrefix = oldRoot + Path.DirectorySeparatorChar;
        var oldAltPrefix = oldRoot + Path.AltDirectorySeparatorChar;

        foreach (var key in pageDirectoryIndex.Keys.ToList())
        {
            var currentPath = Path.GetFullPath(pageDirectoryIndex[key])
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (currentPath.Equals(oldRoot, StringComparison.OrdinalIgnoreCase))
            {
                pageDirectoryIndex[key] = newRoot;
                continue;
            }

            if (currentPath.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase)
                || currentPath.StartsWith(oldAltPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var suffix = currentPath[oldRoot.Length..];
                pageDirectoryIndex[key] = newRoot + suffix;
            }
        }
    }

    public static async Task<string?> ResolvePageIdAsync(
        IConfluenceApiClient apiClient,
        string spaceKey,
        string? pageId,
        string? pageTitle)
    {
        if (!string.IsNullOrEmpty(pageId))
            return pageId;

        if (!string.IsNullOrEmpty(pageTitle))
            return await apiClient.FindPageByTitleAsync(spaceKey, null, pageTitle);

        return null;
    }
}
