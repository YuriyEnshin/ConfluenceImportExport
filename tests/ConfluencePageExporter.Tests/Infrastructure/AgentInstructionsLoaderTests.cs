using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

/// <summary>
/// Sanity-checks the build glue that embeds <c>docs/mcp/agent-instructions.md</c>
/// into the assembly. If the resource is missing or empty, the MCP server
/// would silently ship without any agent guidance — these tests fail loudly
/// so a misconfigured .csproj is caught at CI time, not at runtime.
/// </summary>
public class AgentInstructionsLoaderTests
{
    [Fact]
    public void TryLoad_ShouldReturnNonEmptyMarkdown()
    {
        var instructions = AgentInstructionsLoader.TryLoad();

        instructions.ShouldNotBeNullOrWhiteSpace(
            "the embedded agent-instructions.md must ship with the assembly");
        instructions!.Length.ShouldBeGreaterThan(500,
            "the cheat sheet should not be a stub");
    }

    [Fact]
    public void TryLoad_ShouldContainKeyWorkflowMarkers()
    {
        // Light contract test: the instructions must at least mention the
        // core concepts. If someone replaces the file with a placeholder,
        // this fires.
        var instructions = AgentInstructionsLoader.TryLoad();

        instructions.ShouldNotBeNull();
        instructions!.ShouldContain("confluence_get_page_content");
        instructions!.ShouldContain("confluence_ping");
        instructions!.ShouldContain("3-way merge");
        instructions!.ShouldContain("OUT_OF_SANDBOX");
    }
}
