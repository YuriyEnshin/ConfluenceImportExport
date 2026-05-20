namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Resolves user-supplied paths against the MCP server's sandbox root.
/// Relative paths are anchored to <see cref="RootDir"/>; absolute paths
/// are normalised and checked to be inside the root.
/// Throws <see cref="OutOfSandboxException"/> if a path escapes the root.
/// </summary>
public interface IPathSandbox
{
    string RootDir { get; }
    string Resolve(string userPath);
}
