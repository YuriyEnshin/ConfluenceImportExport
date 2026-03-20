using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class CliOverrideBuilderTests
{
    [Fact]
    public void Build_ShouldMapGlobalOptions_ForDownloadUpdate()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --base-url https://x.com --username u --token t --space-key S --dry-run");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.Should().ContainKey("Global:BaseUrl").WhoseValue.Should().Be("https://x.com");
        overrides.Should().ContainKey("Global:Username").WhoseValue.Should().Be("u");
        overrides.Should().ContainKey("Global:Token").WhoseValue.Should().Be("t");
        overrides.Should().ContainKey("Global:SpaceKey").WhoseValue.Should().Be("S");
        overrides.Should().ContainKey("Global:DryRun").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldMapDownloadUpdateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --page-id 123 --output-dir ./out --recursive");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.Should().ContainKey("Download:PageId").WhoseValue.Should().Be("123");
        overrides.Should().ContainKey("Download:OutputDir").WhoseValue.Should().Be("./out");
        overrides.Should().ContainKey("Download:Recursive").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldMapDownloadMergeOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download merge --page-id 123 --output-dir ./out --report");

        var overrides = CliOverrideBuilder.Build(pr, "download merge");

        overrides.Should().ContainKey("Download:PageId").WhoseValue.Should().Be("123");
        overrides.Should().ContainKey("Download:OutputDir").WhoseValue.Should().Be("./out");
        overrides.Should().ContainKey("Global:Report").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldMapUploadUpdateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload update --source-dir ./src --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "upload update");

        overrides.Should().ContainKey("Upload:Update:SourceDir").WhoseValue.Should().Be("./src");
        overrides.Should().ContainKey("Upload:Update:PageId").WhoseValue.Should().Be("1");
    }

    [Fact]
    public void Build_ShouldMapUploadMergeOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload merge --source-dir ./src --page-id 1 --report");

        var overrides = CliOverrideBuilder.Build(pr, "upload merge");

        overrides.Should().ContainKey("Upload:Merge:SourceDir").WhoseValue.Should().Be("./src");
        overrides.Should().ContainKey("Upload:Merge:PageId").WhoseValue.Should().Be("1");
        overrides.Should().ContainKey("Global:Report").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldMapUploadCreateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload create --source-dir ./src --parent-id 99 --recursive");

        var overrides = CliOverrideBuilder.Build(pr, "upload create");

        overrides.Should().ContainKey("Upload:Create:SourceDir").WhoseValue.Should().Be("./src");
        overrides.Should().ContainKey("Upload:Create:ParentId").WhoseValue.Should().Be("99");
        overrides.Should().ContainKey("Upload:Create:Recursive").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldMapCompareOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("compare --page-title MyPage --output-dir ./out --match-by-title");

        var overrides = CliOverrideBuilder.Build(pr, "compare");

        overrides.Should().ContainKey("Compare:PageTitle").WhoseValue.Should().Be("MyPage");
        overrides.Should().ContainKey("Compare:OutputDir").WhoseValue.Should().Be("./out");
        overrides.Should().ContainKey("Compare:MatchByTitle").WhoseValue.Should().Be("True");
    }

    [Fact]
    public void Build_ShouldNotIncludeOptionsNotExplicitlySet()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.Should().ContainKey("Download:PageId");
        overrides.Should().NotContainKey("Download:OutputDir");
        overrides.Should().NotContainKey("Download:Recursive");
        overrides.Should().NotContainKey("Global:BaseUrl");
    }

    [Fact]
    public void Build_ShouldNormalizePathValues()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse(new[] { "download", "update", "--output-dir", "\"/tmp/My Dir\"", "--page-id", "1" });

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.Should().ContainKey("Download:OutputDir").WhoseValue.Should().Be("/tmp/My Dir");
    }

    [Fact]
    public void Build_ShouldFindSharedOptionsPlacedBeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--base-url https://x.com download update --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.Should().ContainKey("Global:BaseUrl").WhoseValue.Should().Be("https://x.com");
        overrides.Should().ContainKey("Download:PageId").WhoseValue.Should().Be("1");
    }

    [Fact]
    public void Build_ConfigShow_ShouldMapOptionsToAllSections()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show --page-id 42 --recursive --source-dir ./src");

        var overrides = CliOverrideBuilder.Build(pr, "config show");

        overrides.Should().ContainKey("Download:PageId").WhoseValue.Should().Be("42");
        overrides.Should().ContainKey("Upload:Update:PageId").WhoseValue.Should().Be("42");
        overrides.Should().ContainKey("Compare:PageId").WhoseValue.Should().Be("42");

        overrides.Should().ContainKey("Download:Recursive").WhoseValue.Should().Be("True");
        overrides.Should().ContainKey("Upload:Update:Recursive").WhoseValue.Should().Be("True");
        overrides.Should().ContainKey("Upload:Create:Recursive").WhoseValue.Should().Be("True");
        overrides.Should().ContainKey("Compare:Recursive").WhoseValue.Should().Be("True");

        overrides.Should().ContainKey("Upload:Update:SourceDir").WhoseValue.Should().Be("./src");
        overrides.Should().ContainKey("Upload:Create:SourceDir").WhoseValue.Should().Be("./src");
    }

    [Fact]
    public void Build_ConfigShow_ShouldMapGlobalOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show --base-url https://x.com --dry-run");

        var overrides = CliOverrideBuilder.Build(pr, "config show");

        overrides.Should().ContainKey("Global:BaseUrl").WhoseValue.Should().Be("https://x.com");
        overrides.Should().ContainKey("Global:DryRun").WhoseValue.Should().Be("True");
    }
}
