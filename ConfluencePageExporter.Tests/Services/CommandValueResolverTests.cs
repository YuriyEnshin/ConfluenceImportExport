using System.CommandLine;
using ConfluencePageExporter.Services;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Services;

public class CommandValueResolverTests
{
    [Fact]
    public void ResolveOptionalString_ShouldPreferCli_WhenOptionSpecified()
    {
        var option = new Option<string>("--name");
        var command = new Command("test") { option };
        var parseResult = command.Parse("--name cli");

        var value = CommandValueResolver.ResolveOptionalString(parseResult, option, "config");

        value.Should().Be("cli");
    }

    [Fact]
    public void ResolveOptionalString_ShouldFallbackToConfig_WhenOptionNotSpecified()
    {
        var option = new Option<string>("--name");
        var command = new Command("test") { option };
        var parseResult = command.Parse(string.Empty);

        var value = CommandValueResolver.ResolveOptionalString(parseResult, option, "config");

        value.Should().Be("config");
    }

    [Fact]
    public void ResolveBool_ShouldPreferCli_WhenFlagSpecified()
    {
        var option = new Option<bool>("--dry-run");
        var command = new Command("test") { option };
        var parseResult = command.Parse("--dry-run");

        var value = CommandValueResolver.ResolveBool(parseResult, option, false);

        value.Should().BeTrue();
    }

    [Fact]
    public void ResolveEnum_ShouldThrow_WhenConfigValueIsInvalid()
    {
        var option = new Option<string>("--mode");
        var command = new Command("test") { option };
        var parseResult = command.Parse(string.Empty);

        var act = () => CommandValueResolver.ResolveEnum(parseResult, option, "invalid", "a", "--mode", "a", "b");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Invalid value 'invalid' for --mode*");
    }

    [Fact]
    public void ExtractConfigPathFromArgs_ShouldSupportBothFormats()
    {
        var fromPair = CommandValueResolver.ExtractConfigPathFromArgs(["--config", "app.json"]);
        var fromEquals = CommandValueResolver.ExtractConfigPathFromArgs(["--config=app.json"]);

        fromPair.Should().Be("app.json");
        fromEquals.Should().Be("app.json");
    }
}
