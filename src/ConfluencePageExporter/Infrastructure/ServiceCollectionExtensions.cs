using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Shared composition root for the Confluence integration. Both entry points —
/// the CLI host (<c>Program.cs</c>) and the MCP host (<see cref="McpServerRunner"/>) —
/// call this so their wiring can't drift apart. Centralises the
/// <c>HttpClientFactory</c>-based <see cref="IConfluenceApiClient"/> registration
/// (typed client + resilience handler chain) and the content normalizer.
/// </summary>
public static class ServiceCollectionExtensions
{
    private const string ConfluenceClientName = "confluence";

    public static IServiceCollection AddConfluenceExporterCore(this IServiceCollection services)
    {
        services.AddSingleton<IContentNormalizer, XmlContentNormalizer>();

        // Delegating handlers are resolved from DI; the factory assigns their
        // InnerHandler when it builds the pipeline.
        services.AddTransient<RetryingHttpHandler>();
        services.AddTransient<HttpTimingHandler>();

        // Named client carrying the resilience pipeline (outermost → innermost):
        //   HttpClient → RetryingHttpHandler → HttpTimingHandler → SocketsHttpHandler
        // The SocketsHttpHandler connection-lifetime cap evicts stale sockets
        // after a network blip; the retry handler absorbs transient failures.
        services.AddHttpClient(ConfluenceClientName)
            .ConfigureHttpClient((sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<GlobalOptions>>().Value;
                var username = opts.Username
                    ?? throw new ArgumentException("Missing required parameter: --username (or Global:Username in config)");
                var token = opts.Token
                    ?? throw new ArgumentException("Missing required parameter: --token (or Global:Token in config)");
                // Basic auth works the same for on-prem and cloud.
                var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{token}"));
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            })
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(2),
                PooledConnectionIdleTimeout = TimeSpan.FromMinutes(1),
                ConnectTimeout = TimeSpan.FromSeconds(30),
            })
            .AddHttpMessageHandler<RetryingHttpHandler>()
            .AddHttpMessageHandler<HttpTimingHandler>();

        services.AddTransient<IConfluenceApiClient>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<GlobalOptions>>().Value;
            var baseUrl = opts.BaseUrl
                ?? throw new ArgumentException("Missing required parameter: --base-url (or Global:BaseUrl in config)");
            var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(ConfluenceClientName);
            var logger = sp.GetRequiredService<ILogger<HttpClientConfluenceApiClient>>();
            return new HttpClientConfluenceApiClient(baseUrl, http, logger);
        });

        return services;
    }
}
