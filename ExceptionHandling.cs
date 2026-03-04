using System.Net.Http;

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
