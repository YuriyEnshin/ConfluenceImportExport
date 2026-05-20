namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Default <see cref="IConsoleWriter"/> implementation that writes to
/// <see cref="Console.Out"/>. Used by the CLI entry point.
/// </summary>
public sealed class StdConsoleWriter : IConsoleWriter
{
    public void WriteLine(string message = "") => Console.WriteLine(message);
    public void Write(string message) => Console.Write(message);
}
