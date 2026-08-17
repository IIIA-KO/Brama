using Microsoft.Extensions.Logging;

namespace Brama.Http;

/// <summary>
/// Brama's own log messages, defined through the logging source generator so no allocation or
/// formatting happens for a request whose level is disabled.
/// </summary>
internal static partial class BramaLog
{
    [LoggerMessage(
        EventId = 1000,
        Message = "{Method} {Route} responded {StatusCode} in {ElapsedMilliseconds}ms (version {Version})")]
    public static partial void Request(
        ILogger logger,
        LogLevel level,
        string method,
        string route,
        int statusCode,
        double elapsedMilliseconds,
        int? version);
}
