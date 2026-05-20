namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Abstraction over stdout writes used by command handlers and reports.
/// Keeps the CLI behaviour unchanged while allowing alternative facades
/// (MCP, tests) to redirect or suppress textual output without touching
/// business logic.
/// </summary>
public interface IConsoleWriter
{
    void WriteLine(string message = "");
    void Write(string message);
}
