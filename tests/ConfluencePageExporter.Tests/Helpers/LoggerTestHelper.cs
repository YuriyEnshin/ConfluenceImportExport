using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ConfluencePageExporter.Tests.Helpers;

public static class LoggerTestHelper
{
    public static ILogger<T> CreateLogger<T>() => NullLogger<T>.Instance;

    public static ILoggerFactory CreateLoggerFactory() => NullLoggerFactory.Instance;
}
