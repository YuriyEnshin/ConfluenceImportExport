using System.Net;
using System.Net.Http;

namespace ConfluencePageExporter.Tests.Helpers;

public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responders = new();
    public List<HttpRequestMessage> Requests { get; } = [];

    public void EnqueueResponse(HttpStatusCode statusCode, string content = "", string mediaType = "application/json")
    {
        EnqueueResponder(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, System.Text.Encoding.UTF8, mediaType)
        });
    }

    public void EnqueueResponder(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        _responders.Enqueue(responder);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (_responders.Count == 0)
        {
            throw new InvalidOperationException($"No response configured for request: {request.Method} {request.RequestUri}");
        }

        var responder = _responders.Dequeue();
        return Task.FromResult(responder(request));
    }
}
