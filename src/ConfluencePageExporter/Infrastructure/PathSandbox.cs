using System.Runtime.InteropServices;

namespace ConfluencePageExporter.Infrastructure;

public sealed class PathSandbox : IPathSandbox
{
    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

    private readonly string _rootDirWithSeparator;

    public string RootDir { get; }

    public PathSandbox(string rootDir)
    {
        if (string.IsNullOrWhiteSpace(rootDir))
            throw new ArgumentException("Root directory must be a non-empty path.", nameof(rootDir));

        // Path.GetFullPath preserves a trailing separator in its input (so
        // "C:\foo\" round-trips as "C:\foo\"), but Path.GetFullPath on a
        // path derived from the same root via "." drops it. Comparing the
        // two with Equals/StartsWith then mis-classifies the *root itself*
        // as outside the sandbox. Trim the trailing separator from the
        // canonical RootDir so both forms compare equal.
        RootDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootDir));

        // _rootDirWithSeparator must end in a separator for the
        // StartsWith-based descendant check. For drive roots like "C:\"
        // (or POSIX "/"), Path.TrimEndingDirectorySeparator leaves the
        // separator in place — so re-appending one would produce "C:\\"
        // and break the check. Append only when the trim actually
        // happened (or never existed in the first place).
        _rootDirWithSeparator = RootDir.Length > 0 && IsDirectorySeparator(RootDir[^1])
            ? RootDir
            : RootDir + Path.DirectorySeparatorChar;
    }

    private static bool IsDirectorySeparator(char c) =>
        c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

    public string Resolve(string userPath)
    {
        if (string.IsNullOrWhiteSpace(userPath))
            throw new ArgumentException("Path must be a non-empty string.", nameof(userPath));

        var combined = Path.IsPathRooted(userPath)
            ? userPath
            : Path.Combine(RootDir, userPath);

        var fullPath = Path.GetFullPath(combined);

        var isInside =
            fullPath.Equals(RootDir, PathComparison) ||
            fullPath.StartsWith(_rootDirWithSeparator, PathComparison);

        if (!isInside)
        {
            throw new OutOfSandboxException(
                $"Path '{userPath}' resolves to '{fullPath}', which is outside the sandbox root '{RootDir}'.");
        }

        return fullPath;
    }
}

public sealed class OutOfSandboxException : Exception
{
    public OutOfSandboxException(string message) : base(message) { }
}
