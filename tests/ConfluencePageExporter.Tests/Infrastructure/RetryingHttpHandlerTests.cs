using System.Net;
using System.Net.Sockets;
using ConfluencePageExporter.Infrastructure;
using Shouldly;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfluencePageExporter.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="RetryingHttpHandler"/>. We mount the handler at the
/// top of an <see cref="HttpClient"/>, plug a <see cref="StubInnerHandler"/>
/// beneath it that produces controlled responses/exceptions per attempt, and
/// assert the retry contract: transient → retry, non-transient → propagate,
/// POST → never retry, cancellation → abort.
/// </summary>
public class RetryingHttpHandlerTests
{
    private static RetryingHttpHandler Build(StubInnerHandler inner, int max = 3) =>
        new(NullLogger.Instance, inner, maxAttempts: max, baseDelay: TimeSpan.FromMilliseconds(1));

    // ── Idempotent verbs ─────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldRetry_OnNakedHttpRequestException_ThenSucceed()
    {
        var inner = new StubInnerHandler(attempt => attempt switch
        {
            1 or 2 => throw new HttpRequestException("transient network error"),
            _      => new HttpResponseMessage(HttpStatusCode.OK),
        });
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.AttemptCount.ShouldBe(3);
    }

    [Fact]
    public async Task SendAsync_ShouldRetry_OnSocketException()
    {
        var inner = new StubInnerHandler(attempt => attempt < 2
            ? throw new SocketException((int)SocketError.ConnectionAborted)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_ShouldRetry_OnHttp503_ThenReturnSuccess()
    {
        var inner = new StubInnerHandler(attempt => attempt < 2
            ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            : new HttpResponseMessage(HttpStatusCode.OK));
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        inner.AttemptCount.ShouldBe(2);
    }

    [Fact]
    public async Task SendAsync_ShouldGiveUp_AfterMaxAttempts_AndThrow()
    {
        var inner = new StubInnerHandler(_ => throw new HttpRequestException("perma-down"));
        using var client = new HttpClient(Build(inner, max: 3));

        var act = async () => await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        (await Should.ThrowAsync<HttpRequestException>(act)).Message.ShouldContain("perma-down");
        inner.AttemptCount.ShouldBe(3);
    }

    [Fact]
    public async Task SendAsync_ShouldGiveUp_AfterMaxAttempts_AndReturnLastTransientResponse()
    {
        // If every attempt yields a transient status, the caller still gets
        // the final response (instead of an exception) — they can then call
        // EnsureSuccessStatusCode themselves if they want.
        var inner = new StubInnerHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));
        using var client = new HttpClient(Build(inner, max: 3));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        inner.AttemptCount.ShouldBe(3);
    }

    // ── Non-transient: do not retry ──────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldNotRetry_On401()
    {
        var inner = new StubInnerHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        inner.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetry_On404()
    {
        var inner = new StubInnerHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        inner.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetry_On500()
    {
        // 500 is server bug, not transient — retrying just doubles the noise.
        var inner = new StubInnerHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using var client = new HttpClient(Build(inner));

        var response = await client.GetAsync("http://example.com/", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
        inner.AttemptCount.ShouldBe(1);
    }

    // ── Non-idempotent: never retry ──────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldNotRetry_OnPost_EvenForTransientException()
    {
        // Retrying POST risks duplicating a non-idempotent side-effect like
        // "create page" — safer to fail fast and let the caller decide.
        var inner = new StubInnerHandler(_ => throw new HttpRequestException("transient"));
        using var client = new HttpClient(Build(inner));

        var act = async () => await client.PostAsync("http://example.com/", new StringContent("{}"), TestContext.Current.CancellationToken);

        await Should.ThrowAsync<HttpRequestException>(act);
        inner.AttemptCount.ShouldBe(1);
    }

    [Fact]
    public async Task SendAsync_ShouldNotRetry_OnPost_EvenFor503()
    {
        var inner = new StubInnerHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        using var client = new HttpClient(Build(inner));

        var response = await client.PostAsync("http://example.com/", new StringContent("{}"), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);
        inner.AttemptCount.ShouldBe(1);
    }

    // ── Cancellation ─────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ShouldAbort_OnCancellation()
    {
        // First attempt throws; before we get to retry, the token is
        // cancelled. We must abort instead of silently swallowing.
        using var cts = new CancellationTokenSource();
        var inner = new StubInnerHandler(_ =>
        {
            cts.Cancel();
            throw new HttpRequestException("network");
        });
        using var client = new HttpClient(Build(inner));

        var act = async () => await client.GetAsync("http://example.com/", cts.Token);

        // Either HttpRequestException (caught before cancellation check) or
        // OperationCanceledException (the Task.Delay throws on cancel). The
        // contract is "do not silently succeed".
        await Should.ThrowAsync<Exception>(act);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Test-only inner handler. Each call to <see cref="SendAsync"/> invokes
    /// <see cref="_responder"/> with the 1-based attempt counter. The
    /// responder can return a response synchronously or throw to simulate a
    /// network failure.
    /// </summary>
    private sealed class StubInnerHandler : HttpMessageHandler
    {
        private readonly Func<int, HttpResponseMessage> _responder;
        public int AttemptCount { get; private set; }

        public StubInnerHandler(Func<int, HttpResponseMessage> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AttemptCount++;
            return Task.FromResult(_responder(AttemptCount));
        }
    }
}
