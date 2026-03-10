using ConfluencePageExporter.Services;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Services;

public class ConfigFileLoaderTests
{
    [Fact]
    public void Load_ShouldReturnEmptyConfig_WhenDefaultFileDoesNotExist()
    {
        using var temp = new TempDirectoryScope();
        var currentDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(temp.RootPath);
        try
        {
            var config = ConfigFileLoader.Load(null);
            config.Should().NotBeNull();
            config.Defaults.Should().NotBeNull();
        }
        finally
        {
            Directory.SetCurrentDirectory(currentDir);
        }
    }

    [Fact]
    public void Load_ShouldThrow_WhenExplicitConfigPathDoesNotExist()
    {
        var act = () => ConfigFileLoader.Load("X:\\missing-config.json");

        act.Should().Throw<FileNotFoundException>()
            .WithMessage("*Configuration file does not exist*");
    }

    [Fact]
    public void Load_ShouldParseConfig_WhenJsonIsValid()
    {
        using var temp = new TempDirectoryScope();
        var configPath = temp.WriteTextFile("config.json", """
        {
          "defaults": {
            "baseUrl": "https://wiki.example.com",
            "authType": "cloud",
            "download": {
              "overwriteStrategy": "overwrite"
            }
          }
        }
        """);

        var config = ConfigFileLoader.Load(configPath);

        config.Defaults.BaseUrl.Should().Be("https://wiki.example.com");
        config.Defaults.AuthType.Should().Be("cloud");
        config.Defaults.Download.OverwriteStrategy.Should().Be("overwrite");
    }

    [Fact]
    public void Load_ShouldThrowInvalidOperation_WhenJsonIsMalformed()
    {
        using var temp = new TempDirectoryScope();
        var configPath = temp.WriteTextFile("bad.json", "{ \"defaults\": ");

        var act = () => ConfigFileLoader.Load(configPath);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Configuration file is invalid*");
    }
}
