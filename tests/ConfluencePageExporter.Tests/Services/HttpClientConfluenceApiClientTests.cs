using System.Net;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
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

        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetChildrenPagesAsync_ShouldHandlePagination()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, BuildPageResultsJson(startId: 1, count: 100, hasNext: true));
        handler.EnqueueResponse(HttpStatusCode.OK, BuildPageResultsJson(startId: 101, count: 1, hasNext: false));
        var client = CreateClient(handler);

        var result = await client.GetChildrenPagesAsync("10");

        result.Count().ShouldBe(101);
        handler.Requests.Count().ShouldBe(2);
        handler.Requests[0].RequestUri!.ToString().ShouldContain("start=0");
        handler.Requests[1].RequestUri!.ToString().ShouldContain("start=100");
    }

    [Fact]
    public async Task GetAttachmentsAsync_ShouldHandlePagination()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, BuildAttachmentResultsJson(startId: 1, count: 100, hasNext: true));
        handler.EnqueueResponse(HttpStatusCode.OK, BuildAttachmentResultsJson(startId: 101, count: 2, hasNext: false));
        var client = CreateClient(handler);

        var result = await client.GetAttachmentsAsync("20");

        result.Count().ShouldBe(102);
        handler.Requests.Count().ShouldBe(2);
        handler.Requests[0].RequestUri!.ToString().ShouldContain("start=0");
        handler.Requests[1].RequestUri!.ToString().ShouldContain("start=100");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldReturnFirstPageId_WhenFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"results":[{"id":"55","title":"Target"}]}""");
        var client = CreateClient(handler);

        var result = await client.FindPageByTitleAsync("DOCS", null, "Target");

        result.ShouldBe("55");
        handler.Requests.ShouldHaveSingleItem();
        handler.Requests[0].RequestUri!.ToString().ShouldContain("/rest/api/content/search");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldReturnNull_OnHttpError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"message":"error"}""");
        var client = CreateClient(handler);

        var result = await client.FindPageByTitleAsync("DOCS", null, "Target");

        result.ShouldBeNull();
    }

    [Fact]
    public async Task CreatePageAsync_ShouldReturnResultWithIdAndVersion_WhenRequestSucceeds()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.Method.ShouldBe(HttpMethod.Post);
            var body = request.Content!.ReadAsStringAsync().Result;
            body.ShouldContain(@"""title"":""NewPage""");
            body.ShouldContain(@"""ancestors""");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"700","title":"NewPage","version":{"number":1}}""")
            };
        });
        var client = CreateClient(handler);

        var result = await client.CreatePageAsync("DOCS", "10", "NewPage", "<p>x</p>");

        result.ShouldNotBeNull();
        result!.Id.ShouldBe("700");
        result.VersionNumber.ShouldBe(1);
    }

    [Fact]
    public async Task CreatePageAsync_ShouldThrowApiException_OnFailedRequest()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.BadRequest, """{"message":"bad"}""");
        var client = CreateClient(handler);

        var act = async () => await client.CreatePageAsync("DOCS", null, "NewPage", "<p>x</p>");

        var ex = await Should.ThrowAsync<ConfluenceApiException>(act);
        ex.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        ex.ShouldNotBeOfType<ConfluenceConflictException>();
    }

    [Fact]
    public async Task UpdatePageAsync_ShouldThrowConflict_On409()
    {
        var handler = new StubHttpMessageHandler();
        // First GET (fetch current version) succeeds; the PUT returns 409.
        handler.EnqueueResponse(HttpStatusCode.OK, """{"id":"900","title":"Old","version":{"number":3}}""");
        handler.EnqueueResponse(HttpStatusCode.Conflict, """{"message":"version conflict"}""");
        var client = CreateClient(handler);

        var act = async () => await client.UpdatePageAsync("900", "New", "<p>new</p>", null);

        var ex = await Should.ThrowAsync<ConfluenceConflictException>(act);
        ex.StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdatePageAsync_ShouldThrowApiException_OnForbidden()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"id":"900","title":"Old","version":{"number":3}}""");
        handler.EnqueueResponse(HttpStatusCode.Forbidden, """{"message":"no permission"}""");
        var client = CreateClient(handler);

        var act = async () => await client.UpdatePageAsync("900", "New", "<p>new</p>", null);

        var ex = await Should.ThrowAsync<ConfluenceApiException>(act);
        ex.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        ex.IsAuthFailure.ShouldBeTrue();
    }

    [Fact]
    public async Task UpdatePageAsync_ShouldIncrementVersionAndReturnResult()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"id":"900","title":"Old","version":{"number":3}}""");
        handler.EnqueueResponder(request =>
        {
            request.Method.ShouldBe(HttpMethod.Put);
            var body = request.Content!.ReadAsStringAsync().Result;
            body.ShouldContain(@"""number"":4");

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"id":"900","title":"New","version":{"number":4}}""")
            };
        });

        var client = CreateClient(handler);

        var result = await client.UpdatePageAsync("900", "New", "<p>new</p>", null);

        result.ShouldNotBeNull();
        result!.Id.ShouldBe("900");
        result.VersionNumber.ShouldBe(4);
        handler.Requests.Count().ShouldBe(2);
        handler.Requests[0].Method.ShouldBe(HttpMethod.Get);
        handler.Requests[1].Method.ShouldBe(HttpMethod.Put);
    }

    [Fact]
    public async Task DownloadAttachmentAsync_ShouldUseBaseUrl_ForRelativeDownloadUrl()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.RequestUri!.ToString().ShouldBe("https://wiki.example.com/download/path");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3])
            };
        });
        var client = CreateClient(handler);

        var result = await client.DownloadAttachmentAsync("/download/path");

        result.ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task DownloadAttachmentAsync_ShouldKeepAbsoluteDownloadUrl()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.RequestUri!.ToString().ShouldBe("https://cdn.example.com/file.bin");
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([9, 9])
            };
        });
        var client = CreateClient(handler);

        var result = await client.DownloadAttachmentAsync("https://cdn.example.com/file.bin");

        result.ShouldBe([9, 9]);
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

        result.Count().ShouldBe(3);
        result[0].Number.ShouldBe(3);
        result[2].Number.ShouldBe(1);
        handler.Requests.ShouldHaveSingleItem();
        handler.Requests[0].RequestUri!.ToString().ShouldContain("/rest/experimental/content/100/version");
    }

    [Fact]
    public async Task GetPageVersionsAsync_ShouldReturnEmpty_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetPageVersionsAsync("999");

        result.ShouldBeEmpty();
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

        result.ShouldNotBeNull();
        result!.Title.ShouldBe("OldTitle");
        result.Ancestors.Where(a => a.Title == "Parent").ShouldHaveSingleItem();
        handler.Requests[0].RequestUri!.ToString().ShouldContain("status=historical");
        handler.Requests[0].RequestUri!.ToString().ShouldContain("version=2");
    }

    [Fact]
    public async Task GetPageAtVersionAsync_ShouldReturnNull_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.GetPageAtVersionAsync("999", 1);

        result.ShouldBeNull();
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

        result.Version.ShouldNotBeNull();
        result.Version!.Number.ShouldBe(5);
        result.Version.When.ShouldNotBeNull();
        handler.Requests[0].RequestUri!.ToString().ShouldContain("expand=body.storage,ancestors,version");
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldThrowOperationCanceled_WhenTokenAlreadyCancelled()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"id":"100","title":"T"}""");
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.GetPageByIdAsync("100", cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldExpandSpace_AndParseSpaceKey()
    {
        var handler = new StubHttpMessageHandler();
        var json = JsonConvert.SerializeObject(new
        {
            id = "100",
            title = "Test",
            body = new { storage = new { value = "<p>x</p>", representation = "storage" } },
            ancestors = Array.Empty<object>(),
            version = new { number = 5, when = "2026-03-15T10:00:00.000+0000" },
            space = new { key = "DOCS" }
        });
        handler.EnqueueResponse(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var result = await client.GetPageByIdAsync("100");

        result.SpaceKey.ShouldBe("DOCS");
        handler.Requests[0].RequestUri!.ToString().ShouldContain("childTypes.all,space");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldEscapeQuotesInCql()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"results":[]}""");
        var client = CreateClient(handler);

        await client.FindPageByTitleAsync("DOCS", null, "a \"quoted\" title");

        // The literal quotes in the title are backslash-escaped before the CQL
        // is built; the backslash survives URL-encoding as %5C (the quote
        // itself is normalised back to " by Uri). Nothing else in the query
        // contains a backslash, so %5C present ⇔ escaping happened — without it
        // the raw quote would have terminated the CQL string early.
        handler.Requests[0].RequestUri!.ToString().ShouldContain("%5C");
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
