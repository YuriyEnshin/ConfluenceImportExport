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
    public void DownloadCommand_ShouldContainExpectedOptions()
    {
        var root = CommandDefinitions.Build();
        var download = root.Subcommands.First(c => c.Name == "download");

        var optionNames = download.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--page-id", "--page-title", "--output-dir", "--recursive", "--overwrite-strategy"]);
        optionNames.Should().Contain(["--base-url", "--username", "--token", "--space-key", "--auth-type", "--dry-run"]);
    }

    [Fact]
    public void UploadCommand_ShouldContainUpdateAndCreateSubcommands()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");

        upload.Subcommands.Select(c => c.Name).Should().Contain(["update", "create"]);
    }

    [Fact]
    public void UploadUpdateCommand_ShouldContainExpectedOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var update = upload.Subcommands.First(c => c.Name == "update");

        var optionNames = update.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--source-dir", "--page-id", "--page-title", "--on-error", "--move-pages", "--recursive"]);
    }

    [Fact]
    public void UploadCreateCommand_ShouldContainExpectedOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var create = upload.Subcommands.First(c => c.Name == "create");

        var optionNames = create.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--source-dir", "--parent-id", "--parent-title", "--recursive"]);
    }

    [Fact]
    public void CompareCommand_ShouldContainExpectedOptions()
    {
        var root = CommandDefinitions.Build();
        var compare = root.Subcommands.First(c => c.Name == "compare");

        var optionNames = compare.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--page-id", "--page-title", "--output-dir", "--recursive", "--match-by-title"]);
    }

    [Fact]
    public void ConfigCommand_ShouldContainShowSubcommand()
    {
        var root = CommandDefinitions.Build();
        var config = root.Subcommands.First(c => c.Name == "config");

        config.Subcommands.Select(c => c.Name).Should().Contain("show");
    }

    [Fact]
    public void RootCommand_ShouldHaveGlobalOptions()
    {
        var root = CommandDefinitions.Build();

        var optionNames = root.Options.Select(o => o.Name).ToList();
        optionNames.Should().Contain(["--config", "--verbose"]);
    }

    [Fact]
    public void Parse_ShouldDetectUnknownCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("nonexistent");

        pr.Errors.Should().NotBeEmpty();
    }
}
