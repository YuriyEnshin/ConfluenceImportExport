using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// <see cref="DelegatingHandler"/> that retries transient HTTP failures with
/// exponential backoff, honouring the server's <c>Retry-After</c> header when
/// present (Confluence Cloud sends it with 429 rate-limit and some 503
/// responses; capped at <see cref="MaxRetryAfter"/> so a long server-suggested
/// pause can't hang a tool call). Idempotent verbs (GET / HEAD / PUT / DELETE /
/// OPTIONS) retry on any transient failure. POST retries <b>only</b> on 429:
/// a rate limiter rejects the request before it is processed, so a side effect
/// like "create page" or "upload attachment" cannot be duplicated — while on
/// 5xx or a network error the server may have already applied the change, so
/// POST is passed through untouched.
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
    /// <summary>
    /// Upper bound for a server-suggested <c>Retry-After</c> wait. Anything
    /// longer degenerates into an apparent hang for an interactive CLI/MCP
    /// call; better to burn the remaining attempts and surface the 429.
    /// </summary>
    internal static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);

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

    /// <summary>
    /// DI / HttpClientFactory constructor. The factory builds the handler
    /// pipeline and assigns <see cref="DelegatingHandler.InnerHandler"/>, so no
    /// inner handler is supplied here. Uses the default retry budget.
    /// </summary>
    public RetryingHttpHandler(ILogger<RetryingHttpHandler> logger)
        : base()
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxAttempts = 3;
        _baseDelay = TimeSpan.FromMilliseconds(250);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var idempotent = IsIdempotent(request.Method);

        Exception? lastException = null;
        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

                // 429 is safe to retry for every verb: the rate limiter
                // rejected the request before any side effect happened.
                var retryable = IsTransientStatus(response.StatusCode)
                    && (idempotent || response.StatusCode == HttpStatusCode.TooManyRequests);
                if (!retryable || attempt == _maxAttempts)
                {
                    return response;
                }

                var delay = RetryAfterDelay(response) ?? NextDelay(attempt);
                _logger.LogWarning(
                    "HTTP {Method} {Uri} returned transient status {Status}; retrying in {DelayMs}ms ({Attempt}/{Max}).",
                    request.Method.Method, request.RequestUri, (int)response.StatusCode,
                    delay.TotalMilliseconds, attempt, _maxAttempts);
                response.Dispose();

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (
                idempotent
                && IsTransient(ex)
                && attempt < _maxAttempts
                && !cancellationToken.IsCancellationRequested)
            {
                lastException = ex;
                var delay = NextDelay(attempt);
                _logger.LogWarning(
                    ex,
                    "HTTP {Method} {Uri} threw transient error; retrying in {DelayMs}ms ({Attempt}/{Max}).",
                    request.Method.Method, request.RequestUri,
                    delay.TotalMilliseconds, attempt, _maxAttempts);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }

        // Unreachable in practice: the loop always either returns or rethrows
        // on the final attempt. Defensive throw for completeness.
        throw lastException ?? new InvalidOperationException("Retry loop exited without a response.");
    }

    /// <summary>
    /// Extracts the server-suggested wait from <c>Retry-After</c> (both the
    /// delta-seconds and HTTP-date forms), clamped to
    /// [0, <see cref="MaxRetryAfter"/>]. Returns <c>null</c> when the header
    /// is absent, letting the caller fall back to exponential backoff.
    /// </summary>
    internal static TimeSpan? RetryAfterDelay(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter is null)
            return null;

        TimeSpan? delay = retryAfter.Delta
            ?? (retryAfter.Date is { } date ? date - DateTimeOffset.UtcNow : null);
        if (delay is null)
            return null;

        if (delay < TimeSpan.Zero)
            return TimeSpan.Zero;
        return delay > MaxRetryAfter ? MaxRetryAfter : delay;
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
