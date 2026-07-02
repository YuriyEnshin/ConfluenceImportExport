using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

/// <summary>
/// Tests for <see cref="DeploymentTypeResolver"/>: an explicit --auth-type
/// wins over the URL, unknown values fail loudly, and auto-detection
/// recognises *.atlassian.net hosts only.
/// </summary>
public class DeploymentTypeResolverTests
{
    // ── Explicit value ───────────────────────────────────────────────────

    [Theory]
    [InlineData("onprem", DeploymentType.OnPrem)]
    [InlineData("ONPREM", DeploymentType.OnPrem)]
    [InlineData("cloud", DeploymentType.Cloud)]
    [InlineData("Cloud", DeploymentType.Cloud)]
    [InlineData("  cloud  ", DeploymentType.Cloud)]
    public void Resolve_ExplicitValue_WinsOverBaseUrl(string authType, DeploymentType expected)
    {
        // Both a Cloud-looking and an on-prem-looking URL: the flag decides.
        DeploymentTypeResolver.Resolve(authType, "https://example.atlassian.net/wiki").ShouldBe(expected);
        DeploymentTypeResolver.Resolve(authType, "https://confluence.corp.local").ShouldBe(expected);
    }

    [Theory]
    [InlineData("server")]
    [InlineData("datacenter")]
    [InlineData("cl0ud")]
    public void Resolve_UnknownExplicitValue_Throws(string authType)
    {
        var ex = Should.Throw<ArgumentException>(
            () => DeploymentTypeResolver.Resolve(authType, "https://confluence.corp.local"));

        ex.Message.ShouldContain(authType);
        ex.Message.ShouldContain("onprem");
        ex.Message.ShouldContain("cloud");
    }

    // ── Auto-detection ───────────────────────────────────────────────────

    [Theory]
    [InlineData("https://mysite.atlassian.net", DeploymentType.Cloud)]
    [InlineData("https://mysite.atlassian.net/", DeploymentType.Cloud)]
    [InlineData("https://mysite.atlassian.net/wiki", DeploymentType.Cloud)]
    [InlineData("https://MYSITE.ATLASSIAN.NET/wiki/", DeploymentType.Cloud)]
    [InlineData("https://team.dev.atlassian.net", DeploymentType.Cloud)]
    [InlineData("https://atlassian.net", DeploymentType.OnPrem)]      // apex is not a site
    [InlineData("https://notatlassian.net", DeploymentType.OnPrem)]   // suffix must match on a dot
    [InlineData("https://evil.example.com/?q=.atlassian.net", DeploymentType.OnPrem)]
    [InlineData("https://confluence.corp.local/confluence", DeploymentType.OnPrem)]
    [InlineData(null, DeploymentType.OnPrem)]
    [InlineData("", DeploymentType.OnPrem)]
    [InlineData("   ", DeploymentType.OnPrem)]
    [InlineData("not a url", DeploymentType.OnPrem)]
    public void Autodetect_RecognisesCloudHostsOnly(string? baseUrl, DeploymentType expected) =>
        DeploymentTypeResolver.Autodetect(baseUrl).ShouldBe(expected);

    [Fact]
    public void Resolve_WithoutExplicitValue_FallsBackToAutodetect()
    {
        DeploymentTypeResolver.Resolve(null, "https://mysite.atlassian.net/wiki").ShouldBe(DeploymentType.Cloud);
        DeploymentTypeResolver.Resolve("", "https://confluence.corp.local").ShouldBe(DeploymentType.OnPrem);
        DeploymentTypeResolver.Resolve("   ", null).ShouldBe(DeploymentType.OnPrem);
    }

    // ── TryParse ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("onprem", true)]
    [InlineData("cloud", true)]
    [InlineData("wrong", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParse_AcceptsOnlyKnownValues(string? value, bool expected) =>
        DeploymentTypeResolver.TryParse(value, out _).ShouldBe(expected);
}
