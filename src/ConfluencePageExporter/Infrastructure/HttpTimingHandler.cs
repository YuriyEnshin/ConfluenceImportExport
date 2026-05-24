using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ConfluencePageExporter.Infrastructure;

public sealed class HttpTimingHandler : DelegatingHandler
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates the timing handler. When <paramref name="innerHandler"/> is
    /// <c>null</c> the legacy <see cref="HttpClientHandler"/> is used to
    /// preserve previous behaviour; callers that need connection-pool
    /// recycling should pass a configured <see cref="SocketsHttpHandler"/>.
    /// </summary>
    public HttpTimingHandler(ILogger logger, HttpMessageHandler? innerHandler = null)
        : base(innerHandler ?? new HttpClientHandler())
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var response = await base.SendAsync(request, cancellationToken);
        var elapsedMs = (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var bytes = response.Content.Headers.ContentLength;

        _logger.LogDebug(
            "[HTTP] {Method} {Uri} -> {Status} in {ElapsedMs}ms ({Bytes} bytes)",
            request.Method.Method,
            request.RequestUri,
            (int)response.StatusCode,
            elapsedMs,
            bytes?.ToString() ?? "?");

        return response;
    }
}
