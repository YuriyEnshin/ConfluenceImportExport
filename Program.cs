using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ConfluencePageExporter;
using ConfluencePageExporter.Commands;

class Program
{
    static async Task<int> Main(string[] args)
    {
        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging(builder => builder
            .AddConsole()
            .SetMinimumLevel(LogLevel.Debug));
        var serviceProvider = serviceCollection.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        var downloadHandler = new DownloadCommandHandler(loggerFactory);
        var uploadHandler = new UploadCommandHandler(loggerFactory);
        var compareHandler = new CompareCommandHandler(loggerFactory);

        var downloadCommand = downloadHandler.CreateCommand();
        var uploadCommand = uploadHandler.CreateCommand();
        var compareCommand = compareHandler.CreateCommand();

        var rootCommand = new RootCommand(
            "Downloads Confluence pages to local files or uploads local files to Confluence.")
        {
            downloadCommand,
            uploadCommand,
            compareCommand
        };

        rootCommand.SetAction(async (parseResult) =>
        {
            if (parseResult.CommandResult?.Command == rootCommand)
            {
                Console.WriteLine("Confluence Page Exporter");
                Console.WriteLine("========================");
                Console.WriteLine("Usage: ConfluencePageExporter <command> [arguments]");
                Console.WriteLine();
                Console.WriteLine("Commands:");
                Console.WriteLine("  download         Download Confluence pages to local files");
                Console.WriteLine("  upload update    Update existing Confluence pages from local files");
                Console.WriteLine("  upload create    Create new Confluence pages from local files");
                Console.WriteLine("  compare          Compare Confluence tree with local exported copy");
                Console.WriteLine();
                Console.WriteLine("Use \"ConfluencePageExporter <command> --help\" for details.");
                Console.WriteLine();
                Console.WriteLine("Examples:");
                Console.WriteLine("  ConfluencePageExporter download --base-url https://wiki.example.com --username user --token xyz --space-key DOCS --page-id 12345 --output-dir ./export");
                Console.WriteLine("  ConfluencePageExporter download --base-url https://wiki.example.com --username user --token xyz --space-key DOCS --page-title \"My Page\" --recursive --output-dir ./export");
                Console.WriteLine("  ConfluencePageExporter upload update --base-url https://wiki.example.com --username user --token xyz --space-key DOCS --source-dir ./export/MyPage --recursive");
                Console.WriteLine("  ConfluencePageExporter upload create --base-url https://wiki.example.com --username user --token xyz --space-key DOCS --parent-id 12345 --source-dir ./export/MyPage --recursive");
                Console.WriteLine("  ConfluencePageExporter compare --base-url https://wiki.example.com --username user --token xyz --space-key DOCS --page-id 12345 --recursive --output-dir ./export");
                return;
            }

            await parseResult.InvokeAsync();
        });

        var parseResult = rootCommand.Parse(args);
        try
        {
            return await parseResult.InvokeAsync();
        }
        catch (Exception ex)
        {
            var message = ExceptionHandling.GetUserFriendlyErrorMessage(ex);
            await Console.Error.WriteLineAsync($"Error: {message}");
            return 1;
        }
    }
}
