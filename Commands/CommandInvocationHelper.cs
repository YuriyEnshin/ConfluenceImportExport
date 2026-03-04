namespace ConfluencePageExporter.Commands;

/// <summary>
/// Helper for command invocation with user-friendly exception handling.
/// </summary>
internal static class CommandInvocationHelper
{
    /// <summary>
    /// Executes the given async action. On exception, writes a user-friendly message to stderr and exits with code 1.
    /// </summary>
    public static async Task RunAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex);
        }
    }

    /// <summary>
    /// Executes the given async action and returns its result. On exception, writes a user-friendly message to stderr and exits with code 1.
    /// </summary>
    public static async Task<T> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(ex);
            throw; // Unreachable - Environment.Exit never returns
        }
    }

    private static async Task HandleExceptionAsync(Exception ex)
    {
        var message = ExceptionHandling.GetUserFriendlyErrorMessage(ex);
        await Console.Error.WriteLineAsync($"Error: {message}");
        Environment.Exit(1);
    }
}
