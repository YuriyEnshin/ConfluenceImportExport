using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Commands;

public class CommandLayerTests
{
    [Fact]
    public void RootCommand_ShouldContainAllTopLevelCommands()
    {
        var root = CommandDefinitions.Build();

        new[] { "download", "upload", "compare", "config" }
            .ShouldBeSubsetOf(root.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void RootCommand_ShouldHaveSharedRecursiveOptions()
    {
        var root = CommandDefinitions.Build();

        var optionNames = root.Options.Select(o => o.Name).ToList();
        new[]
        {
            "--config", "--verbose",
            "--base-url", "--username", "--token", "--space-key", "--auth-type",
            "--dry-run", "--recursive", "--report"
        }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void DownloadCommand_ShouldContainUpdateAndMergeSubcommands()
    {
        var root = CommandDefinitions.Build();
        var download = root.Subcommands.First(c => c.Name == "download");

        new[] { "update", "merge" }.ShouldBeSubsetOf(download.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void DownloadUpdateCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var download = root.Subcommands.First(c => c.Name == "download");
        var update = download.Subcommands.First(c => c.Name == "update");

        var optionNames = update.Options.Select(o => o.Name).ToList();
        new[] { "--page-id", "--page-title", "--output-dir" }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void UploadCommand_ShouldContainUpdateCreateAndMergeSubcommands()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");

        new[] { "update", "create", "merge" }.ShouldBeSubsetOf(upload.Subcommands.Select(c => c.Name));
    }

    [Fact]
    public void UploadUpdateCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var update = upload.Subcommands.First(c => c.Name == "update");

        var optionNames = update.Options.Select(o => o.Name).ToList();
        new[] { "--source-dir", "--page-id", "--page-title" }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void UploadCreateCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var upload = root.Subcommands.First(c => c.Name == "upload");
        var create = upload.Subcommands.First(c => c.Name == "create");

        var optionNames = create.Options.Select(o => o.Name).ToList();
        new[] { "--source-dir", "--parent-id", "--parent-title" }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void CompareCommand_ShouldContainOwnOptions()
    {
        var root = CommandDefinitions.Build();
        var compare = root.Subcommands.First(c => c.Name == "compare");

        var optionNames = compare.Options.Select(o => o.Name).ToList();
        new[] { "--page-id", "--page-title", "--output-dir", "--match-by-title" }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void ConfigShowCommand_ShouldContainAllCommandSpecificOptions()
    {
        var root = CommandDefinitions.Build();
        var config = root.Subcommands.First(c => c.Name == "config");
        var show = config.Subcommands.First(c => c.Name == "show");

        var optionNames = show.Options.Select(o => o.Name).ToList();
        new[]
        {
            "--page-id", "--page-title", "--output-dir",
            "--source-dir",
            "--parent-id", "--parent-title", "--match-by-title", "--detect-source"
        }.ShouldBeSubsetOf(optionNames);
    }

    [Fact]
    public void SharedOptions_ShouldBeRecognizedInSubcommands()
    {
        var root = CommandDefinitions.Build();

        root.Parse("download update --base-url https://x.com --page-id 1").Errors.ShouldBeEmpty();
        root.Parse("download merge --base-url https://x.com --page-id 1").Errors.ShouldBeEmpty();
        root.Parse("upload update --token t --source-dir ./src").Errors.ShouldBeEmpty();
        root.Parse("upload create --username u --source-dir ./src").Errors.ShouldBeEmpty();
        root.Parse("upload merge --token t --source-dir ./src").Errors.ShouldBeEmpty();
        root.Parse("compare --space-key S --page-id 1").Errors.ShouldBeEmpty();
        root.Parse("config show --base-url https://x.com --page-id 1").Errors.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ShouldDetectUnknownCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("nonexistent");

        pr.Errors.ShouldNotBeEmpty();
    }
}
