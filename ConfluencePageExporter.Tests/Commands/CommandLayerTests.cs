using System.CommandLine;
using ConfluencePageExporter.Commands;
using ConfluencePageExporter.Models;
using ConfluencePageExporter.Tests.Helpers;
using FluentAssertions;

namespace ConfluencePageExporter.Tests.Commands;

public class CommandLayerTests
{
    [Fact]
    public void DownloadCommand_ShouldContainExpectedOptions()
    {
        var handler = new DownloadCommandHandler(LoggerTestHelper.CreateLoggerFactory());

        var command = handler.CreateCommand();

        command.Name.Should().Be("download");
        command.Options.Select(o => o.ToString()).Should().Contain(s => s.Contains("--page-id", StringComparison.Ordinal));
        command.Options.Select(o => o.ToString()).Should().Contain(s => s.Contains("--page-title", StringComparison.Ordinal));
        command.Options.Select(o => o.ToString()).Should().Contain(s => s.Contains("--recursive", StringComparison.Ordinal));
        command.Options.Select(o => o.ToString()).Should().Contain(s => s.Contains("--overwrite-strategy", StringComparison.Ordinal));
    }

    [Fact]
    public void UploadCommand_ShouldContainUpdateAndCreateSubcommands()
    {
        var handler = new UploadCommandHandler(LoggerTestHelper.CreateLoggerFactory());

        var command = handler.CreateCommand();

        command.Name.Should().Be("upload");
        command.Subcommands.Select(c => c.Name).Should().Contain(["update", "create"]);
    }

    [Fact]
    public void CompareCommand_ShouldContainMatchByTitleOption()
    {
        var handler = new CompareCommandHandler(LoggerTestHelper.CreateLoggerFactory());

        var command = handler.CreateCommand();

        command.Name.Should().Be("compare");
        command.Options.Select(o => o.ToString()).Should().Contain(s => s.Contains("--match-by-title", StringComparison.Ordinal));
    }

    [Fact]
    public void EnumOption_ShouldRejectInvalidValues()
    {
        var enumOption = CommandOptionsBuilder.CreateEnumOption("--mode", "mode", "a", "b");
        var command = new Command("test") { enumOption };

        var parseResult = command.Parse("--mode c");

        parseResult.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task DownloadCommand_ShouldPrintError_WhenBothPageIdAndPageTitleProvided()
    {
        var handler = new DownloadCommandHandler(LoggerTestHelper.CreateLoggerFactory());
        var command = handler.CreateCommand();

        using var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var parseResult = command.Parse([
                "--base-url", "https://example.com",
                "--username", "user",
                "--token", "token",
                "--space-key", "DOCS",
                "--page-id", "1",
                "--page-title", "Title",
                "--output-dir", "out"
            ]);

            await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(previous);
        }

        writer.ToString().Should().ContainEquivalentOf("mutually exclusive");
    }

    [Fact]
    public async Task UploadUpdateCommand_ShouldPrintError_WhenBothPageIdAndPageTitleProvided()
    {
        var handler = new UploadCommandHandler(LoggerTestHelper.CreateLoggerFactory());
        var command = handler.CreateCommand();

        using var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var parseResult = command.Parse([
                "update",
                "--base-url", "https://example.com",
                "--username", "user",
                "--token", "token",
                "--space-key", "DOCS",
                "--source-dir", "out",
                "--page-id", "1",
                "--page-title", "Title"
            ]);

            await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(previous);
        }

        writer.ToString().Should().ContainEquivalentOf("mutually exclusive");
    }

    [Fact]
    public async Task UploadCreateCommand_ShouldPrintError_WhenBothParentIdAndParentTitleProvided()
    {
        var handler = new UploadCommandHandler(LoggerTestHelper.CreateLoggerFactory());
        var command = handler.CreateCommand();

        using var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var parseResult = command.Parse([
                "create",
                "--base-url", "https://example.com",
                "--username", "user",
                "--token", "token",
                "--space-key", "DOCS",
                "--source-dir", "out",
                "--parent-id", "1",
                "--parent-title", "Parent"
            ]);

            await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(previous);
        }

        writer.ToString().Should().ContainEquivalentOf("mutually exclusive");
    }

    [Fact]
    public async Task CompareCommand_ShouldPrintError_WhenPageIdentifierMissing()
    {
        var handler = new CompareCommandHandler(LoggerTestHelper.CreateLoggerFactory());
        var command = handler.CreateCommand();

        using var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var parseResult = command.Parse([
                "--base-url", "https://example.com",
                "--username", "user",
                "--token", "token",
                "--space-key", "DOCS",
                "--output-dir", "out"
            ]);

            await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(previous);
        }

        writer.ToString().Should().ContainEquivalentOf("Either --page-id or --page-title");
    }

    [Fact]
    public async Task DownloadCommand_ShouldApplyConfigValues_ForValidation()
    {
        var config = new AppConfig
        {
            Defaults = new DefaultConfig
            {
                BaseUrl = "https://example.com",
                Username = "user",
                Token = "token",
                SpaceKey = "DOCS",
                Download = new DownloadDefaults
                {
                    OutputDir = "out",
                    PageId = "1",
                    PageTitle = "Title"
                }
            }
        };
        var handler = new DownloadCommandHandler(LoggerTestHelper.CreateLoggerFactory(), config);
        var command = handler.CreateCommand();

        using var writer = new StringWriter();
        var previous = Console.Out;
        Console.SetOut(writer);
        try
        {
            var parseResult = command.Parse([]);
            await parseResult.InvokeAsync(cancellationToken: TestContext.Current.CancellationToken);
        }
        finally
        {
            Console.SetOut(previous);
        }

        writer.ToString().Should().ContainEquivalentOf("mutually exclusive");
    }
}
