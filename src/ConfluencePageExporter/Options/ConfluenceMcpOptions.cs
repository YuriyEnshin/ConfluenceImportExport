namespace ConfluencePageExporter.Options;

/// <summary>
/// MCP server runtime options. Populated exclusively from CLI arguments
/// of the <c>mcp</c> command — never from configuration files or environment
/// variables — so that an agent connected to the server cannot widen its
/// own sandbox or unlock write tools via configuration.
/// </summary>
public sealed class ConfluenceMcpOptions
{
    public required string RootDir { get; init; }
    public bool ReadOnly { get; init; }
}
