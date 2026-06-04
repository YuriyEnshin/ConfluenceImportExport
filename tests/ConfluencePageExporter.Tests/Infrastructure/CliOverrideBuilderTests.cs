using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class CliOverrideBuilderTests
{
    [Fact]
    public void Build_ShouldMapGlobalOptions_ForDownloadUpdate()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --base-url https://x.com --username u --token t --space-key S --dry-run");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.ShouldContainKeyAndValue("Global:BaseUrl", "https://x.com");
        overrides.ShouldContainKeyAndValue("Global:Username", "u");
        overrides.ShouldContainKeyAndValue("Global:Token", "t");
        overrides.ShouldContainKeyAndValue("Global:SpaceKey", "S");
        overrides.ShouldContainKeyAndValue("Global:DryRun", "True");
    }

    [Fact]
    public void Build_ShouldMapDownloadUpdateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --page-id 123 --output-dir ./out --recursive");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.ShouldContainKeyAndValue("Download:Update:PageId", "123");
        overrides.ShouldContainKeyAndValue("Download:Update:OutputDir", "./out");
        overrides.ShouldContainKeyAndValue("Download:Update:Recursive", "True");
    }

    [Fact]
    public void Build_ShouldMapDownloadMergeOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download merge --page-id 123 --output-dir ./out --report");

        var overrides = CliOverrideBuilder.Build(pr, "download merge");

        overrides.ShouldContainKeyAndValue("Download:Merge:PageId", "123");
        overrides.ShouldContainKeyAndValue("Download:Merge:OutputDir", "./out");
        overrides.ShouldContainKeyAndValue("Global:Report", "True");
    }

    [Fact]
    public void Build_ShouldMapUploadUpdateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload update --source-dir ./src --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "upload update");

        overrides.ShouldContainKeyAndValue("Upload:Update:SourceDir", "./src");
        overrides.ShouldContainKeyAndValue("Upload:Update:PageId", "1");
    }

    [Fact]
    public void Build_ShouldMapUploadMergeOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload merge --source-dir ./src --page-id 1 --report");

        var overrides = CliOverrideBuilder.Build(pr, "upload merge");

        overrides.ShouldContainKeyAndValue("Upload:Merge:SourceDir", "./src");
        overrides.ShouldContainKeyAndValue("Upload:Merge:PageId", "1");
        overrides.ShouldContainKeyAndValue("Global:Report", "True");
    }

    [Fact]
    public void Build_ShouldMapUploadCreateOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload create --source-dir ./src --parent-id 99 --recursive");

        var overrides = CliOverrideBuilder.Build(pr, "upload create");

        overrides.ShouldContainKeyAndValue("Upload:Create:SourceDir", "./src");
        overrides.ShouldContainKeyAndValue("Upload:Create:ParentId", "99");
        overrides.ShouldContainKeyAndValue("Upload:Create:Recursive", "True");
    }

    [Fact]
    public void Build_ShouldMapCompareOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("compare --page-title MyPage --output-dir ./out --match-by-title");

        var overrides = CliOverrideBuilder.Build(pr, "compare");

        overrides.ShouldContainKeyAndValue("Compare:PageTitle", "MyPage");
        overrides.ShouldContainKeyAndValue("Compare:OutputDir", "./out");
        overrides.ShouldContainKeyAndValue("Compare:MatchByTitle", "True");
    }

    [Fact]
    public void Build_ShouldNotIncludeOptionsNotExplicitlySet()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download update --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.ShouldContainKey("Download:Update:PageId");
        overrides.ShouldNotContainKey("Download:Update:OutputDir");
        overrides.ShouldNotContainKey("Download:Update:Recursive");
        overrides.ShouldNotContainKey("Global:BaseUrl");
    }

    [Fact]
    public void Build_ShouldNormalizePathValues()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse(new[] { "download", "update", "--output-dir", "\"/tmp/My Dir\"", "--page-id", "1" });

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.ShouldContainKeyAndValue("Download:Update:OutputDir", "/tmp/My Dir");
    }

    [Fact]
    public void Build_ShouldFindSharedOptionsPlacedBeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--base-url https://x.com download update --page-id 1");

        var overrides = CliOverrideBuilder.Build(pr, "download update");

        overrides.ShouldContainKeyAndValue("Global:BaseUrl", "https://x.com");
        overrides.ShouldContainKeyAndValue("Download:Update:PageId", "1");
    }

    [Fact]
    public void Build_ConfigShow_ShouldMapOptionsToAllSections()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show --page-id 42 --recursive --source-dir ./src");

        var overrides = CliOverrideBuilder.Build(pr, "config show");

        overrides.ShouldContainKeyAndValue("Download:Update:PageId", "42");
        overrides.ShouldContainKeyAndValue("Download:Merge:PageId", "42");
        overrides.ShouldContainKeyAndValue("Upload:Update:PageId", "42");
        overrides.ShouldContainKeyAndValue("Upload:Merge:PageId", "42");
        overrides.ShouldContainKeyAndValue("Compare:PageId", "42");

        overrides.ShouldContainKeyAndValue("Download:Update:Recursive", "True");
        overrides.ShouldContainKeyAndValue("Download:Merge:Recursive", "True");
        overrides.ShouldContainKeyAndValue("Upload:Update:Recursive", "True");
        overrides.ShouldContainKeyAndValue("Upload:Create:Recursive", "True");
        overrides.ShouldContainKeyAndValue("Upload:Merge:Recursive", "True");
        overrides.ShouldContainKeyAndValue("Compare:Recursive", "True");

        overrides.ShouldContainKeyAndValue("Upload:Update:SourceDir", "./src");
        overrides.ShouldContainKeyAndValue("Upload:Create:SourceDir", "./src");
        overrides.ShouldContainKeyAndValue("Upload:Merge:SourceDir", "./src");
    }

    [Fact]
    public void Build_ConfigShow_ShouldMapGlobalOptions()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show --base-url https://x.com --dry-run");

        var overrides = CliOverrideBuilder.Build(pr, "config show");

        overrides.ShouldContainKeyAndValue("Global:BaseUrl", "https://x.com");
        overrides.ShouldContainKeyAndValue("Global:DryRun", "True");
    }
}
