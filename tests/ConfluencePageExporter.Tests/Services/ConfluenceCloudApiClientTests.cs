using System.Net;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using Shouldly;
using Newtonsoft.Json;

namespace ConfluencePageExporter.Tests.Services;

/// <summary>
/// Tests for <see cref="ConfluenceCloudApiClient"/> (Cloud v2 + retained v1
/// endpoints): URL shapes, v2→domain mapping, space-id→key and parent-title
/// caches, cursor pagination, downloadLink prefixing, and the read-only
/// contract (write methods throw).
/// </summary>
public class ConfluenceCloudApiClientTests
{
    private const string Site = "https://mysite.atlassian.net";
    private const string V2 = Site + "/wiki/api/v2";
    private const string V1 = Site + "/wiki/rest/api";

    // ── Base URL normalisation ───────────────────────────────────────────

    [Theory]
    [InlineData("https://mysite.atlassian.net")]
    [InlineData("https://mysite.atlassian.net/")]
    [InlineData("https://mysite.atlassian.net/wiki")]
    [InlineData("https://mysite.atlassian.net/wiki/")]
    public async Task Ctor_ShouldAcceptSiteAndWikiRoots_AndBuildSameUrls(string baseUrl)
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"displayName":"U","accountId":"a1"}""");
        var client = CreateClient(handler, baseUrl);

        await client.GetCurrentUserAsync(TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V1}/user/current");
    }

    // ── GetPageByIdAsync: mapping, space & parent-title resolution ──────

    [Fact]
    public async Task GetPageByIdAsync_ShouldMapV2Page_AndResolveSpaceAndParentTitle()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "Child", spaceId: "777", parentId: "50", version: 5));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("50", "Parent", spaceId: "777"));
        var client = CreateClient(handler);

        var page = await client.GetPageByIdAsync("100", TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/100?body-format=storage");
        handler.Requests[1].RequestUri!.ToString().ShouldBe($"{V2}/spaces/777");
        handler.Requests[2].RequestUri!.ToString().ShouldBe($"{V2}/pages/50");

        page.Id.ShouldBe("100");
        page.Title.ShouldBe("Child");
        page.Body.Storage.Value.ShouldBe("<p>x</p>");
        page.Version!.Number.ShouldBe(5);
        page.Version.When.ShouldNotBeNull();       // createdAt → When
        page.SpaceKey.ShouldBe("DOCS");            // numeric spaceId resolved to key
        page.ParentId.ShouldBe("50");              // parentId exposed via Ancestors
        page.Ancestors.ShouldHaveSingleItem().Title.ShouldBe("Parent");
        page.ChildTypes.ShouldBeNull();            // v2 has no childTypes
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldUseCaches_ForRepeatedSpaceAndParent()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "First", spaceId: "777", parentId: "50"));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("50", "Parent", spaceId: "777"));
        // Second page in the same space under the same parent: page fetch only.
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("200", "Second", spaceId: "777", parentId: "50"));
        var client = CreateClient(handler);

        await client.GetPageByIdAsync("100", TestContext.Current.CancellationToken);
        var second = await client.GetPageByIdAsync("200", TestContext.Current.CancellationToken);

        handler.Requests.Count.ShouldBe(4);
        second.SpaceKey.ShouldBe("DOCS");
        second.Ancestors.ShouldHaveSingleItem().Title.ShouldBe("Parent");
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldNotFailPage_WhenParentTitleUnresolvable()
    {
        // A deleted/forbidden parent degrades the ancestor title to "", it
        // must not fail the page fetch itself.
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "Orphanish", spaceId: "777", parentId: "50"));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        handler.EnqueueResponse(HttpStatusCode.Forbidden, """{"message":"no access"}""");
        var client = CreateClient(handler);

        var page = await client.GetPageByIdAsync("100", TestContext.Current.CancellationToken);

        page.ParentId.ShouldBe("50");
        page.Ancestors.ShouldHaveSingleItem().Title.ShouldBe("");
    }

    [Fact]
    public async Task TryGetPageByIdAsync_ShouldReturnNull_ForNotFound()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var result = await client.TryGetPageByIdAsync("123", TestContext.Current.CancellationToken);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task TryGetPageByIdAsync_ShouldThrowApiException_OnServerError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"message":"boom"}""");
        var client = CreateClient(handler);

        var act = async () => await client.TryGetPageByIdAsync("123", TestContext.Current.CancellationToken);

        var ex = await Should.ThrowAsync<ConfluenceApiException>(act);
        ex.StatusCode.ShouldBe(HttpStatusCode.InternalServerError);
    }

    [Fact]
    public async Task GetPageByIdAsync_ShouldThrowAuthFailure_OnForbidden()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.Forbidden, """{"message":"no access to page"}""");
        var client = CreateClient(handler);

        var act = async () => await client.GetPageByIdAsync("123", TestContext.Current.CancellationToken);

        var ex = await Should.ThrowAsync<ConfluenceApiException>(act);
        ex.IsAuthFailure.ShouldBeTrue();
        ex.Message.ShouldContain("403");
    }

    // ── Children: stubs + bulk bodies, cursor pagination ────────────────

    [Fact]
    public async Task GetChildrenPagesAsync_ShouldBulkFetchBodies_AndPreserveStubOrder()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            StubPageJson("1", "Alpha"), StubPageJson("2", "Beta")));
        // Bulk returns them in reverse order — the client must restore stub order.
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            PageObj("2", "Beta", spaceId: "777", parentId: "10", body: "<p>b</p>"),
            PageObj("1", "Alpha", spaceId: "777", parentId: "10", body: "<p>a</p>")));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        var client = CreateClient(handler);

        var children = await client.GetChildrenPagesAsync("10", TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/10/children?limit=250");
        handler.Requests[1].RequestUri!.ToString().ShouldBe($"{V2}/pages?id=1,2&body-format=storage&limit=250");

        children.Count.ShouldBe(2);
        children[0].Id.ShouldBe("1");
        children[0].Body.Storage.Value.ShouldBe("<p>a</p>");
        children[0].SpaceKey.ShouldBe("DOCS");
        children[0].ParentId.ShouldBe("10");
        children[1].Id.ShouldBe("2");
    }

    [Fact]
    public async Task GetChildrenPagesAsync_ShouldFollowCursor_ByReapplyingNextQueryToEndpoint()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            next: "/wiki/api/v2/pages/10/children?cursor=abc&limit=250",
            StubPageJson("1", "Alpha")));
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(StubPageJson("2", "Beta")));
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            PageObj("1", "Alpha", spaceId: "777", parentId: "10"),
            PageObj("2", "Beta", spaceId: "777", parentId: "10")));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        var client = CreateClient(handler);

        var children = await client.GetChildrenPagesAsync("10", TestContext.Current.CancellationToken);

        // The next-link's query is re-applied to our absolute endpoint URL —
        // its relative base is never trusted.
        handler.Requests[1].RequestUri!.ToString().ShouldBe($"{V2}/pages/10/children?cursor=abc&limit=250");
        children.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GetChildrenPagesAsync_ShouldReturnEmpty_WhenNoChildren()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson());
        var client = CreateClient(handler);

        var children = await client.GetChildrenPagesAsync("10", TestContext.Current.CancellationToken);

        children.ShouldBeEmpty();
        handler.Requests.ShouldHaveSingleItem(); // no bulk request for zero stubs
    }

    // ── Attachments ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetAttachmentsAsync_ShouldMapFlatV2Fields_IntoExtensions()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(new Dictionary<string, object?>
        {
            ["id"] = "att1",
            ["title"] = "diagram",
            ["mediaType"] = "application/vnd.jgraph.mxfile",
            ["fileSize"] = 12345L,
            ["comment"] = "src",
            ["downloadLink"] = "/download/attachments/100/diagram",
            ["version"] = new { number = 7, createdAt = "2026-06-01T10:00:00.000Z", minorEdit = true },
        }));
        var client = CreateClient(handler);

        var attachments = await client.GetAttachmentsAsync("100", TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/100/attachments?limit=250");
        var att = attachments.ShouldHaveSingleItem();
        att.Id.ShouldBe("att1");
        att.Title.ShouldBe("diagram");
        att.Extensions!.FileSize.ShouldBe(12345L);
        att.EffectiveMediaType.ShouldBe("application/vnd.jgraph.mxfile");
        att.Version!.Number.ShouldBe(7);
        att.Links.DownloadUrl.ShouldBe("/download/attachments/100/diagram");
    }

    [Fact]
    public async Task GetAttachmentsAsync_ShouldFollowCursor()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            next: "/wiki/api/v2/pages/100/attachments?cursor=zzz&limit=250",
            new Dictionary<string, object?> { ["id"] = "a1", ["title"] = "f1.txt" }));
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            new Dictionary<string, object?> { ["id"] = "a2", ["title"] = "f2.txt" }));
        var client = CreateClient(handler);

        var attachments = await client.GetAttachmentsAsync("100", TestContext.Current.CancellationToken);

        attachments.Count.ShouldBe(2);
        handler.Requests[1].RequestUri!.ToString().ShouldBe($"{V2}/pages/100/attachments?cursor=zzz&limit=250");
    }

    [Fact]
    public async Task GetAttachmentsAsync_ShouldReturnEmpty_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"message":"boom"}""");
        var client = CreateClient(handler);

        var attachments = await client.GetAttachmentsAsync("100", TestContext.Current.CancellationToken);

        attachments.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("/download/attachments/100/f.png", Site + "/wiki/download/attachments/100/f.png")]
    [InlineData("/wiki/download/attachments/100/f.png", Site + "/wiki/download/attachments/100/f.png")]
    [InlineData("https://cdn.example.com/f.png", "https://cdn.example.com/f.png")]
    public async Task DownloadAttachmentAsync_ShouldPrefixRelativeLinks_WithWikiContext(string link, string expected)
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponder(request =>
        {
            request.RequestUri!.ToString().ShouldBe(expected);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) };
        });
        var client = CreateClient(handler);

        var bytes = await client.DownloadAttachmentAsync(link, TestContext.Current.CancellationToken);

        bytes.ShouldBe([1, 2, 3]);
    }

    // ── Versions & historical pages ──────────────────────────────────────

    [Fact]
    public async Task GetPageVersionsAsync_ShouldMapCreatedAt_AndRequestNewestFirst()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, ListJson(
            new Dictionary<string, object?> { ["number"] = 3, ["createdAt"] = "2026-06-03T10:00:00.000Z", ["message"] = "", ["minorEdit"] = false },
            new Dictionary<string, object?> { ["number"] = 2, ["createdAt"] = "2026-06-02T10:00:00.000Z", ["message"] = "upd", ["minorEdit"] = true }));
        var client = CreateClient(handler);

        var versions = await client.GetPageVersionsAsync("100", 5, TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/100/versions?limit=5&sort=-modified-date");
        versions.Count.ShouldBe(2);
        versions[0].Number.ShouldBe(3);
        versions[0].When.ShouldNotBeNull();
        versions[1].Message.ShouldBe("upd");
        versions[1].MinorEdit.ShouldBeTrue();
    }

    [Fact]
    public async Task GetPageVersionsAsync_ShouldReturnEmpty_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var versions = await client.GetPageVersionsAsync("999", 10, TestContext.Current.CancellationToken);

        versions.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetPageAtVersionAsync_ShouldRequestVersion_WithoutBody()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "OldTitle", spaceId: "777", parentId: "50", version: 2));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("50", "Parent", spaceId: "777"));
        var client = CreateClient(handler);

        var page = await client.GetPageAtVersionAsync("100", 2, TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/100?version=2");
        page!.Title.ShouldBe("OldTitle");
        page.Ancestors.ShouldHaveSingleItem().Title.ShouldBe("Parent");
    }

    [Fact]
    public async Task GetPageAtVersionAsync_ShouldReturnNull_OnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.NotFound);
        var client = CreateClient(handler);

        var page = await client.GetPageAtVersionAsync("999", 1, TestContext.Current.CancellationToken);

        page.ShouldBeNull();
    }

    [Fact]
    public async Task GetPageContentAtVersionAsync_ShouldRequestStorageBody_AndThrowOnError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "OldTitle", spaceId: "777", version: 2, body: "<p>old</p>"));
        handler.EnqueueResponse(HttpStatusCode.OK, SpaceJson("777", "DOCS"));
        var client = CreateClient(handler);

        var page = await client.GetPageContentAtVersionAsync("100", 2, TestContext.Current.CancellationToken);

        handler.Requests[0].RequestUri!.ToString().ShouldBe($"{V2}/pages/100?version=2&body-format=storage");
        page.Body.Storage.Value.ShouldBe("<p>old</p>");

        var failing = new StubHttpMessageHandler();
        failing.EnqueueResponse(HttpStatusCode.NotFound, """{"message":"no such version"}""");
        var failingClient = CreateClient(failing);

        var act = async () => await failingClient.GetPageContentAtVersionAsync("100", 99, TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ConfluenceApiException>(act);
    }

    // ── v1 endpoints kept on Cloud ───────────────────────────────────────

    [Fact]
    public async Task FindPageByTitleAsync_ShouldUseV1CqlSearch_UnderWikiContext()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"results":[{"id":"55","title":"Target"}]}""");
        var client = CreateClient(handler);

        var id = await client.FindPageByTitleAsync("DOCS", "10", "Target", TestContext.Current.CancellationToken);

        id.ShouldBe("55");
        var url = handler.Requests[0].RequestUri!.ToString();
        url.ShouldStartWith($"{V1}/content/search?cql=");
        url.ShouldContain("parent%3D10");
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldReturnNull_WhenNoResults()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, """{"results":[]}""");
        var client = CreateClient(handler);

        var id = await client.FindPageByTitleAsync("DOCS", null, "Missing", TestContext.Current.CancellationToken);

        id.ShouldBeNull();
    }

    [Fact]
    public async Task FindPageByTitleAsync_ShouldThrowApiException_OnHttpError()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.InternalServerError, """{"message":"error"}""");
        var client = CreateClient(handler);

        var act = async () => await client.FindPageByTitleAsync("DOCS", null, "Target", TestContext.Current.CancellationToken);

        await Should.ThrowAsync<ConfluenceApiException>(act);
    }

    // ── Read-only contract ───────────────────────────────────────────────

    [Fact]
    public async Task WriteOperations_ShouldThrowNotSupported_InReadOnlyCloudPhase()
    {
        var handler = new StubHttpMessageHandler();
        var client = CreateClient(handler);
        var ct = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<NotSupportedException>(() => client.CreatePageAsync("DOCS", null, "T", "<p/>", ct));
        await Should.ThrowAsync<NotSupportedException>(() => client.UpdatePageAsync("1", "T", "<p/>", null, null, ct));
        await Should.ThrowAsync<NotSupportedException>(() => client.UploadAttachmentAsync("1", "f", "f", ct));
        await Should.ThrowAsync<NotSupportedException>(() => client.UpdateAttachmentDataAsync("1", "a", "f", "f", null, ct));
        await Should.ThrowAsync<NotSupportedException>(() => client.DeleteAttachmentAsync("1", "a", ct));

        handler.Requests.ShouldBeEmpty(); // rejected before any HTTP traffic
    }

    // ── Cancellation ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetPageByIdAsync_ShouldThrowOperationCanceled_WhenTokenAlreadyCancelled()
    {
        var handler = new StubHttpMessageHandler();
        handler.EnqueueResponse(HttpStatusCode.OK, PageJson("100", "T"));
        var client = CreateClient(handler);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.GetPageByIdAsync("100", cts.Token);

        await Should.ThrowAsync<OperationCanceledException>(act);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static ConfluenceCloudApiClient CreateClient(StubHttpMessageHandler handler, string baseUrl = Site)
    {
        var httpClient = new HttpClient(handler);
        return new ConfluenceCloudApiClient(
            baseUrl,
            httpClient,
            LoggerTestHelper.CreateLogger<ConfluenceCloudApiClient>());
    }

    private static Dictionary<string, object?> PageObj(
        string id, string title, string? spaceId = null, string? parentId = null, int version = 1, string body = "<p>x</p>") => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["spaceId"] = spaceId,
        ["parentId"] = parentId,
        ["version"] = new { number = version, createdAt = "2026-06-01T10:00:00.000Z", minorEdit = false },
        ["body"] = new { storage = new { value = body, representation = "storage" } },
        ["_links"] = new { webui = $"/spaces/x/pages/{id}" },
    };

    private static string PageJson(
        string id, string title, string? spaceId = null, string? parentId = null, int version = 1, string body = "<p>x</p>") =>
        JsonConvert.SerializeObject(PageObj(id, title, spaceId, parentId, version, body));

    /// <summary>Children-listing stub: id/title only, like the real v2 children endpoint.</summary>
    private static Dictionary<string, object?> StubPageJson(string id, string title) => new()
    {
        ["id"] = id,
        ["title"] = title,
        ["status"] = "current",
        ["spaceId"] = "777",
        ["childPosition"] = 0,
    };

    private static string SpaceJson(string id, string key) =>
        JsonConvert.SerializeObject(new { id, key, name = key });

    private static string ListJson(params object[] results) => ListJson(next: null, results);

    private static string ListJson(string? next, params object[] results) =>
        JsonConvert.SerializeObject(new Dictionary<string, object?>
        {
            ["results"] = results,
            ["_links"] = next is null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["next"] = next },
        });
}
