using System.Reflection;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Reads <c>docs/mcp/agent-instructions.md</c> from the assembly's embedded
/// resources. The same file is also published in the repo under
/// <c>docs/mcp/</c> for humans and for users who want to paste it into
/// custom agent rules; the embedded copy is what the MCP server ships to
/// connecting clients via <see cref="ModelContextProtocol.Server.McpServerOptions.ServerInstructions"/>.
/// </summary>
public static class AgentInstructionsLoader
{
    private const string ResourceName = "ConfluencePageExporter.agent-instructions.md";

    /// <summary>
    /// Returns the instructions string, or <c>null</c> if the embedded
    /// resource is missing. Returning null (vs. throwing) lets the MCP
    /// server start even if the resource gets stripped from a custom build —
    /// the server still works, agents just lose the cheat sheet.
    /// </summary>
    public static string? TryLoad()
    {
        var assembly = typeof(AgentInstructionsLoader).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
