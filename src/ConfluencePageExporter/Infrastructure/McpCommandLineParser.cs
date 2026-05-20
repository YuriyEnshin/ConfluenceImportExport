using System.CommandLine;
using System.CommandLine.Parsing;
using ConfluencePageExporter.Options;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Extracts MCP-specific options (--root-dir, --read-only) directly from the
/// parsed command line. These are deliberately NOT routed through
/// <see cref="Microsoft.Extensions.Configuration.IConfiguration"/> so that an
/// agent connected to the server cannot widen its sandbox or unlock write
/// tools via configuration files / environment variables.
/// </summary>
public static class McpCommandLineParser
{
    public static ConfluenceMcpOptions? Parse(ParseResult parseResult)
    {
        string? rootDirRaw = null;
        bool readOnly = false;

        var cmd = parseResult.CommandResult;
        while (cmd != null)
        {
            foreach (var or in cmd.Children.OfType<OptionResult>())
            {
                if (or.Implicit) continue;
                switch (or.Option.Name)
                {
                    case "--root-dir":
                        rootDirRaw = or.Tokens.FirstOrDefault()?.Value;
                        break;
                    case "--read-only":
                        readOnly = true;
                        break;
                }
            }
            cmd = cmd.Parent as CommandResult;
        }

        if (string.IsNullOrWhiteSpace(rootDirRaw))
        {
            Console.Error.WriteLine("Error: --root-dir is required for the mcp command.");
            return null;
        }

        var rootDir = PathNormalizer.Normalize(rootDirRaw)!;
        rootDir = Path.GetFullPath(rootDir);

        if (!Directory.Exists(rootDir))
        {
            Console.Error.WriteLine($"Error: --root-dir does not exist: {rootDir}");
            return null;
        }

        return new ConfluenceMcpOptions
        {
            RootDir = rootDir,
            ReadOnly = readOnly,
        };
    }
}
