using ConfluencePageExporter.Infrastructure;
using Shouldly;

namespace ConfluencePageExporter.Tests.Infrastructure;

public class BootstrapParserTests
{
    [Fact]
    public void Parse_ShouldExtractDownloadCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --base-url https://x.com");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBe("download");
        result.ConfigPath.ShouldBeNull();
        result.Verbose.ShouldBeFalse();
    }

    [Fact]
    public void Parse_ShouldExtractUploadUpdateCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload update --source-dir ./pages");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBe("upload update");
    }

    [Fact]
    public void Parse_ShouldExtractUploadCreateCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("upload create --source-dir ./pages");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBe("upload create");
    }

    [Fact]
    public void Parse_ShouldExtractCompareCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("compare --page-id 123");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBe("compare");
    }

    [Fact]
    public void Parse_ShouldExtractConfigShowCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("config show");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBe("config show");
    }

    [Fact]
    public void Parse_ShouldReturnEmptyPath_WhenNoCommandSpecified()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("");

        var result = BootstrapParser.Parse(pr);

        result.CommandPath.ShouldBeEmpty();
    }

    [Fact]
    public void Parse_ShouldExtractVerboseFlag_BeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--verbose download --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.Verbose.ShouldBeTrue();
        result.CommandPath.ShouldBe("download");
    }

    [Fact]
    public void Parse_ShouldExtractVerboseFlag_AfterCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --verbose --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.Verbose.ShouldBeTrue();
        result.CommandPath.ShouldBe("download");
    }

    [Fact]
    public void Parse_ShouldExtractConfigPath_BeforeCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("--config my-config.json download --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.ShouldBe("my-config.json");
    }

    [Fact]
    public void Parse_ShouldExtractConfigPath_AfterCommand()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse("download --config my-config.json --page-id 1");

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.ShouldBe("my-config.json");
    }

    [Fact]
    public void Parse_ShouldNormalizeConfigPath()
    {
        var root = CommandDefinitions.Build();
        var pr = root.Parse(new[] { "--config", "\"/tmp/My Config.json\"", "download", "--page-id", "1" });

        var result = BootstrapParser.Parse(pr);

        result.ConfigPath.ShouldBe("/tmp/My Config.json");
    }
}
