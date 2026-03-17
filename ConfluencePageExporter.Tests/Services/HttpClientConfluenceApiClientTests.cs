using System.Net;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;
using Newtonsoft.Json;

namespace ConfluencePageExporter.Tests.Services;

public class HttpClientConfluenceApiClientTests
{
    [Fact]
    public async Task TryGetPageByIdAsync_ShouldReturnNull_ForNotFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.TryGetPageByIdAsync("123");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetChildrenPagesAsync_ShouldHandlePagination()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, BuildPageResultsJson(startId: 1, count: 100, hasNext: true));
        handler.EnqueueResponse(HttpStatusCode.OK, BuildPageResultsJson(startId: 101, count: 1, hasNext: false));
        var client = CreateClient(handler);

        var result = await client.GetChildrenPagesAsync("10");

        result.Should().HaveCount(101);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("start=0");
        handler.Requests[1].RequestUri!.ToString().Should().Contain("start=100");
    }

    [Fact]
    public async Task GetAttachmentsAsync_ShouldHandlePagination()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, BuildAttachmentResultsJson(startId: 1, count: 100, hasNext: true));
        handler.EnqueueResponse(HttpStatusCode.OK, BuildAttachmentResultsJson(startId: 101, count: 2, hasNext: false));
        var client = CreateClient(handler);

        var result = await client.GetAttachmentsAsync("20");

        result.Should().HaveCount(102);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("start=0");
        handler.Requests[1].RequestUri!.ToString().Should().Contain("start=100");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldReturnFirstPageId_WhenFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"results":[{"id":"55","title":"Target"}]}""");
        var client = CreateClient(handler);

        var result = await client.FindPageByTitleAsync("DOCS", null, "Target");

        result.Should().Be("55");
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/rest/api/content/search");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldReturnNull_OnHttpError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"message":"error"}""");
        var client = CreateClient(handler);

        var result = await client.FindPageByTitleAsync("DOCS", null, "Target");

        result.Should().BeNull();
    }

    [Fact]
    public async Task CreatePageAsync_ShouldReturnResultWithIdAndVersion_WhenRequestSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            var body = request.Content!.ReadAsStringAsync().Result;
            body.Should().Contain(@"""title"":""NewPage""");
            body.Should().Contain(@"""ancestors""");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"700","title":"NewPage","version":{"number":1}}""")
            };
        });
        var client = CreateClient(handler);

        var result = await client.CreatePageAsync("DOCS", "10", "NewPage", "<p>x</p>");

        result.Should().NotBeNull();
        result!.Id.Should().Be("700");
        result.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task CreatePageAsync_ShouldReturnNull_OnFailedRequest()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.BadRequest, """{"message":"bad"}""");
        var client = CreateClient(handler);

        var result = await client.CreatePageAsync("DOCS", null, "NewPage", "<p>x</p>");

        result.Should().BeNull();
    }

    [Fact]
    public async Task UpdatePageAsync_ShouldIncrementVersionAndReturnResult()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"id":"900","title":"Old","version":{"number":3}}""");
        handler.EnqueueResponder(request =>
        {
            request.Method.Should().Be(HttpMethod.Put);
            var body = request.Content!.ReadAsStringAsync().Result;
            body.Should().Contain(@"""number"":4");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"900","title":"New","version":{"number":4}}""")
            };
        });

        var client = CreateClient(handler);

        var result = await client.UpdatePageAsync("900", "New", "<p>new</p>", null);

        result.Should().NotBeNull();
        result!.Id.Should().Be("900");
        result.VersionNumber.Should().Be(4);
        handler.Requests.Should().HaveCount(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Get);
        handler.Requests[1].Method.Should().Be(HttpMethod.Put);
    }

    [Fact]
    public async Task DownloadAttachmentAsync_ShouldUseBaseUrl_ForRelativeDownloadUrl()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.RequestUri!.ToString().Should().Be("https://wiki.example.com/download/path");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        });
        var client = CreateClient(handler);

        var result = await client.DownloadAttachmentAsync("/download/path");

        result.Should().Equal([1, 2, 3]);
    }

    [Fact]
    public async Task DownloadAttachmentAsync_ShouldKeepAbsoluteDownloadUrl()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.RequestUri!.ToString().Should().Be("https://cdn.example.com/file.bin");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([9, 9])
            };
        });
        var client = CreateClient(handler);

        var result = await client.DownloadAttachmentAsync("https://cdn.example.com/file.bin");

        result.Should().Equal([9, 9]);
    }

    [Fact]
    public async Task GetPageVersionsAsync_ShouldReturnVersionList()
    {
        var handler = new StubHttpMessageHandler();
        var json = JsonConvert.SerializeObject(new
        {
            results = new[]
            {
                new { number = 3, when = "2026-03-15T10:00:00.000+0000", message = "", minorEdit = false },
                new { number = 2, when = "2026-03-14T09:00:00.000+0000", message = "update", minorEdit = false },
                new { number = 1, when = "2026-03-13T08:00:00.000+0000", message = "created", minorEdit = false }
            },
            start = 0,
            limit = 10,
            size = 3
        });
        handler.EnqueueResponse(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var result = await client.GetPageVersionsAsync("100", 10);

        result.Should().HaveCount(3);
        result[0].Number.Should().Be(3);
        result[2].Number.Should().Be(1);
        handler.Requests.Should().ContainSingle();
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/rest/experimental/content/100/version");
    }

    [Fact]
    public async Task GetPageVersionsAsync_ShouldReturnEmpty_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetPageVersionsAsync("999");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAtVersionAsync_ShouldReturnHistoricalPage()
    {
        var handler = new StubHttpMessageHandler();
        var json = JsonConvert.SerializeObject(new
        {
            id = "100",
            title = "OldTitle",
            ancestors = new[] { new { id = "50", title = "Parent" } },
            version = new { number = 2, when = "2026-03-14T09:00:00.000+0000" }
        });
        handler.EnqueueResponse(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var result = await client.GetPageAtVersionAsync("100", 2);

        result.Should().NotBeNull();
        result!.Title.Should().Be("OldTitle");
        result.Ancestors.Should().ContainSingle(a => a.Title == "Parent");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("status=historical");
        handler.Requests[0].RequestUri!.ToString().Should().Contain("version=2");
    }

    [Fact]
    public async Task GetPageAtVersionAsync_ShouldReturnNull_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetPageAtVersionAsync("999", 1);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldExpandVersion()
    {
        var handler = new StubHttpMessageHandler();
        var json = JsonConvert.SerializeObject(new
        {
            id = "100",
            title = "Test",
            body = new { storage = new { value = "<p>test</p>", representation = "storage" } },
            ancestors = Array.Empty<object>(),
            version = new { number = 5, when = "2026-03-15T10:00:00.000+0000" }
        });
        handler.EnqueueResponse(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var result = await client.GetPageByIdAsync("100");

        result.Version.Should().NotBeNull();
        result.Version!.Number.Should().Be(5);
        result.Version.When.Should().NotBeNull();
        handler.Requests[0].RequestUri!.ToString().Should().Contain("expand=body.storage,ancestors,version");
    }

    private static HttpClientConfluenceApiClient CreateClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new HttpClientConfluenceApiClient(
            "https://wiki.example.com",
            httpClient,
            LoggerTestHelper.CreateLogger<HttpClientConfluenceApiClient>());
    }

    private static string BuildPageResultsJson(int startId, int count, bool hasNext)
    {
        var results = Enumerable.Range(startId, count)
            .Select(i => new
            {
                id = i.ToString(),
                title = $"Page{i}",
                body = new
                {
                    storage = new
                    {
                        value = $"<p>{i}</p>",
                        representation = "storage"
                    }
                }
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["results"] = results,
            ["_links"] = hasNext
                ? new Dictionary<string, string> { ["next"] = "/next" }
                : new Dictionary<string, string>()
        };

        return JsonConvert.SerializeObject(payload);
    }

    private static string BuildAttachmentResultsJson(int startId, int count, bool hasNext)
    {
        var results = Enumerable.Range(startId, count)
            .Select(i => new
            {
                id = $"a{i}",
                title = $"file{i}.txt",
                _links = new
                {
                    download = $"/d/{i}"
                }
            })
            .ToList();

        var payload = new Dictionary<string, object?>
        {
            ["results"] = results,
            ["_links"] = hasNext
                ? new Dictionary<string, string> { ["next"] = "/next" }
                : new Dictionary<string, string>()
        };

        return JsonConvert.SerializeObject(payload);
    }
}
