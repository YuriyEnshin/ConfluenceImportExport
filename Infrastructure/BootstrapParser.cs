using System.CommandLine;
using System.CommandLine.Parsing;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Phase-1 lightweight extraction of infrastructure values from a <see cref="ParseResult"/>:
/// config file path, verbose flag, and the invoked command path.
/// Walks the full command chain so recursive options are found regardless of placement.
/// </summary>
public static class BootstrapParser
{
    public static BootstrapResult Parse(ParseResult parseResult)
    {
        string? configPath = null;
        bool verbose = false;

        foreach (var child in CollectOptionResults(parseResult))
        {
            if (child.Implicit) continue;

            switch (child.Option.Name)
            {
                case "--config":
                    configPath = PathNormalizer.Normalize(child.Tokens.FirstOrDefault()?.Value);
                    break;
                case "--verbose":
                    verbose = true;
                    break;
            }
        }

        var commandPath = BuildCommandPath(parseResult);

        return new BootstrapResult(configPath, verbose, commandPath);
    }

    private static IEnumerable<OptionResult> CollectOptionResults(ParseResult parseResult)
    {
        var cmd = parseResult.CommandResult;
        while (cmd != null)
        {
            foreach (var or in cmd.Children.OfType<OptionResult>())
                yield return or;
            cmd = cmd.Parent as CommandResult;
        }
    }

    private static string BuildCommandPath(ParseResult parseResult)
    {
        var parts = new List<string>();
        var cmd = parseResult.CommandResult;
        while (cmd != null)
        {
            var name = cmd.Command.Name;
            if (!string.IsNullOrEmpty(name) && cmd.Command is not RootCommand)
                parts.Insert(0, name);
            cmd = cmd.Parent as CommandResult;
        }
        return string.Join(" ", parts);
    }
}

public sealed record BootstrapResult(
    string? ConfigPath,
    bool Verbose,
    string CommandPath);
