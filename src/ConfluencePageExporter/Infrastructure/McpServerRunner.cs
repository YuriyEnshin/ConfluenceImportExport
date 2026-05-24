using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tools;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Bootstraps the application as an MCP stdio server. Distinct from the
/// regular CLI host because stdout is reserved for the JSON-RPC protocol —
/// all logging must go to stderr and command-handler text output is
/// captured per-invocation by <see cref="BufferingConsoleWriter"/>.
/// </summary>
public static class McpServerRunner
{
    public static async Task<int> RunAsync(
        BootstrapResult boot,
        IReadOnlyDictionary<string, string?> cliOverrides,
        ConfluenceMcpOptions mcpOptions,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Defensive: even though all handlers now use IConsoleWriter, redirect
            // Console.Out to stderr so any accidental future Console.WriteLine
            // call cannot corrupt the JSON-RPC stream on stdout.
            Console.SetOut(Console.Error);

            var builder = Host.CreateApplicationBuilder(Array.Empty<string>());
            builder.Configuration.Sources.Clear();

            if (boot.ConfigPath != null)
            {
                if (!File.Exists(boot.ConfigPath))
                {
                    await Console.Error.WriteLineAsync($"Error: Configuration file does not exist: {Path.GetFullPath(boot.ConfigPath)}");
                    return 1;
                }
                builder.Configuration.AddJsonFile(boot.ConfigPath, optional: false, reloadOnChange: false);
            }
            else
            {
                builder.Configuration.AddJsonFile("confluence-exporter.json", optional: true, reloadOnChange: false);
            }

            builder.Configuration
                .AddEnvironmentVariables(prefix: "CONFLUENCE_EXPORTER__")
                .AddInMemoryCollection(cliOverrides);

            // Logging → stderr. Stdout is reserved for MCP JSON-RPC frames.
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            builder.Logging.Services.Configure<ConsoleLoggerOptions>(o =>
                o.LogToStandardErrorThreshold = LogLevel.Trace);
            builder.Logging.SetMinimumLevel(boot.Verbose ? LogLevel.Debug : LogLevel.Information);

            builder.Services.AddOptions<GlobalOptions>()
                .Bind(builder.Configuration.GetSection("Global"));

            builder.Services.AddSingleton(mcpOptions);
            builder.Services.AddSingleton<IPathSandbox>(new PathSandbox(mcpOptions.RootDir));

            builder.Services.AddTransient<IConfluenceApiClient>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<GlobalOptions>>().Value;
                var logger = sp.GetRequiredService<ILogger<HttpClientConfluenceApiClient>>();
                return new HttpClientConfluenceApiClient(
                    opts.BaseUrl ?? throw new ArgumentException("Missing required configuration: Global:BaseUrl"),
                    opts.Username ?? throw new ArgumentException("Missing required configuration: Global:Username"),
                    opts.Token ?? throw new ArgumentException("Missing required configuration: Global:Token"),
                    logger,
                    opts.AuthType ?? "onprem");
            });

            // Ship the agent-facing instructions to the client via
            // InitializeResult.Instructions. Most MCP clients (Claude Code,
            // Claude Desktop, Cursor) feed this directly into the agent's
            // system prompt, so the agent sees workflow guidance — sandbox
            // model, conflict-resolution recipe, error-code semantics — the
            // moment the connection is established, without the operator
            // having to paste anything into custom rules.
            var agentInstructions = AgentInstructionsLoader.TryLoad();
            builder.Services
                .AddMcpServer(options =>
                {
                    if (!string.IsNullOrEmpty(agentInstructions))
                        options.ServerInstructions = agentInstructions;
                })
                .WithStdioServerTransport()
                .WithTools<ConfluenceMcpTools>();

            using var host = builder.Build();

            // Validate global configuration up-front so the agent sees the
            // problem on connect instead of on first tool call.
            var startupLogger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("McpServer");
            var globalOpts = host.Services.GetRequiredService<IOptions<GlobalOptions>>().Value;
            if (string.IsNullOrEmpty(globalOpts.BaseUrl) || string.IsNullOrEmpty(globalOpts.Username) || string.IsNullOrEmpty(globalOpts.Token))
            {
                await Console.Error.WriteLineAsync(
                    "Error: MCP server requires Global:BaseUrl, Global:Username and Global:Token to be set via configuration file or CONFLUENCE_EXPORTER__* environment variables.");
                return 1;
            }
            startupLogger.LogInformation(
                "Confluence MCP server starting. Root sandbox: {RootDir}. Read-only: {ReadOnly}. Base URL: {BaseUrl}",
                mcpOptions.RootDir, mcpOptions.ReadOnly, globalOpts.BaseUrl);

            await host.RunAsync(cancellationToken);
            return 0;
        }
        catch (Exception ex)
        {
            var message = ExceptionHandling.GetUserFriendlyErrorMessage(ex);
            await Console.Error.WriteLineAsync($"Error: {message}");
            return 1;
        }
    }
}
