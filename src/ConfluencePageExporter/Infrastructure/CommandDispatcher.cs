using Microsoft.Extensions.DependencyInjection;
using ConfluencePageExporter.Commands;

namespace ConfluencePageExporter.Infrastructure;

/// <summary>
/// Resolves and executes the appropriate <see cref="ICommandHandler"/> from DI
/// based on the invoked command path. Wraps execution in exception handling.
/// </summary>
public static class CommandDispatcher
{
    public static async Task<int> DispatchAsync(
        string commandPath,
        IServiceProvider services,
        CancellationToken ct = default)
    {
        try
        {
            var handler = ResolveHandler(commandPath, services);
            if (handler == null)
            {
                await Console.Error.WriteLineAsync("Unknown command. Use --help for usage information.");
                return 1;
            }

            return await handler.ExecuteAsync(ct);
        }
        catch (Exception ex)
        {
            var message = ExceptionHandling.GetUserFriendlyErrorMessage(ex);
            await Console.Error.WriteLineAsync($"Error: {message}");
            return 1;
        }
    }

    private static ICommandHandler? ResolveHandler(string commandPath, IServiceProvider services)
    {
        return commandPath switch
        {
            "download update" => services.GetRequiredService<DownloadUpdateCommandHandler>(),
            "download merge" => services.GetRequiredService<DownloadMergeCommandHandler>(),
            "upload update" => services.GetRequiredService<UploadUpdateCommandHandler>(),
            "upload create" => services.GetRequiredService<UploadCreateCommandHandler>(),
            "upload merge" => services.GetRequiredService<UploadMergeCommandHandler>(),
            "compare" => services.GetRequiredService<CompareCommandHandler>(),
            "config show" => services.GetRequiredService<ConfigShowCommandHandler>(),
            _ => null
        };
    }
}
