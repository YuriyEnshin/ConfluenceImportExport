using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

public static class LocalStorageHelper
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

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
    }

    public static string SanitizeFileName(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
            sb.Append(InvalidFileNameChars.Contains(c) ? '_' : c);
        var sanitized = sb.ToString().TrimEnd('.', ' ');
        return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
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

    public static DateTime? GetMarkerFileTimeUtc(string pageDir)
    {
        if (!Directory.Exists(pageDir)) return null;

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".id") && fileName.Length > 3)
                return File.GetLastWriteTimeUtc(file);
        }
        return null;
    }

    public static async Task WritePageIdMarkerAsync(
        string pageDir, string pageId, int? version = null, string? originalTitle = null,
        string? spaceKey = null, CancellationToken ct = default)
    {
        if (!Directory.Exists(pageDir))
            throw new DirectoryNotFoundException($"Page directory does not exist: {pageDir}");

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            File.Delete(file);
        }

        var markerName = version.HasValue ? $".id{pageId}_{version.Value}" : $".id{pageId}";
        var markerPath = Path.Combine(pageDir, markerName);
        await File.WriteAllTextAsync(markerPath, SerializeMarkerBody(originalTitle, spaceKey), ct);
    }

    /// <summary>
    /// Builds the marker file body. When a space key is known the body is a
    /// compact JSON object (<c>{"title":…,"space":…}</c>); otherwise the legacy
    /// plain-text title is written verbatim so older tooling keeps reading it
    /// unchanged. This keeps upgrade churn at zero until a sync captures space.
    /// </summary>
    private static string SerializeMarkerBody(string? title, string? spaceKey)
    {
        if (string.IsNullOrEmpty(spaceKey))
            return title ?? string.Empty;

        return JsonSerializer.Serialize(
            new MarkerBody
            {
                Title = string.IsNullOrEmpty(title) ? null : title,
                Space = spaceKey,
            },
            MarkerJsonOptions);
    }

    /// <summary>
    /// Parses a marker body written by <see cref="SerializeMarkerBody"/> or by
    /// any earlier version. New format: a JSON object with optional
    /// <c>title</c>/<c>space</c>. Legacy format: the raw text is the title and
    /// the space is unknown. A body that starts with '{' but does not parse to
    /// our shape is treated as a legacy title (defensive — a real Confluence
    /// title is extremely unlikely to be a JSON object with these keys).
    /// </summary>
    private static PageMarkerContent ParseMarkerBody(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0)
            return new PageMarkerContent(null, null);

        if (trimmed[0] == '{')
        {
            try
            {
                var body = JsonSerializer.Deserialize<MarkerBody>(trimmed, MarkerJsonOptions);
                if (body != null && (body.Title != null || body.Space != null))
                    return new PageMarkerContent(
                        string.IsNullOrWhiteSpace(body.Title) ? null : body.Title,
                        string.IsNullOrWhiteSpace(body.Space) ? null : body.Space);
            }
            catch (JsonException)
            {
                // Not our JSON — fall through and treat it as a legacy plain title.
            }
        }

        return new PageMarkerContent(trimmed, null);
    }

    /// <summary>
    /// Reads and parses the marker body for a page directory: the original
    /// title and the space key (either may be null). Returns an empty result
    /// when the directory or marker is missing. Reads the file once — prefer
    /// this over calling <see cref="ReadOriginalTitle"/> and
    /// <see cref="ReadSpaceKey"/> separately.
    /// </summary>
    public static PageMarkerContent ReadMarkerContent(string pageDir)
    {
        if (!Directory.Exists(pageDir)) return new PageMarkerContent(null, null);

        foreach (var file in Directory.GetFiles(pageDir, ".id*"))
        {
            var fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".id") && fileName.Length > 3)
                return ParseMarkerBody(File.ReadAllText(file));
        }
        return new PageMarkerContent(null, null);
    }

    public static string? ReadOriginalTitle(string pageDir) => ReadMarkerContent(pageDir).Title;

    /// <summary>
    /// Space key recorded in the marker body, or null for legacy markers that
    /// predate space capture. This is a cached hint — the server (via the page
    /// id) remains the authority for an existing page's real space.
    /// </summary>
    public static string? ReadSpaceKey(string pageDir) => ReadMarkerContent(pageDir).SpaceKey;

    /// <summary>
    /// Returns the effective page title for upload/comparison.
    /// If the original title is stored in the .id marker and the folder name matches
    /// the sanitized form of that title, returns the original title (preserving special characters).
    /// If the folder was renamed by the user, returns the folder name (user intent to rename).
    /// </summary>
    public static string GetPageTitle(string pageDir)
    {
        var storedTitle = ReadOriginalTitle(pageDir);
        var folderName = GetPageTitleFromDirectory(pageDir);

        if (storedTitle == null)
            return folderName;

        var expectedFolderName = SanitizeFileName(storedTitle);
        if (string.Equals(expectedFolderName, folderName, StringComparison.OrdinalIgnoreCase))
            return storedTitle;

        return folderName;
    }

    public static async Task<string> ReadPageContent(string pageDir, CancellationToken ct = default)
    {
        var indexPath = Path.Combine(pageDir, "index.html");
        if (!File.Exists(indexPath))
            throw new InvalidOperationException($"No index.html found in '{pageDir}'");
        return await File.ReadAllTextAsync(indexPath, ct);
    }

    public static async Task<string?> ReadLocalPageContentOrNull(string pageDirectory, CancellationToken ct = default)
    {
        var indexPath = Path.Combine(pageDirectory, "index.html");
        if (!File.Exists(indexPath))
            return null;

        return await File.ReadAllTextAsync(indexPath, ct);
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

    /// <summary>
    /// A "page directory" is a folder that directly holds an <c>index.html</c> —
    /// i.e. it represents a single Confluence page. A folder that only contains
    /// page sub-folders (a multi-tree container) is not a page directory.
    /// </summary>
    public static bool IsPageDirectory(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, "index.html"));

    /// <summary>
    /// Enumerates the immediate child folders of a container that are themselves
    /// page directories — i.e. the root of each page tree under it. Used by the
    /// multi-tree upload mode to process several trees (possibly from different
    /// spaces) in one call. Not recursive: a tree root's own descendants are
    /// handled by the per-tree recursive walk.
    /// </summary>
    public static IEnumerable<string> EnumerateTreeRoots(string containerDir)
    {
        if (!Directory.Exists(containerDir))
            yield break;

        foreach (var dir in Directory.EnumerateDirectories(containerDir))
        {
            if (File.Exists(Path.Combine(dir, "index.html")))
                yield return dir;
        }
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

        // Sort deterministically: Directory.EnumerateFiles order is filesystem-defined
        // (alphabetic on NTFS, inode-order on ext4/tmpfs), which makes the "first marker wins"
        // dedup pick non-reproducible across OSes/filesystems.
        var markerFiles = Directory.EnumerateFiles(rootDir, ".id*", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

        foreach (var markerFile in markerFiles)
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
        string? pageTitle,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(pageId))
            return pageId;

        if (!string.IsNullOrEmpty(pageTitle))
            return await apiClient.FindPageByTitleAsync(spaceKey, null, pageTitle, ct);

        return null;
    }
}
