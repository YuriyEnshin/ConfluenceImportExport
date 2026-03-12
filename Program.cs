using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter;
using ConfluencePageExporter.Commands;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

// ── Phase 1: bootstrap parse ──────────────────────────────────────────
var rootCommand = CommandDefinitions.Build();
var parseResult = rootCommand.Parse(args);

if (parseResult.Errors.Count > 0)
    return await parseResult.InvokeAsync();

var boot = BootstrapParser.Parse(parseResult);
var cliOverrides = CliOverrideBuilder.Build(parseResult, boot.CommandPath);

if (string.IsNullOrEmpty(boot.CommandPath))
{
    return await parseResult.InvokeAsync();
}

// ── Phase 2: build host ───────────────────────────────────────────────
try
{
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

    builder.Logging.ClearProviders();
    builder.Logging.AddSimpleConsole(o =>
    {
        o.SingleLine = true;
        o.TimestampFormat = "HH:mm:ss ";
    });
    builder.Logging.SetMinimumLevel(boot.Verbose ? LogLevel.Debug : LogLevel.Information);

    // ── Bind options ──────────────────────────────────────────────────
    builder.Services.AddOptions<GlobalOptions>()
        .Bind(builder.Configuration.GetSection("Global"));

    builder.Services.AddOptions<DownloadOptions>()
        .Bind(builder.Configuration.GetSection("Download"));

    builder.Services.AddOptions<UploadUpdateOptions>()
        .Bind(builder.Configuration.GetSection("Upload:Update"));

    builder.Services.AddOptions<UploadCreateOptions>()
        .Bind(builder.Configuration.GetSection("Upload:Create"));

    builder.Services.AddOptions<CompareOptions>()
        .Bind(builder.Configuration.GetSection("Compare"));

    // ── Register services ─────────────────────────────────────────────
    builder.Services.AddTransient<IConfluenceApiClient>(sp =>
    {
        var opts = sp.GetRequiredService<IOptions<GlobalOptions>>().Value;
        var logger = sp.GetRequiredService<ILogger<HttpClientConfluenceApiClient>>();
        return new HttpClientConfluenceApiClient(
            opts.BaseUrl ?? throw new ArgumentException("Missing required parameter: --base-url (or Global:BaseUrl in config)"),
            opts.Username ?? throw new ArgumentException("Missing required parameter: --username (or Global:Username in config)"),
            opts.Token ?? throw new ArgumentException("Missing required parameter: --token (or Global:Token in config)"),
            logger,
            opts.AuthType ?? "onprem");
    });

    builder.Services.AddTransient<DownloadCommandHandler>();
    builder.Services.AddTransient<UploadUpdateCommandHandler>();
    builder.Services.AddTransient<UploadCreateCommandHandler>();
    builder.Services.AddTransient<CompareCommandHandler>();
    builder.Services.AddSingleton<IReadOnlyDictionary<string, string?>>(cliOverrides);
    builder.Services.AddTransient<ConfigShowCommandHandler>();

    using var host = builder.Build();

    // ── Phase 3: dispatch ─────────────────────────────────────────────
    return await CommandDispatcher.DispatchAsync(boot.CommandPath, host.Services);
}
catch (Exception ex)
{
    var message = ExceptionHandling.GetUserFriendlyErrorMessage(ex);
    await Console.Error.WriteLineAsync($"Error: {message}");
    return 1;
}
