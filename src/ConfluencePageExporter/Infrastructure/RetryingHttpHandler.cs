using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// <see cref="DelegatingHandler"/> that retries transient HTTP failures with
/// exponential backoff. Idempotent verbs only (GET / HEAD / PUT / DELETE /
/// OPTIONS); POST is passed through untouched so we never accidentally
/// duplicate a non-idempotent side effect like "create page" or "upload
/// attachment".
/// </summary>
/// <remarks>
/// Designed for the long-running MCP server: combined with the connection-
/// lifetime cap added in PR1 (<c>SocketsHttpHandler.PooledConnectionLifetime</c>),
/// this means a single network blip (VPN reconnect, NAT timeout, server-side
/// reset) is absorbed transparently — the agent's tool call appears to "just
/// work" after a brief stutter, instead of returning a NETWORK_ERROR that
/// requires manual ping/retry from the agent's side.
/// </remarks>
public sealed class RetryingHttpHandler : DelegatingHandler
{
    private readonly ILogger _logger;
    private readonly int _maxAttempts;
    private readonly TimeSpan _baseDelay;

    public RetryingHttpHandler(
        ILogger logger,
        HttpMessageHandler innerHandler,
        int maxAttempts = 3,
        TimeSpan? baseDelay = null)
        : base(innerHandler)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxAttempts = maxAttempts;
        _baseDelay = baseDelay ?? TimeSpan.FromMilliseconds(250);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // POST is non-idempotent. We don't know whether the server saw the
        // request before the failure, so retrying could create a duplicate
        // page or attachment. Pass through unchanged.
        if (!IsIdempotent(request.Method))
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        Exception? lastException = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                if (!IsTransientStatus(response.StatusCode) || attempt == _maxAttempts)
                {
                    return response;
                }
                _logger.LogWarning(
                    "HTTP {Method} {Uri} returned transient status {Status}; retrying in {DelayMs}ms ({Attempt}/{Max}).",
                    request.Method.Method, request.RequestUri, (int)response.StatusCode,
                    NextDelay(attempt).TotalMilliseconds, attempt, _maxAttempts);
                response.Dispose();
            }
            catch (Exception ex) when (
                IsTransient(ex)
                && attempt < _maxAttempts
                && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "HTTP {Method} {Uri} threw transient error; retrying in {DelayMs}ms ({Attempt}/{Max}).",
                    request.Method.Method, request.RequestUri,
                    NextDelay(attempt).TotalMilliseconds, attempt, _maxAttempts);
            }

            await Task.Delay(NextDelay(attempt), cancellationToken).ConfigureAwait(false);
        }

        // Unreachable in practice: the loop always either returns or rethrows
        // on the final attempt. Defensive throw for completeness.
        throw lastException ?? new InvalidOperationException("Retry loop exited without a response.");
    }

    private TimeSpan NextDelay(int attempt) =>
        TimeSpan.FromMilliseconds(_baseDelay.TotalMilliseconds * Math.Pow(2, attempt - 1));

    private static bool IsIdempotent(HttpMethod m) =>
        m == HttpMethod.Get
        || m == HttpMethod.Head
        || m == HttpMethod.Put
        || m == HttpMethod.Delete
        || m == HttpMethod.Options;

    private static bool IsTransient(Exception ex) => ex switch
    {
        HttpRequestException hre => hre.StatusCode is null || IsTransientStatus(hre.StatusCode.Value),
        IOException => true,
        SocketException => true,
        _ => false,
    };

    private static bool IsTransientStatus(HttpStatusCode status) =>
        status == HttpStatusCode.RequestTimeout       // 408
        || status == HttpStatusCode.TooManyRequests   // 429
        || status == HttpStatusCode.BadGateway        // 502
        || status == HttpStatusCode.ServiceUnavailable// 503
        || status == HttpStatusCode.GatewayTimeout;   // 504
}
