using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

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

        instructions.Should().NotBeNullOrWhiteSpace(
            "the embedded agent-instructions.md must ship with the assembly");
        instructions!.Length.Should().BeGreaterThan(500,
            "the cheat sheet should not be a stub");
    }

    [Fact]
    public void TryLoad_ShouldContainKeyWorkflowMarkers()
    {
        // Light contract test: the instructions must at least mention the
        // core concepts. If someone replaces the file with a placeholder,
        // this fires.
        var instructions = AgentInstructionsLoader.TryLoad();

        instructions.Should().NotBeNull();
        instructions!.Should().Contain("confluence_get_page_content");
        instructions!.Should().Contain("confluence_ping");
        instructions!.Should().Contain("3-way merge");
        instructions!.Should().Contain("OUT_OF_SANDBOX");
    }
}
