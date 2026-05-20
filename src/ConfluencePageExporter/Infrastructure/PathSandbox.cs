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

        RootDir = Path.GetFullPath(rootDir);
        _rootDirWithSeparator = RootDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

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
