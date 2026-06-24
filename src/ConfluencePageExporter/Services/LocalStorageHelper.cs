using System.Text;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter.Models;

namespace ConfluencePageExporter.Services;

/// <summary>
/// Pure helpers over the local page-tree layout: name sanitisation, content
/// and attachment IO, directory enumeration and the page-id → directory index.
/// The <c>.id</c> marker itself (format, read, write policy) is owned by
/// <see cref="PageMarker"/>.
/// </summary>
public static class LocalStorageHelper
{
    private static readonly HashSet<char> InvalidFileNameChars = new(Path.GetInvalidFileNameChars());

    public static string SanitizeFileName(string title)
    {
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
            sb.Append(InvalidFileNameChars.Contains(c) ? '_' : c);
        var sanitized = sb.ToString().TrimEnd('.', ' ');
        return string.IsNullOrEmpty(sanitized) ? "_" : sanitized;
    }

    /// <summary>
    /// Returns the effective page title for upload/comparison.
    /// If the original title is stored in the .id marker and the folder name matches
    /// the sanitized form of that title, returns the original title (preserving special characters).
    /// If the folder was renamed by the user, returns the folder name (user intent to rename).
    /// </summary>
    public static string GetPageTitle(string pageDir)
    {
        var storedTitle = PageMarker.Load(pageDir)?.Title;
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

    /// <summary>
    /// Builds the post-sync <see cref="AttachmentBaseline"/> for a local
    /// attachment file (the file must already reflect the synced state). The raw
    /// SHA-256 is reused from <paramref name="prior"/> when the server version and
    /// byte size are unchanged, so an unchanged attachment is not re-hashed on
    /// every sync. Returns null when the file does not exist (e.g. a failed
    /// download), so no baseline is stamped for it.
    /// </summary>
    public static async Task<AttachmentBaseline?> BuildAttachmentBaselineAsync(
        string filePath,
        string? serverName,
        int? serverVersion,
        IReadOnlyDictionary<string, AttachmentBaseline>? prior,
        CancellationToken ct = default)
    {
        var info = new FileInfo(filePath);
        if (!info.Exists)
            return null;

        var size = info.Length;
        var localName = Path.GetFileName(filePath);

        string? hash;
        if (prior != null
            && prior.TryGetValue(localName, out var p)
            && p.Version == serverVersion
            && p.Size == size
            && !string.IsNullOrEmpty(p.Hash))
        {
            hash = p.Hash;
        }
        else
        {
            hash = await AttachmentHasher.ComputeFileHashAsync(filePath, ct);
        }

        return new AttachmentBaseline(serverName, serverVersion, hash, size);
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

    /// <summary>Last segment of a '/'-separated relative path ("a/b/c" → "c").</summary>
    public static string GetLastPathSegment(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
    }

    /// <summary>Parent of a '/'-separated relative path ("a/b/c" → "a/b"; "" when there is none).</summary>
    public static string GetParentPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[..lastSlash] : "";
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

            var pageId = PageMarker.ParseFileName(markerName).PageId;
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

}
