using System.Net.Http;
using ConfluencePageExporter.Services;

namespace ConfluencePageExporter;

/// <summary>
/// Provides user-friendly error messages for exceptions that may occur during command execution.
/// </summary>
public static class ExceptionHandling
{
    /// <summary>
    /// Returns a user-friendly error message for the given exception.
    /// </summary>
    public static string GetUserFriendlyErrorMessage(Exception ex)
    {
        return ex switch
        {
            ConfluenceConflictException => $"{ex.Message} Подсказка: выполните 'download merge' для синхронизации серверных правок, затем повторите загрузку.",
            ConfluenceApiException => ex.Message,
            DirectoryNotFoundException => ex.Message,
            FileNotFoundException => ex.Message,
            InvalidOperationException => ex.Message,
            ArgumentException => ex.Message,
            UnauthorizedAccessException => ex.Message,
            IOException => ex.Message,
            HttpRequestException => ex.InnerException != null
                ? $"{ex.Message} ({ex.InnerException.Message})"
                : ex.Message,
            _ => ex.Message
        };
    }
}
