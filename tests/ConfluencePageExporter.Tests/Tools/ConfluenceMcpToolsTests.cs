using Microsoft.Extensions.Options;
using MEO = Microsoft.Extensions.Options.Options;
using ConfluencePageExporter.Infrastructure;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Options;
using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using ConfluencePageExporter.Tools;
using FluentAssertions;
using Moq;

namespace ConfluencePageExporter.Tests.Tools;

/// <summary>
/// Functional tests for <see cref="ConfluenceMcpTools"/>. We don't spin up an
/// actual MCP server here — the tool methods are invoked directly because
/// they are designed to be callable independently of the transport.
/// </summary>
public class ConfluenceMcpToolsTests
{
    private static IOptions<GlobalOptions> Globals(string? spaceKey = "SPACE")
        => MEO.Create(new GlobalOptions { SpaceKey = spaceKey });

    private static ConfluenceMcpOptions McpOpts(string rootDir, bool readOnly = false)
        => new() { RootDir = rootDir, ReadOnly = readOnly };

    // ── DownloadUpdate ───────────────────────────────────────────────────

    [Fact]
    public async Task DownloadUpdate_ShouldReturnSuccess_OnHappyPath()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);

        var page = ApiClientMockFactory.CreatePage("1", "Root", "<p>x</p>", hasPages: false, hasAttachments: false);
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetPageByIdAsync("1")).ReturnsAsync(page);
        api.Setup(x => x.GetAttachmentsAsync(It.IsAny<string>())).ReturnsAsync(new List<AttachmentData>());

        var result = await ConfluenceMcpTools.DownloadUpdate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            pageId: "1", pageTitle: null, outputDir: "out");

        AssertSuccess(result);
    }

    [Fact]
    public async Task DownloadUpdate_ShouldReturnOutOfSandbox_WhenPathEscapesRoot()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.DownloadUpdate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            pageId: "1", pageTitle: null, outputDir: "../../../evil");

        AssertError(result, "OUT_OF_SANDBOX");
    }

    [Fact]
    public async Task DownloadUpdate_ShouldReturnInvalidArgs_WhenBothPageIdAndTitleSet()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.DownloadUpdate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            pageId: "1", pageTitle: "Root", outputDir: "out");

        AssertError(result, "INVALID_ARGS");
    }

    [Fact]
    public async Task DownloadUpdate_ShouldReturnInvalidArgs_WhenNoSpaceKeyAnywhere()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.DownloadUpdate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(spaceKey: null),
            pageId: "1", pageTitle: null, outputDir: "out");

        AssertError(result, "INVALID_ARGS");
    }

    // ── UploadUpdate / read-only enforcement ─────────────────────────────

    [Fact]
    public async Task UploadUpdate_ShouldReturnReadOnlyViolation_WhenServerIsReadOnly()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.UploadUpdate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            McpOpts(temp.RootPath, readOnly: true),
            sourceDir: "src", pageId: "1");

        AssertError(result, "READ_ONLY_VIOLATION");
        // API should not have been touched at all.
        api.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UploadCreate_ShouldReturnReadOnlyViolation_WhenServerIsReadOnly()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.UploadCreate(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            McpOpts(temp.RootPath, readOnly: true),
            sourceDir: "src");

        AssertError(result, "READ_ONLY_VIOLATION");
    }

    [Fact]
    public async Task UploadMerge_ShouldReturnReadOnlyViolation_WhenServerIsReadOnly()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.UploadMerge(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            McpOpts(temp.RootPath, readOnly: true),
            sourceDir: "src");

        AssertError(result, "READ_ONLY_VIOLATION");
    }

    // ── Compare smoke test ───────────────────────────────────────────────

    [Fact]
    public async Task Compare_ShouldReturnOutOfSandbox_WhenOutputDirEscapesRoot()
    {
        using var temp = new TempDirectoryScope();
        var sandbox = new PathSandbox(temp.RootPath);
        var api = ApiClientMockFactory.CreateLoose();

        var result = await ConfluenceMcpTools.Compare(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), sandbox, Globals(),
            pageId: "1", pageTitle: null, outputDir: "../escape");

        AssertError(result, "OUT_OF_SANDBOX");
    }

    // ── Ping ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ping_ShouldReturnSuccess_WithAuthenticatedUser()
    {
        using var temp = new TempDirectoryScope();
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetCurrentUserAsync())
            .ReturnsAsync(new ConfluenceUser { Username = "alice", DisplayName = "Alice A.", UserKey = "u-42" });

        var globals = MEO.Create(new GlobalOptions { BaseUrl = "https://confluence.example.com", SpaceKey = "SPACE" });
        var result = await ConfluenceMcpTools.Ping(
            api.Object, LoggerTestHelper.CreateLoggerFactory(), globals, McpOpts(temp.RootPath));

        AssertSuccess(result);
        var report = result.GetType().GetProperty("report")!.GetValue(result)!;
        report.GetType().GetProperty("baseUrl")!.GetValue(report).Should().Be("https://confluence.example.com");
        report.GetType().GetProperty("spaceKey")!.GetValue(report).Should().Be("SPACE");
        var auth = report.GetType().GetProperty("authenticatedAs")!.GetValue(report)!;
        auth.GetType().GetProperty("username")!.GetValue(auth).Should().Be("alice");
        auth.GetType().GetProperty("displayName")!.GetValue(auth).Should().Be("Alice A.");
        var sandbox = report.GetType().GetProperty("sandbox")!.GetValue(report)!;
        sandbox.GetType().GetProperty("rootDir")!.GetValue(sandbox).Should().Be(temp.RootPath);
        sandbox.GetType().GetProperty("readOnly")!.GetValue(sandbox).Should().Be(false);
    }

    [Fact]
    public async Task Ping_ShouldReturnAuthFailed_OnHttp401()
    {
        using var temp = new TempDirectoryScope();
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetCurrentUserAsync())
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Unauthorized", inner: null, System.Net.HttpStatusCode.Unauthorized));

        var result = await ConfluenceMcpTools.Ping(
            api.Object, LoggerTestHelper.CreateLoggerFactory(),
            MEO.Create(new GlobalOptions { BaseUrl = "https://x", SpaceKey = "S" }),
            McpOpts(temp.RootPath));

        AssertError(result, "AUTH_FAILED");
    }

    [Fact]
    public async Task Ping_ShouldReturnNetworkError_OnNakedHttpRequestException()
    {
        using var temp = new TempDirectoryScope();
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetCurrentUserAsync())
            .ThrowsAsync(new System.Net.Http.HttpRequestException("DNS failure"));

        var result = await ConfluenceMcpTools.Ping(
            api.Object, LoggerTestHelper.CreateLoggerFactory(),
            MEO.Create(new GlobalOptions { BaseUrl = "https://x", SpaceKey = "S" }),
            McpOpts(temp.RootPath));

        AssertError(result, "NETWORK_ERROR");
    }

    [Fact]
    public async Task Ping_ShouldWorkInReadOnlyMode()
    {
        // confluence_ping is a diagnostic and must not be gated by --read-only.
        using var temp = new TempDirectoryScope();
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.GetCurrentUserAsync())
            .ReturnsAsync(new ConfluenceUser { Username = "ro-user" });

        var result = await ConfluenceMcpTools.Ping(
            api.Object, LoggerTestHelper.CreateLoggerFactory(),
            MEO.Create(new GlobalOptions { BaseUrl = "https://x", SpaceKey = "S" }),
            McpOpts(temp.RootPath, readOnly: true));

        AssertSuccess(result);
    }

    // ── GetPageContent ───────────────────────────────────────────────────

    private const string SampleStorage =
        "<p>Hello <strong>world</strong></p><ac:structured-macro ac:name=\"info\"><ac:rich-text-body><p>tip</p></ac:rich-text-body></ac:structured-macro>";

    [Fact]
    public async Task GetPageContent_ShouldReturnCurrentVersion_OnHappyPath()
    {
        var api = ApiClientMockFactory.CreateLoose();
        var page = ApiClientMockFactory.CreatePage("123", "My Page", SampleStorage, versionNumber: 7);
        api.Setup(x => x.GetPageByIdAsync("123")).ReturnsAsync(page);

        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "123", pageTitle: null);

        AssertSuccess(result);
        var report = result.GetType().GetProperty("report")!.GetValue(result)!;
        report.GetType().GetProperty("pageId")!.GetValue(report).Should().Be("123");
        report.GetType().GetProperty("title")!.GetValue(report).Should().Be("My Page");
        report.GetType().GetProperty("content")!.GetValue(report).Should().Be(SampleStorage);
        report.GetType().GetProperty("truncated")!.GetValue(report).Should().Be(false);
        report.GetType().GetProperty("normalized")!.GetValue(report).Should().Be(false);
        api.Verify(x => x.GetPageByIdAsync("123"), Times.Once);
        api.Verify(x => x.GetPageContentAtVersionAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task GetPageContent_ShouldCallVersionedEndpoint_WhenVersionGiven()
    {
        var api = ApiClientMockFactory.CreateLoose();
        var page = ApiClientMockFactory.CreatePage("123", "My Page", "<p>old</p>", versionNumber: 5);
        api.Setup(x => x.GetPageContentAtVersionAsync("123", 5)).ReturnsAsync(page);

        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "123", pageTitle: null, version: 5);

        AssertSuccess(result);
        api.Verify(x => x.GetPageContentAtVersionAsync("123", 5), Times.Once);
        api.Verify(x => x.GetPageByIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetPageContent_ShouldResolveByTitle_WhenPageIdMissing()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.FindPageByTitleAsync("SPACE", null, "Some Title")).ReturnsAsync("777");
        api.Setup(x => x.GetPageByIdAsync("777"))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("777", "Some Title", "<p>x</p>"));

        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: null, pageTitle: "Some Title");

        AssertSuccess(result);
        api.Verify(x => x.FindPageByTitleAsync("SPACE", null, "Some Title"), Times.Once);
        api.Verify(x => x.GetPageByIdAsync("777"), Times.Once);
    }

    [Fact]
    public async Task GetPageContent_ShouldReturnInvalidArgs_WhenBothPageIdAndTitleSet()
    {
        var api = ApiClientMockFactory.CreateLoose();
        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "1", pageTitle: "T");

        AssertError(result, "INVALID_ARGS");
    }

    [Fact]
    public async Task GetPageContent_ShouldReturnPageNotFound_WhenTitleUnresolvable()
    {
        var api = ApiClientMockFactory.CreateLoose();
        api.Setup(x => x.FindPageByTitleAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>()))
            .ReturnsAsync((string?)null);

        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: null, pageTitle: "Missing");

        AssertError(result, "PAGE_NOT_FOUND");
    }

    [Fact]
    public async Task GetPageContent_ShouldNormalize_WhenRequested()
    {
        // Different attribute ordering should collapse to the same canonical
        // form once normalized, but raw output should preserve server bytes.
        var api = ApiClientMockFactory.CreateLoose();
        var withQuirkySpacing = "<p>a</p>\n   <p>b</p>";
        api.Setup(x => x.GetPageByIdAsync("1"))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("1", "T", withQuirkySpacing));

        var raw = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "1", pageTitle: null, normalize: false);
        var normalized = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "1", pageTitle: null, normalize: true);

        var rawContent = (string)raw.GetType().GetProperty("report")!.GetValue(raw)!
            .GetType().GetProperty("content")!.GetValue(raw.GetType().GetProperty("report")!.GetValue(raw)!)!;
        var normContent = (string)normalized.GetType().GetProperty("report")!.GetValue(normalized)!
            .GetType().GetProperty("content")!.GetValue(normalized.GetType().GetProperty("report")!.GetValue(normalized)!)!;

        rawContent.Should().Be(withQuirkySpacing);
        normContent.Should().NotBe(withQuirkySpacing); // canonicalisation must change something
    }

    [Fact]
    public async Task GetPageContent_ShouldTruncate_WhenContentExceedsMaxBytes()
    {
        var api = ApiClientMockFactory.CreateLoose();
        var huge = new string('a', 5000);
        api.Setup(x => x.GetPageByIdAsync("1"))
            .ReturnsAsync(ApiClientMockFactory.CreatePage("1", "T", huge));

        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "1", pageTitle: null, maxBytes: 1024);

        AssertSuccess(result);
        var report = result.GetType().GetProperty("report")!.GetValue(result)!;
        report.GetType().GetProperty("truncated")!.GetValue(report).Should().Be(true);
        ((int)report.GetType().GetProperty("fullSize")!.GetValue(report)!).Should().Be(5000);
        ((int)report.GetType().GetProperty("contentLength")!.GetValue(report)!).Should().BeLessThanOrEqualTo(1024);
    }

    [Fact]
    public async Task GetPageContent_ShouldRejectTooSmallMaxBytes()
    {
        var api = ApiClientMockFactory.CreateLoose();
        var result = await ConfluenceMcpTools.GetPageContent(
            api.Object, new XmlContentNormalizer(), LoggerTestHelper.CreateLoggerFactory(), Globals(),
            pageId: "1", pageTitle: null, maxBytes: 16);

        AssertError(result, "INVALID_ARGS");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void AssertSuccess(object result)
    {
        var success = result.GetType().GetProperty("success")!.GetValue(result);
        success.Should().Be(true, "expected a success envelope, got {0}",
            result.GetType().GetProperty("errorCode")?.GetValue(result));
    }

    private static void AssertError(object result, string expectedCode)
    {
        var success = result.GetType().GetProperty("success")!.GetValue(result);
        success.Should().Be(false);
        var errorCode = result.GetType().GetProperty("errorCode")!.GetValue(result) as string;
        errorCode.Should().Be(expectedCode);
    }
}
