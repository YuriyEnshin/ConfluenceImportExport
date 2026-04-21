using ConfluencePageExporter.Infrastructure;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class BootstrapParserTests
{
    [Fact]
    public void Parse_ShouldExtractDownloadCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --base-url https://x.com");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().Be("download");
        result.ConfigPath.Should().BeNull();
        result.Verbose.Should().BeFalse();
    }

    [Fact]
    public void Parse_ShouldExtractUploadUpdateCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload update --source-dir ./pages");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().Be("upload update");
    }

    [Fact]
    public void Parse_ShouldExtractUploadCreateCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload create --source-dir ./pages");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().Be("upload create");
    }

    [Fact]
    public void Parse_ShouldExtractCompareCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("compare --page-id 123");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().Be("compare");
    }

    [Fact]
    public void Parse_ShouldExtractConfigShowCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().Be("config show");
    }

    [Fact]
    public void Parse_ShouldReturnEmptyPath_WhenNoCommandSpecified()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShouldExtractVerboseFlag_BeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--verbose download --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.Verbose.Should().BeTrue();
        result.CommandPath.Should().Be("download");
    }

    [Fact]
    public void Parse_ShouldExtractVerboseFlag_AfterCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --verbose --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.Verbose.Should().BeTrue();
        result.CommandPath.Should().Be("download");
    }

    [Fact]
    public void Parse_ShouldExtractConfigPath_BeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--config my-config.json download --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.Should().Be("my-config.json");
    }

    [Fact]
    public void Parse_ShouldExtractConfigPath_AfterCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --config my-config.json --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.Should().Be("my-config.json");
    }

    [Fact]
    public void Parse_ShouldNormalizeConfigPath()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse(new[] { "--config", "\"/tmp/My Config.json\"", "download", "--page-id", "1" });

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.Should().Be("/tmp/My Config.json");
    }
}
