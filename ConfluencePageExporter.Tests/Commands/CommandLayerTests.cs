using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Commands;

public class CommandLayerTests
{
    [Fact]
    public void RootCommand_ShouldContainAllTopLevelCommands()
    {
        var root = CommandDefinitions.Build();

        root.Subcommands.Select(c => c.Name)
            .Should().Contain(["download", "upload", "compare", "config"]);
    }

    [Fact]
    public void RootCommand_ShouldHaveSharedRecursiveOptions()
    {
        var root = CommandDefinitions.Build();

        var optionNames = root.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain([
            "--config", "--verbose",
            "--base-url", "--username", "--token", "--space-key", "--auth-type",
            "--dry-run", "--recursive"
        ]);
    }

    [Fact]
    public void DownloadCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var download = root.Subcommands.First(c => c.Name == "download");

        var optionNames = download.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--page-id", "--page-title", "--output-dir", "--overwrite-strategy"]);
    }

    [Fact]
    public void UploadCommand_ShouldContainUpdateAndCreateSubcommands()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");

        upload.Subcommands.Select(c => c.Name).Should().Contain(["update", "create"]);
    }

    [Fact]
    public void UploadUpdateCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var update = upload.Subcommands.First(c => c.Name == "update");

        var optionNames = update.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--source-dir", "--page-id", "--page-title", "--on-error", "--move-pages"]);
    }

    [Fact]
    public void UploadCreateCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var create = upload.Subcommands.First(c => c.Name == "create");

        var optionNames = create.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--source-dir", "--parent-id", "--parent-title"]);
    }

    [Fact]
    public void CompareCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var compare = root.Subcommands.First(c => c.Name == "compare");

        var optionNames = compare.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--page-id", "--page-title", "--output-dir", "--match-by-title"]);
    }

    [Fact]
    public void ConfigShowCommand_ShouldContainAllCommandSpecificOptions()
    {
        var root = CommandDefinitions.Build();
        var config = root.Subcommands.First(c => c.Name == "config");
        var show = config.Subcommands.First(c => c.Name == "show");

        var optionNames = show.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain([
            "--page-id", "--page-title", "--output-dir", "--overwrite-strategy",
            "--source-dir", "--on-error", "--move-pages",
            "--parent-id", "--parent-title", "--match-by-title"
        ]);
    }

    [Fact]
    public void SharedOptions_ShouldBeRecognizedInSubcommands()
    {
        var root = CommandDefinitions.Build();

        root.Parse("download --base-url https://x.com --page-id 1").Errors.Should().BeEmpty();
        root.Parse("upload update --token t --source-dir ./src").Errors.Should().BeEmpty();
        root.Parse("upload create --username u --source-dir ./src").Errors.Should().BeEmpty();
        root.Parse("compare --space-key S --page-id 1").Errors.Should().BeEmpty();
        root.Parse("config show --base-url https://x.com --page-id 1").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldDetectUnknownCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("nonexistent");

        pr.Errors.Should().NotBeEmpty();
    }
}
